using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public interface ISourceDiscoveryPlanner
{
    IReadOnlyList<SourceDiscoveryTarget> PlanTargets(Supplier supplier);
}

public sealed record SourceDiscoveryTarget(
    string SourceName,
    Uri Url,
    string Reason);
