using System.Net;
using System.Text.RegularExpressions;

namespace SupplierIntelligence.Api.Services;

public sealed partial class HttpWebsiteResearcher(HttpClient httpClient) : IWebsiteResearcher
{
    private static readonly string[] ImportantPathTerms =
    [
        "about",
        "about-us",
        "company",
        "who-we-are",
        "what-we-do",
        "products",
        "product",
        "services",
        "solutions",
        "industries",
        "markets",
        "bearings",
        "seals",
        "lubrication",
        "manufacturing",
        "production",
        "imprint",
        "impressum",
        "legal",
        "contact",
        "quality",
        "cert",
        "certificate",
        "certification",
        "compliance",
        "sustainability",
        "environment",
        "responsibility",
        "esg",
        "governance",
        "code-of-conduct",
        "policies",
        "supplier",
        "suppliers",
        "investors",
        "annual-report",
        "locations",
        "privacy"
    ];

    private static readonly string[] EvidenceTerms =
    [
        "ISO 9001",
        "ISO 14001",
        "ISO 45001",
        "ISO 27001",
        "IATF 16949",
        "AS9100",
        "ISO 50001",
        "ISO 13485",
        "quality management",
        "environmental management",
        "energy management",
        "information security",
        "certified",
        "certificate",
        "compliance",
        "sustainability",
        "ESG",
        "code of conduct",
        "supplier code",
        "manufactures",
        "supplies",
        "products",
        "services",
        "markets",
        "industries",
        "locations",
        "headquartered",
        "founded",
        "imprint",
        "VAT",
        "registration",
        "commercial register"
    ];

    public async Task<WebsiteResearchResult> ResearchAsync(Uri websiteUrl, CancellationToken cancellationToken)
    {
        var pages = new List<WebsiteResearchPage>();
        var importantLinks = new List<string>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var homePage = await FetchPageAsync("Homepage", websiteUrl, cancellationToken);
        if (homePage is not null)
        {
            pages.Add(homePage);
            visited.Add(NormalizeUrl(homePage.Url));

            var links = (await ExtractImportantLinksFromSitemapAsync(websiteUrl, cancellationToken))
                .Concat(await ExtractImportantLinksFromHtmlAsync(websiteUrl, cancellationToken))
                .Concat(ExtractImportantLinks(websiteUrl, homePage.TextSnippet))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(ScoreImportantUrl)
                .Take(12)
                .ToList();

            importantLinks.AddRange(links);

            foreach (var link in links.Take(7))
            {
                if (!Uri.TryCreate(link, UriKind.Absolute, out var pageUri))
                {
                    continue;
                }

                if (!visited.Add(NormalizeUrl(pageUri.ToString())))
                {
                    continue;
                }

                var page = await FetchPageAsync(ClassifyPage(pageUri), pageUri, cancellationToken);
                if (page is not null)
                {
                    pages.Add(page);
                }
            }
        }

        return new WebsiteResearchResult(
            pages,
            pages.SelectMany(page => page.MatchedTerms).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            importantLinks);
    }

    private async Task<WebsiteResearchPage?> FetchPageAsync(
        string pageType,
        Uri url,
        CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", "SupplierIntelligenceLearningApp/1.0");
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var html = await response.Content.ReadAsStringAsync(cancellationToken);
            var title = ExtractTitle(html);
            var description = ExtractDescription(html);
            var text = NormalizeText(StripHtml(html));
            var matchedTerms = EvidenceTerms
                .Where(term => text.Contains(term, StringComparison.OrdinalIgnoreCase))
                .Take(8)
                .ToList();

            return new WebsiteResearchPage(
                pageType,
                url.ToString(),
                title,
                description,
                Truncate(text, 900),
                matchedTerms);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or InvalidOperationException)
        {
            return null;
        }
    }

    private async Task<IReadOnlyList<string>> ExtractImportantLinksFromHtmlAsync(
        Uri websiteUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await httpClient.GetStringAsync(websiteUrl, cancellationToken);
            return LinkRegex()
                .Matches(html)
                .Select(match => WebUtility.HtmlDecode(match.Groups["href"].Value))
                .Select(href => ToAbsoluteUrl(websiteUrl, href))
                .Where(url => url is not null)
                .Select(url => url!)
                .Where(url => IsInternalUrl(websiteUrl, url))
                .Where(IsImportantUrl)
                .OrderByDescending(ScoreImportantUrl)
                .Take(30)
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    private async Task<IReadOnlyList<string>> ExtractImportantLinksFromSitemapAsync(
        Uri websiteUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var sitemapUrl = new Uri(websiteUrl, "/sitemap.xml");
            var xml = await httpClient.GetStringAsync(sitemapUrl, cancellationToken);

            return SitemapLocationRegex()
                .Matches(xml)
                .Select(match => WebUtility.HtmlDecode(match.Groups["url"].Value.Trim()))
                .Where(url => IsInternalUrl(websiteUrl, url))
                .Where(IsImportantUrl)
                .OrderByDescending(ScoreImportantUrl)
                .Take(40)
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> ExtractImportantLinks(Uri websiteUrl, string text)
    {
        return ImportantPathTerms
            .Select(term => new Uri(websiteUrl, "/" + term).ToString())
            .Where(IsImportantUrl)
            .ToList();
    }

    private static string? ToAbsoluteUrl(Uri root, string href)
    {
        if (string.IsNullOrWhiteSpace(href) ||
            href.StartsWith('#') ||
            href.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("tel:", StringComparison.OrdinalIgnoreCase) ||
            href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Uri.TryCreate(root, href, out var absolute)
            ? absolute.ToString()
            : null;
    }

    private static bool IsInternalUrl(Uri root, string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var absolute) &&
            absolute.Host.Equals(root.Host, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImportantUrl(string url)
    {
        return ImportantPathTerms.Any(term => url.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static int ScoreImportantUrl(string url)
    {
        var score = 0;

        if (url.Contains("cert", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("quality", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("compliance", StringComparison.OrdinalIgnoreCase))
        {
            score += 30;
        }

        if (url.Contains("about", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("company", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("who-we-are", StringComparison.OrdinalIgnoreCase))
        {
            score += 25;
        }

        if (url.Contains("product", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("service", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("solution", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("industr", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("market", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        if (url.Contains("sustainability", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("environment", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("esg", StringComparison.OrdinalIgnoreCase))
        {
            score += 15;
        }

        if (url.Contains("imprint", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("impressum", StringComparison.OrdinalIgnoreCase) ||
            url.Contains("legal", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static string ClassifyPage(Uri url)
    {
        var value = url.ToString();

        if (value.Contains("cert", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("quality", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("compliance", StringComparison.OrdinalIgnoreCase))
        {
            return "Certification and quality";
        }

        if (value.Contains("imprint", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("impressum", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("legal", StringComparison.OrdinalIgnoreCase))
        {
            return "Legal profile";
        }

        if (value.Contains("contact", StringComparison.OrdinalIgnoreCase))
        {
            return "Contact profile";
        }

        if (value.Contains("product", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("service", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("solution", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("industr", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("market", StringComparison.OrdinalIgnoreCase))
        {
            return "Products and markets";
        }

        if (value.Contains("sustainability", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("environment", StringComparison.OrdinalIgnoreCase))
        {
            return "Sustainability profile";
        }

        return "Company profile";
    }

    private static string ExtractTitle(string html)
    {
        var match = TitleRegex().Match(html);
        return match.Success
            ? Truncate(NormalizeText(StripHtml(match.Groups["title"].Value)), 160)
            : "No title found";
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
        var withSpaces = HtmlTagRegex().Replace(withoutStyles, " ");
        return WebUtility.HtmlDecode(withSpaces);
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

    private static string NormalizeUrl(string url)
    {
        return url.TrimEnd('/');
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

    [GeneratedRegex("<a\\s+[^>]*href=[\"'](?<href>[^\"']+)[\"'][^>]*>", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex LinkRegex();

    [GeneratedRegex("<loc>\\s*(?<url>.*?)\\s*</loc>", RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.Compiled)]
    private static partial Regex SitemapLocationRegex();
}
