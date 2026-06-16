using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public interface IResearchFactExtractor
{
    Task RefreshFactsAsync(
        Supplier supplier,
        AppDbContext db,
        CancellationToken cancellationToken);
}
