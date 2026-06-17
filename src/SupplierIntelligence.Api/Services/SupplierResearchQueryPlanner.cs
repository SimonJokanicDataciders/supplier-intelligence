using System.Text.RegularExpressions;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public sealed partial class SupplierResearchQueryPlanner : ISupplierResearchQueryPlanner
{
    public IReadOnlyList<SupplierResearchQuery> PlanQueries(Supplier supplier)
    {
        var name = Clean(supplier.Name);
        var country = Clean(supplier.CountryCode);
        var industry = Clean(supplier.Industry);
        var hasWebsite = !string.IsNullOrWhiteSpace(supplier.WebsiteUrl);
        var hasRegistryNumber = !string.IsNullOrWhiteSpace(supplier.RegistryNumber);
        var hasVatNumber = !string.IsNullOrWhiteSpace(supplier.VatNumber);
        var hasCertificationHints = !string.IsNullOrWhiteSpace(supplier.CertificationHints);
        var queries = new List<SupplierResearchQuery>();

        Add(queries,
            "identity",
            Quote(name) + $" {country} {industry} official website company profile",
            "Find the most likely public identity, official website, country fit, and short company description.");

        Add(queries,
            "products",
            Quote(name) + $" {industry} products services manufacturer supplier",
            "Find what the supplier sells, manufactures, distributes, or services.");

        if (!hasWebsite)
        {
            Add(queries,
                "market-profile",
                Quote(name) + $" {country} supplier manufacturer exporter company profile",
                "Find marketplace, manufacturer-directory, trade-directory, or public profile evidence when no website was provided.");
        }

        if (hasCertificationHints)
        {
            Add(queries,
                "certification-hints",
                Quote(name) + $" {supplier.CertificationHints} certificate certification quality management",
                "Check whether the supplied certification hints can be supported by public evidence.");
        }
        else
        {
            Add(queries,
                "quality",
                Quote(name) + " ISO 9001 ISO 14001 IATF 16949 certificate quality management",
                "Search for certification, quality-management, environmental-management, or compliance evidence.");
        }

        if (hasRegistryNumber)
        {
            Add(queries,
                "registry-number",
                Quote(name) + $" {supplier.RegistryNumber} company registry registration",
                "Check registry-number evidence without falling back to generic registry pages.");
        }

        if (hasVatNumber)
        {
            Add(queries,
                "tax-identifier",
                Quote(name) + $" {supplier.VatNumber} VAT tax registration",
                "Check VAT or tax identifier evidence.");
        }

        Add(queries,
            "external-evidence",
            Quote(name) + $" {country} {industry} reviews news profile",
            "Find independent non-Wikipedia evidence that confirms identity, products, locations, or risk-relevant public information.");

        return queries
            .GroupBy(query => NormalizeQuery(query.Query), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(hasWebsite ? 4 : 5)
            .ToList();
    }

    private static void Add(
        List<SupplierResearchQuery> queries,
        string label,
        string query,
        string goal)
    {
        var cleanedQuery = Clean(query);
        if (string.IsNullOrWhiteSpace(cleanedQuery))
        {
            return;
        }

        queries.Add(new SupplierResearchQuery(label, cleanedQuery, goal));
    }

    private static string Quote(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : $"\"{value}\"";
    }

    private static string Clean(string value)
    {
        return WhiteSpaceRegex()
            .Replace(value.Trim(), " ");
    }

    private static string NormalizeQuery(string value)
    {
        return WhiteSpaceRegex()
            .Replace(value.Trim().ToLowerInvariant(), " ");
    }

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhiteSpaceRegex();
}
