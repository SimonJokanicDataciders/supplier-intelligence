using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public sealed partial class HttpSourceEvidenceChecker(HttpClient httpClient) : ISourceEvidenceChecker
{
    public async Task<SourceEvidenceCheckResult> CheckAsync(Uri url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "SupplierIntelligenceLearningApp/1.0");
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                return new SourceEvidenceCheckResult(
                    SourceCheckStatus.Blocked,
                    "Source returned an intermediate challenge page instead of supplier evidence.");
            }

            if (response.IsSuccessStatusCode)
            {
                var html = await response.Content.ReadAsStringAsync(cancellationToken);
                var extractedContent = ExtractContent(url, html);
                var title = extractedContent.Title;
                var description = extractedContent.Description;
                var snippet = extractedContent.Text;

                if (title.Equals("No matching Wikidata entity found", StringComparison.OrdinalIgnoreCase))
                {
                    return new SourceEvidenceCheckResult(
                        SourceCheckStatus.Failed,
                        "Structured source did not return an entity matching the supplier name.");
                }

                if (title.Equals("No usable structured content found", StringComparison.OrdinalIgnoreCase))
                {
                    return new SourceEvidenceCheckResult(
                        SourceCheckStatus.Failed,
                        "Structured source did not return usable supplier evidence.");
                }

                if (IsAntiBotOrSearchBoilerplate(title, snippet))
                {
                    return new SourceEvidenceCheckResult(
                        SourceCheckStatus.Blocked,
                        $"Source did not provide usable supplier evidence. Title: {title}.");
                }

                var contentNotes = string.IsNullOrWhiteSpace(snippet)
                    ? $"Automated check reached the source with HTTP {(int)response.StatusCode}."
                    : $"Reached source with HTTP {(int)response.StatusCode}. Title: {title}. Description: {description}. Text: {snippet}";

                return new SourceEvidenceCheckResult(
                    SourceCheckStatus.Reachable,
                    Truncate(contentNotes, 1000));
            }

            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
            {
                return new SourceEvidenceCheckResult(
                    SourceCheckStatus.Blocked,
                    $"Automated check was blocked with HTTP {(int)response.StatusCode}.");
            }

            return new SourceEvidenceCheckResult(
                SourceCheckStatus.Failed,
                $"Automated check returned HTTP {(int)response.StatusCode}.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SourceEvidenceCheckResult(
                SourceCheckStatus.Failed,
                "Automated check timed out.");
        }
        catch (HttpRequestException exception)
        {
            return new SourceEvidenceCheckResult(
                SourceCheckStatus.Failed,
                $"Automated check failed: {exception.Message}");
        }
    }

    private static string ExtractTitle(string html)
    {
        var match = TitleRegex().Match(html);
        return match.Success
            ? Truncate(NormalizeText(StripHtml(match.Groups["title"].Value)), 160)
            : "No title found";
    }

    private static ExtractedSourceContent ExtractContent(Uri sourceUrl, string content)
    {
        var trimmed = content.TrimStart();
        if (trimmed.StartsWith('{'))
        {
            var jsonContent = TryExtractJsonContent(sourceUrl, trimmed);
            if (jsonContent is not null)
            {
                return jsonContent;
            }

            return new ExtractedSourceContent(
                "No usable structured content found",
                "No usable structured evidence found",
                string.Empty);
        }

        return new ExtractedSourceContent(
            ExtractTitle(content),
            ExtractDescription(content),
            Truncate(NormalizeText(StripHtml(content)), 650));
    }

    private static ExtractedSourceContent? TryExtractJsonContent(Uri sourceUrl, string content)
    {
        try
        {
            using var document = JsonDocument.Parse(content);
            var root = document.RootElement;
            var title = ReadString(root, "title") ?? ReadString(root, "displaytitle") ?? "No title found";
            var description = ReadString(root, "description") ?? "No description found";
            var extract = ReadString(root, "extract") ?? ReadString(root, "text") ?? string.Empty;

            if (string.IsNullOrWhiteSpace(extract) &&
                root.TryGetProperty("search", out var search) &&
                search.ValueKind == JsonValueKind.Array &&
                search.GetArrayLength() > 0)
            {
                var first = search[0];
                var label = ReadString(first, "label") ?? title;
                var entityDescription = ReadString(first, "description") ?? description;
                var conceptUri = ReadString(first, "concepturi");

                if (IsWikidataSearch(sourceUrl) &&
                    !IsLikelyEntityMatch(sourceUrl, label, entityDescription))
                {
                    return new ExtractedSourceContent(
                        "No matching Wikidata entity found",
                        "No matching structured entity found",
                        string.Empty);
                }

                title = label;
                description = entityDescription;
                extract = string.IsNullOrWhiteSpace(conceptUri)
                    ? $"{label}: {entityDescription}"
                    : $"{label}: {entityDescription}. Entity: {conceptUri}";
            }

            if (string.IsNullOrWhiteSpace(extract) &&
                IsWikidataSearch(sourceUrl) &&
                root.TryGetProperty("search", out var emptySearch) &&
                emptySearch.ValueKind == JsonValueKind.Array &&
                emptySearch.GetArrayLength() == 0)
            {
                return new ExtractedSourceContent(
                    "No matching Wikidata entity found",
                    "No matching structured entity found",
                    string.Empty);
            }

            if (string.IsNullOrWhiteSpace(extract))
            {
                return null;
            }

            return new ExtractedSourceContent(
                Truncate(NormalizeText(StripHtml(title)), 160),
                Truncate(NormalizeText(StripHtml(description)), 240),
                Truncate(NormalizeText(StripHtml(extract)), 650));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }

    private static bool IsWikidataSearch(Uri sourceUrl)
    {
        return sourceUrl.Host.Contains("wikidata.org", StringComparison.OrdinalIgnoreCase) &&
            sourceUrl.Query.Contains("wbsearchentities", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLikelyEntityMatch(Uri sourceUrl, string label, string description)
    {
        var query = ReadQueryParameter(sourceUrl.Query, "search");
        if (string.IsNullOrWhiteSpace(query))
        {
            return true;
        }

        var normalizedQuery = NormalizeIdentityText(query);
        var normalizedLabel = NormalizeIdentityText(label);
        var normalizedDescription = NormalizeIdentityText(description);

        if (string.IsNullOrWhiteSpace(normalizedQuery) ||
            string.IsNullOrWhiteSpace(normalizedLabel))
        {
            return false;
        }

        return normalizedLabel == normalizedQuery ||
            normalizedLabel.Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase) ||
            normalizedQuery.Contains(normalizedLabel, StringComparison.OrdinalIgnoreCase) ||
            normalizedDescription.Contains($" {normalizedQuery} ", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadQueryParameter(string query, string name)
    {
        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2 &&
                parts[0].Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return WebUtility.UrlDecode(parts[1].Replace("+", " "));
            }
        }

        return null;
    }

    private static string NormalizeIdentityText(string value)
    {
        var normalized = Regex.Replace(value, "[^a-zA-Z0-9]+", " ").Trim().ToLowerInvariant();
        return $" {WhiteSpaceRegex().Replace(normalized, " ")} ";
    }

    private static string ExtractDescription(string html)
    {
        var match = DescriptionRegex().Match(html);
        return match.Success
            ? Truncate(NormalizeText(WebUtility.HtmlDecode(match.Groups["description"].Value)), 240)
            : "No description found";
    }

    private static string StripHtml(string html)
    {
        var withoutScripts = ScriptRegex().Replace(html, " ");
        var withoutStyles = StyleRegex().Replace(withoutScripts, " ");
        var withoutTags = HtmlTagRegex().Replace(withoutStyles, " ");
        return WebUtility.HtmlDecode(withoutTags);
    }

    private static string NormalizeText(string value)
    {
        return WhiteSpaceRegex().Replace(value, " ").Trim();
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        return value[..maxLength].TrimEnd() + "...";
    }

    private static bool IsAntiBotOrSearchBoilerplate(string title, string snippet)
    {
        return title.Contains("HAProxy Challenge", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("Captcha", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("DuckDuckGo All Regions", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("bots use DuckDuckGo", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("Select all squares", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("No results found", StringComparison.OrdinalIgnoreCase);
    }

    [GeneratedRegex("<script\\b[^>]*>.*?</script>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex ScriptRegex();

    [GeneratedRegex("<style\\b[^>]*>.*?</style>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex StyleRegex();

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhiteSpaceRegex();

    [GeneratedRegex("<title[^>]*>(?<title>.*?)</title>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex TitleRegex();

    [GeneratedRegex("<meta\\s+[^>]*(?:name|property)=[\"'](?:description|og:description)[\"'][^>]*content=[\"'](?<description>[^\"']*)[\"'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex DescriptionRegex();
}

public sealed record ExtractedSourceContent(
    string Title,
    string Description,
    string Text);
