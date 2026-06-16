using System.Text.RegularExpressions;

namespace SupplierIntelligence.Api.Services;

public sealed partial class HttpWebsiteCertificationDiscovery(HttpClient httpClient) : IWebsiteCertificationDiscovery
{
    public async Task<IReadOnlyList<DiscoveredCertification>> DiscoverAsync(
        Uri websiteUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            var html = await httpClient.GetStringAsync(websiteUrl, cancellationToken);
            var text = HtmlTagRegex().Replace(html, " ");
            var standards = FindStandards(text);

            return standards
                .Select(standard => new DiscoveredCertification(
                    standard,
                    $"WEBSITE-{standard.Replace(" ", "-", StringComparison.OrdinalIgnoreCase)}",
                    "Supplier website claim",
                    null,
                    $"Discovered {standard} claim on supplier website. This is not registry-verified yet."))
                .ToList();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return [];
        }
    }

    private static IReadOnlyList<string> FindStandards(string text)
    {
        var standards = new List<string>();

        AddIfFound(standards, text, Iso9001Regex(), "ISO 9001");
        AddIfFound(standards, text, Iso14001Regex(), "ISO 14001");
        AddIfFound(standards, text, Iso45001Regex(), "ISO 45001");
        AddIfFound(standards, text, Iatf16949Regex(), "IATF 16949");

        return standards;
    }

    private static void AddIfFound(
        List<string> standards,
        string text,
        Regex regex,
        string standard)
    {
        if (regex.IsMatch(text) && !standards.Contains(standard))
        {
            standards.Add(standard);
        }
    }

    [GeneratedRegex("<[^>]+>", RegexOptions.Compiled)]
    private static partial Regex HtmlTagRegex();

    [GeneratedRegex("ISO\\s*9001", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Iso9001Regex();

    [GeneratedRegex("ISO\\s*14001", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Iso14001Regex();

    [GeneratedRegex("ISO\\s*45001", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Iso45001Regex();

    [GeneratedRegex("IATF\\s*16949", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex Iatf16949Regex();
}
