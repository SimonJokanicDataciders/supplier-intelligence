using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public interface ISupplierResearchQueryPlanner
{
    IReadOnlyList<SupplierResearchQuery> PlanQueries(Supplier supplier);
}

public sealed record SupplierResearchQuery(
    string Label,
    string Query,
    string Goal);
