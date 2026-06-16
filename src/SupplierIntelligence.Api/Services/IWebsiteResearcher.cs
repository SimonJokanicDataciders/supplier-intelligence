namespace SupplierIntelligence.Api.Services;

public interface IWebsiteResearcher
{
    Task<WebsiteResearchResult> ResearchAsync(Uri websiteUrl, CancellationToken cancellationToken);
}

public sealed record WebsiteResearchResult(
    IReadOnlyList<WebsiteResearchPage> Pages,
    IReadOnlyList<string> EvidenceTerms,
    IReadOnlyList<string> ImportantLinks);

public sealed record WebsiteResearchPage(
    string PageType,
    string Url,
    string Title,
    string Description,
    string TextSnippet,
    IReadOnlyList<string> MatchedTerms);
