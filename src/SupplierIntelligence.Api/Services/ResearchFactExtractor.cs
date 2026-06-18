using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public sealed partial class ResearchFactExtractor : IResearchFactExtractor
{
    public async Task RefreshFactsAsync(
        Supplier supplier,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        await db.ResearchSources
            .Where(source => source.SupplierId == supplier.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await db.SupplierFacts
            .Where(fact => fact.SupplierId == supplier.Id)
            .ExecuteDeleteAsync(cancellationToken);

        supplier.ResearchSources.Clear();
        supplier.SupplierFacts.Clear();

        AddProfileFacts(supplier);

        foreach (var sourceCheck in supplier.SourceChecks.OrderBy(source => source.CheckedAt))
        {
            var source = BuildResearchSource(supplier, sourceCheck);
            supplier.ResearchSources.Add(source);

            if (sourceCheck.Status != SourceCheckStatus.Reachable ||
                source.Relevance == FactConfidence.Low)
            {
                continue;
            }

            AddFactsFromSource(supplier, sourceCheck, source);
        }

        AddCertificationFacts(supplier);
        AddMissingEvidenceFacts(supplier);

        await db.SaveChangesAsync(cancellationToken);
    }

    private static void AddProfileFacts(Supplier supplier)
    {
        AddFact(
            supplier,
            SupplierFactType.IndustryProfile,
            $"{supplier.Name} is tracked as a supplier in {supplier.Industry} for country {supplier.CountryCode}.",
            "Supplier intake form",
            "Supplier profile",
            string.Empty,
            FactConfidence.Medium);

        if (!string.IsNullOrWhiteSpace(supplier.WebsiteUrl))
        {
            AddFact(
                supplier,
                SupplierFactType.WebsiteEvidence,
                $"Supplier website provided: {supplier.WebsiteUrl}.",
                "Supplier intake form",
                "Supplier profile",
                supplier.WebsiteUrl,
                FactConfidence.Medium);
        }
    }

    private static ResearchSource BuildResearchSource(Supplier supplier, SourceCheck sourceCheck)
    {
        var snippet = ExtractUsefulSnippet(sourceCheck.Notes);
        var useful = sourceCheck.Status == SourceCheckStatus.Reachable && IsUsefulSnippet(snippet);

        return new ResearchSource
        {
            SupplierId = supplier.Id,
            SourceCheckId = sourceCheck.Id,
            SourceName = sourceCheck.SourceName,
            Url = sourceCheck.Url,
            Kind = ClassifySource(sourceCheck),
            Status = sourceCheck.Status,
            Relevance = useful ? EstimateConfidence(sourceCheck.SourceName, snippet) : FactConfidence.Low,
            Summary = useful ? CleanSnippet(snippet, 700) : BuildNoEvidenceSummary(sourceCheck)
        };
    }

    private static void AddFactsFromSource(
        Supplier supplier,
        SourceCheck sourceCheck,
        ResearchSource researchSource)
    {
        var snippet = CleanSnippet(ExtractUsefulSnippet(sourceCheck.Notes), 900);
        if (!IsUsefulSnippet(snippet))
        {
            return;
        }

        var confidence = EstimateConfidence(sourceCheck.SourceName, snippet);

        if (LooksLikeCompanyDescription(sourceCheck.SourceName, snippet))
        {
            AddFact(
                supplier,
                SupplierFactType.CompanyDescription,
                CleanSnippet(snippet, 700),
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                confidence,
                researchSource);
        }

        if (LooksLikeProductsAndServices(snippet))
        {
            AddFact(
                supplier,
                SupplierFactType.ProductsAndServices,
                BuildProductAndServiceFact(snippet),
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                confidence,
                researchSource);
        }

        foreach (var certification in ExtractCertificationClaims(snippet))
        {
            AddFact(
                supplier,
                SupplierFactType.CertificationClaim,
                certification,
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                FactConfidence.Medium,
                researchSource);
        }

        if (LooksLikeQualitySystem(snippet))
        {
            AddFact(
                supplier,
                SupplierFactType.QualitySystem,
                BuildFocusedFact(snippet, QualityTerms(), 700),
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                confidence,
                researchSource);
        }

        if (LooksLikeSustainabilityOrCompliance(snippet))
        {
            AddFact(
                supplier,
                SupplierFactType.SustainabilityAndCompliance,
                BuildFocusedFact(snippet, SustainabilityTerms(), 700),
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                confidence,
                researchSource);
        }

        if (LooksLikeLocationsOrMarkets(snippet))
        {
            AddFact(
                supplier,
                SupplierFactType.LocationsAndMarkets,
                BuildFocusedFact(snippet, LocationAndMarketTerms(), 700),
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                confidence,
                researchSource);
        }

        if (LooksLikeLegalIdentity(sourceCheck.SourceName, snippet))
        {
            AddFact(
                supplier,
                SupplierFactType.LegalIdentity,
                BuildFocusedFact(snippet, LegalIdentityTerms(), 700),
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                confidence,
                researchSource);
        }

        if (sourceCheck.SourceName.Contains("registry", StringComparison.OrdinalIgnoreCase) &&
            sourceCheck.Status == SourceCheckStatus.Reachable)
        {
            AddFact(
                supplier,
                SupplierFactType.RegistryEvidence,
                CleanSnippet(snippet, 500),
                snippet,
                sourceCheck.SourceName,
                sourceCheck.Url,
                FactConfidence.High,
                researchSource);
        }
    }

    private static void AddCertificationFacts(Supplier supplier)
    {
        foreach (var certification in supplier.Certifications)
        {
            var value = certification.IsVerified
                ? $"{certification.Standard} is stored as verified by {certification.Issuer}."
                : $"{certification.Standard} is stored as an unverified certification claim from {certification.Issuer}.";

            AddFact(
                supplier,
                SupplierFactType.CertificationClaim,
                value,
                certification.VerificationNotes,
                "Certification record",
                string.Empty,
                certification.IsVerified ? FactConfidence.High : FactConfidence.Medium);
        }
    }

    private static void AddMissingEvidenceFacts(Supplier supplier)
    {
        if (!supplier.Certifications.Any(certification => certification.IsVerified))
        {
            AddFact(
                supplier,
                SupplierFactType.MissingEvidence,
                "No verified certification is stored for this supplier.",
                "No verified certification record exists.",
                "Application evidence check",
                string.Empty,
                FactConfidence.High);
        }

        if (string.IsNullOrWhiteSpace(supplier.RegistryNumber) &&
            !supplier.SourceChecks.Any(source =>
                source.Status == SourceCheckStatus.Reachable &&
                source.SourceName.Contains("registry", StringComparison.OrdinalIgnoreCase) &&
                !source.SourceName.Contains("search", StringComparison.OrdinalIgnoreCase)))
        {
            AddFact(
                supplier,
                SupplierFactType.MissingEvidence,
                "No verified company registry evidence is stored for this supplier.",
                "No registry number or verified registry source exists.",
                "Application evidence check",
                string.Empty,
                FactConfidence.High);
        }

        if (string.IsNullOrWhiteSpace(supplier.VatNumber))
        {
            AddFact(
                supplier,
                SupplierFactType.MissingEvidence,
                "No VAT or tax identifier is stored for this supplier.",
                "VAT number is empty.",
                "Application evidence check",
                string.Empty,
                FactConfidence.Medium);
        }

        foreach (var sourceCheck in supplier.SourceChecks.Where(source =>
                     source.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed))
        {
            AddFact(
                supplier,
                SupplierFactType.SourceLimitation,
                $"{sourceCheck.SourceName} could not provide usable supplier evidence.",
                sourceCheck.Notes,
                sourceCheck.SourceName,
                sourceCheck.Url,
                FactConfidence.Medium);
        }
    }

    private static void AddFact(
        Supplier supplier,
        SupplierFactType factType,
        string value,
        string evidenceText,
        string sourceName,
        string sourceUrl,
        FactConfidence confidence,
        ResearchSource? researchSource = null)
    {
        var cleanedValue = CleanSnippet(value, 900);
        if (string.IsNullOrWhiteSpace(cleanedValue) ||
            supplier.SupplierFacts.Count(fact => fact.FactType == factType) >= MaxFactsForType(factType) ||
            supplier.SupplierFacts.Any(fact =>
                fact.FactType == factType &&
                IsDuplicateFactValue(fact.Value, cleanedValue)))
        {
            return;
        }

        supplier.SupplierFacts.Add(new SupplierFact
        {
            SupplierId = supplier.Id,
            ResearchSource = researchSource,
            FactType = factType,
            Value = cleanedValue,
            EvidenceText = CleanSnippet(evidenceText, 900),
            SourceName = sourceName,
            SourceUrl = sourceUrl,
            Confidence = confidence
        });
    }

    private static ResearchSourceKind ClassifySource(SourceCheck sourceCheck)
    {
        var sourceName = sourceCheck.SourceName;

        if (sourceName.Contains("supplier website", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchSourceKind.SupplierWebsite;
        }

        if (sourceName.Contains("website research", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchSourceKind.WebsiteResearch;
        }

        if (sourceName.Contains("registry", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchSourceKind.CompanyRegistry;
        }

        if (sourceName.Contains("AI web search", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchSourceKind.ExternalKnowledge;
        }

        if (sourceName.Contains("VAT", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("VIES", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchSourceKind.VatRegistry;
        }

        if (sourceName.Contains("cert", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchSourceKind.CertificationEvidence;
        }

        if (sourceName.Contains("Wikipedia", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("Wikidata", StringComparison.OrdinalIgnoreCase))
        {
            return ResearchSourceKind.ExternalKnowledge;
        }

        return ResearchSourceKind.Other;
    }

    private static FactConfidence EstimateConfidence(string sourceName, string snippet)
    {
        if (sourceName.Contains("registry", StringComparison.OrdinalIgnoreCase) &&
            !sourceName.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            return FactConfidence.High;
        }

        if (sourceName.Contains("AI web search", StringComparison.OrdinalIgnoreCase))
        {
            return snippet.Contains("Source URLs:", StringComparison.OrdinalIgnoreCase) ||
                snippet.Contains("http", StringComparison.OrdinalIgnoreCase)
                    ? FactConfidence.Medium
                    : FactConfidence.Low;
        }

        if (sourceName.Contains("supplier website", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("website research", StringComparison.OrdinalIgnoreCase))
        {
            return FactConfidence.Medium;
        }

        if (snippet.Contains("manufactures", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("supplies", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("products", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("services", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("founded", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("quality", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("sustainability", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("compliance", StringComparison.OrdinalIgnoreCase) ||
            ExtractCertificationClaims(snippet).Count > 0)
        {
            return FactConfidence.Medium;
        }

        return FactConfidence.Low;
    }

    private static string BuildNoEvidenceSummary(SourceCheck sourceCheck)
    {
        return sourceCheck.Status switch
        {
            SourceCheckStatus.Blocked => "Source was blocked or did not provide usable supplier evidence.",
            SourceCheckStatus.Failed => "Source could not be verified.",
            SourceCheckStatus.Reachable => "Source was reachable but no useful supplier-specific text was extracted.",
            _ => "Source was not checked."
        };
    }

    private static string ExtractUsefulSnippet(string notes)
    {
        var textIndex = notes.IndexOf("Text:", StringComparison.OrdinalIgnoreCase);
        if (textIndex >= 0)
        {
            return notes[(textIndex + "Text:".Length)..].Trim();
        }

        var descriptionIndex = notes.IndexOf("Description:", StringComparison.OrdinalIgnoreCase);
        if (descriptionIndex >= 0)
        {
            return notes[(descriptionIndex + "Description:".Length)..].Trim();
        }

        return notes.Trim();
    }

    private static bool IsUsefulSnippet(string snippet)
    {
        var normalized = snippet.Trim();
        return normalized.Length >= 40 &&
            !normalized.Equals("No description found.", StringComparison.OrdinalIgnoreCase) &&
            !normalized.StartsWith('{') &&
            !normalized.Contains("enable JavaScript", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("Captcha", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("HAProxy Challenge", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("DuckDuckGo All Regions", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("Jump to content", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("Skip to content", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("Main menu", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("Move to sidebar", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("all rights reserved", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("cookie policy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCompanyDescription(string sourceName, string snippet)
    {
        return sourceName.Contains("summary", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("company", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("manufacturer", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("founded", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeProductsAndServices(string snippet)
    {
        return snippet.Contains("manufactures", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("supplies", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("products", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("services", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildProductAndServiceFact(string snippet)
    {
        var matchedTerms = ProductAndServiceTerms()
            .Where(term => snippet.Contains(term, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(10)
            .ToList();

        if (matchedTerms.Count > 0)
        {
            return $"Mentions product or service areas: {string.Join(", ", matchedTerms)}.";
        }

        return BuildFocusedFact(snippet, ProductAndServiceTerms(), 500);
    }

    private static bool LooksLikeQualitySystem(string snippet)
    {
        return QualityTerms().Any(term => snippet.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeSustainabilityOrCompliance(string snippet)
    {
        return SustainabilityTerms().Any(term => snippet.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeLocationsOrMarkets(string snippet)
    {
        return LocationAndMarketTerms().Any(term => snippet.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LooksLikeLegalIdentity(string sourceName, string snippet)
    {
        return sourceName.Contains("legal", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("imprint", StringComparison.OrdinalIgnoreCase) ||
            LegalIdentityTerms().Any(term => snippet.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    private static string BuildFocusedFact(string snippet, IReadOnlyList<string> terms, int maxLength)
    {
        var sentences = SentenceRegex()
            .Split(snippet)
            .Select(sentence => sentence.Trim())
            .Where(sentence => sentence.Length >= 30)
            .Where(sentence => terms.Any(term => sentence.Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        var value = sentences.Count == 0
            ? snippet
            : string.Join(" ", sentences);

        return CleanSnippet(value, maxLength);
    }

    private static IReadOnlyList<string> QualityTerms()
    {
        return
        [
            "ISO",
            "IATF",
            "AS9100",
            "quality",
            "certificate",
            "certified",
            "management system",
            "compliance"
        ];
    }

    private static IReadOnlyList<string> ProductAndServiceTerms()
    {
        return
        [
            "bearings",
            "ball bearings",
            "roller bearings",
            "precision machine components",
            "automotive components",
            "maintenance products",
            "digital service",
            "seals",
            "lubrication",
            "linear motion",
            "mechatronics",
            "power transmission",
            "condition monitoring",
            "manufactures",
            "supplies",
            "products",
            "services",
            "solutions"
        ];
    }

    private static IReadOnlyList<string> SustainabilityTerms()
    {
        return
        [
            "sustainability",
            "environment",
            "ESG",
            "climate",
            "code of conduct",
            "supplier code",
            "responsibility",
            "governance",
            "compliance"
        ];
    }

    private static IReadOnlyList<string> LocationAndMarketTerms()
    {
        return
        [
            "headquartered",
            "headquarters",
            "founded",
            "locations",
            "location",
            "located",
            "address",
            "registered address",
            "office",
            "factory",
            "plant",
            "city",
            "province",
            "district",
            "region",
            "countries",
            "country",
            "export",
            "shipping",
            "global",
            "markets",
            "industries",
            "manufactures",
            "supplies"
        ];
    }

    private static IReadOnlyList<string> LegalIdentityTerms()
    {
        return
        [
            "legal",
            "imprint",
            "impressum",
            "registration",
            "registered",
            "VAT",
            "commercial register",
            "head office",
            "address"
        ];
    }

    private static IReadOnlyList<string> ExtractCertificationClaims(string snippet)
    {
        return CertificationRegex()
            .Matches(snippet)
            .Select(match => match.Value.ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string CleanSnippet(string value, int maxLength)
    {
        var cleaned = WhiteSpaceRegex()
            .Replace(value, " ")
            .Replace("Reached source with HTTP 200.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Title:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return cleaned.Length <= maxLength
            ? cleaned
            : cleaned[..(maxLength - 3)].TrimEnd() + "...";
    }

    private static int MaxFactsForType(SupplierFactType factType)
    {
        return factType switch
        {
            SupplierFactType.ProductsAndServices => 2,
            SupplierFactType.CompanyDescription => 2,
            SupplierFactType.QualitySystem => 2,
            SupplierFactType.SustainabilityAndCompliance => 2,
            SupplierFactType.LocationsAndMarkets => 2,
            SupplierFactType.LegalIdentity => 2,
            SupplierFactType.CertificationClaim => 8,
            SupplierFactType.SourceLimitation => 4,
            SupplierFactType.MissingEvidence => 4,
            _ => 3
        };
    }

    private static bool IsDuplicateFactValue(string existingValue, string newValue)
    {
        var existing = NormalizeFactValue(existingValue);
        var current = NormalizeFactValue(newValue);

        return existing.Equals(current, StringComparison.OrdinalIgnoreCase) ||
            existing.Contains(current, StringComparison.OrdinalIgnoreCase) ||
            current.Contains(existing, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFactValue(string value)
    {
        return WhiteSpaceRegex()
            .Replace(value.Trim().ToLowerInvariant(), " ");
    }

    [GeneratedRegex("\\b(?:ISO\\s?(?:9001|14001|45001|27001|50001|13485)|IATF\\s?16949|AS\\s?9100)\\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CertificationRegex();

    [GeneratedRegex("(?<=[.!?])\\s+", RegexOptions.Compiled)]
    private static partial Regex SentenceRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhiteSpaceRegex();
}
