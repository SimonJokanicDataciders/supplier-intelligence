using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public interface ISourceEvidenceChecker
{
    Task<SourceEvidenceCheckResult> CheckAsync(Uri url, CancellationToken cancellationToken);
}

public sealed record SourceEvidenceCheckResult(
    SourceCheckStatus Status,
    string Notes);
