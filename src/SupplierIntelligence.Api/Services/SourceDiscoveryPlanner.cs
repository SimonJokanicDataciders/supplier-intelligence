using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public sealed class SourceDiscoveryPlanner : ISourceDiscoveryPlanner
{
    public IReadOnlyList<SourceDiscoveryTarget> PlanTargets(Supplier supplier)
    {
        var targets = new List<SourceDiscoveryTarget>();

        AddRegistryTarget(targets, supplier);
        AddVatTarget(targets, supplier);

        return targets
            .GroupBy(target => target.Url.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();
    }

    private static void AddRegistryTarget(List<SourceDiscoveryTarget> targets, Supplier supplier)
    {
        if (string.IsNullOrWhiteSpace(supplier.RegistryNumber))
        {
            return;
        }

        var normalizedCountry = supplier.CountryCode.Trim().ToUpperInvariant();

        if (normalizedCountry == "AT")
        {
            targets.Add(new SourceDiscoveryTarget(
                "Austrian company registry reference",
                new Uri("https://www.justiz.gv.at/firmenbuch"),
                $"Verify Austrian registry number {supplier.RegistryNumber}."));
            return;
        }

        targets.Add(new SourceDiscoveryTarget(
            "Company registry reference",
            new Uri($"https://opencorporates.com/companies?q={Uri.EscapeDataString(supplier.RegistryNumber)}"),
            $"Search public registry data by registry number {supplier.RegistryNumber}."));
    }

    private static void AddVatTarget(List<SourceDiscoveryTarget> targets, Supplier supplier)
    {
        if (string.IsNullOrWhiteSpace(supplier.VatNumber))
        {
            return;
        }

        targets.Add(new SourceDiscoveryTarget(
            "EU VIES VAT validation reference",
            new Uri("https://ec.europa.eu/taxation_customs/vies/#/vat-validation"),
            $"Verify VAT number {supplier.VatNumber} through the EU VIES validation service."));
    }

}
