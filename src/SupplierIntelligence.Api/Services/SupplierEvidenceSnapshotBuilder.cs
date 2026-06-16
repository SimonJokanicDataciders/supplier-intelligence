using System.Text.Json;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public static class SupplierEvidenceSnapshotBuilder
{
    public static string Build(
        Supplier supplier,
        SupplierRiskLevel estimatedRiskLevel,
        int estimatedRiskScore)
    {
        var evidenceQuality = EvidenceQualityCalculator.Calculate(supplier);
        var automationFindings = BuildAutomationFindings(supplier, evidenceQuality);
        var nextChecks = BuildNextChecks(supplier, evidenceQuality);
        var expectedEvidence = BuildExpectedEvidence(supplier);
        var companySummary = BuildCompanySummary(supplier);
        var evidence = new
        {
            SupplierProfile = new
            {
                supplier.Id,
                supplier.Name,
                supplier.CountryCode,
                supplier.Industry,
                supplier.WebsiteUrl,
                supplier.RegistryNumber,
                supplier.VatNumber,
                supplier.CertificationHints,
                InputDepth = CalculateInputDepth(supplier),
                IsSparseInput = IsSparseInput(supplier)
            },
            RiskDecision = new
            {
                Level = estimatedRiskLevel.ToString(),
                Score = estimatedRiskScore,
                IsDraft = true,
                Reason = BuildRiskDecisionReason(supplier, evidenceQuality)
            },
            EvidenceCompleteness = new
            {
                evidenceQuality.HasVerifiedCertification,
                HasAnyCertification = supplier.Certifications.Count > 0,
                HasReachableSource = supplier.SourceChecks.Any(sourceCheck => sourceCheck.Status == SourceCheckStatus.Reachable),
                HasRegistrySource = evidenceQuality.HasReachableRegistrySource,
                HasVatSource = evidenceQuality.HasReachableVatSource,
                evidenceQuality.HasBlockedOrFailedSource
            },
            EvidenceQuality = evidenceQuality,
            CompanySummary = companySummary,
            SupplierFacts = supplier.SupplierFacts
                .OrderBy(fact => fact.FactType)
                .ThenByDescending(fact => fact.Confidence)
                .Select(fact => new
                {
                    Type = fact.FactType.ToString(),
                    fact.Value,
                    fact.SourceName,
                    fact.SourceUrl,
                    Confidence = fact.Confidence.ToString()
                }),
            ResearchSources = supplier.ResearchSources
                .OrderByDescending(source => source.Relevance)
                .ThenBy(source => source.SourceName)
                .Select(source => new
                {
                    source.SourceName,
                    source.Url,
                    Kind = source.Kind.ToString(),
                    Status = source.Status.ToString(),
                    Relevance = source.Relevance.ToString(),
                    source.Summary
                }),
            AutomationFindings = automationFindings,
            ExpectedEvidence = expectedEvidence,
            MissingEvidenceSuggestions = EvidenceQualityCalculator.BuildMissingEvidenceSuggestions(supplier),
            RecommendedNextChecks = nextChecks,
            Certifications = supplier.Certifications.Select(certification => new
            {
                certification.Standard,
                certification.CertificateNumber,
                certification.Issuer,
                certification.ValidUntil,
                certification.IsVerified,
                certification.VerificationNotes
            }),
            SourceChecks = supplier.SourceChecks.Select(sourceCheck => new
            {
                sourceCheck.SourceName,
                EvidenceType = EvidenceQualityCalculator.ClassifySource(sourceCheck),
                sourceCheck.Url,
                Status = sourceCheck.Status.ToString(),
                Notes = BuildSourceCheckNotesForModel(sourceCheck),
                sourceCheck.CheckedAt
            })
        };

        return JsonSerializer.Serialize(
            evidence,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                WriteIndented = true
            });
    }

    private static IReadOnlyList<string> BuildAutomationFindings(
        Supplier supplier,
        EvidenceQualitySummary evidenceQuality)
    {
        var findings = new List<string>
        {
            $"Supplier profile created for {supplier.Name} in {supplier.CountryCode} covering {supplier.Industry}.",
            supplier.WebsiteUrl is null
                ? "No website is available for public profile research."
                : evidenceQuality.HasReachableWebsite
                    ? "Supplier website is available as public source evidence."
                    : "Supplier website availability is not confirmed.",
            supplier.Certifications.Count == 0
                ? "No certification claim was found in available public evidence."
                : $"{supplier.Certifications.Count} certification claim is available for review.",
            evidenceQuality.HasReachableRegistrySource
                ? "Company registry evidence is available."
                : "Company registry evidence is not verified yet.",
            evidenceQuality.HasReachableVatSource
                ? "VAT evidence is available."
                : string.IsNullOrWhiteSpace(supplier.VatNumber)
                    ? "VAT evidence is not available yet."
                    : "VAT evidence is not verified yet."
        };

        if (IsSparseInput(supplier))
        {
            findings.Add("Public profile is limited until identifiers or certificates are available.");
        }

        return findings;
    }

    private static CompanySummary BuildCompanySummary(Supplier supplier)
    {
        var factDescription = supplier.SupplierFacts
            .Where(fact => fact.FactType == SupplierFactType.CompanyDescription)
            .OrderByDescending(fact => fact.Confidence)
            .FirstOrDefault();

        var factExternalHighlights = supplier.SupplierFacts
            .Where(fact => fact.FactType is SupplierFactType.CompanyDescription or SupplierFactType.ProductsAndServices)
            .OrderByDescending(fact => fact.Confidence)
            .Take(5)
            .Select(fact => $"{fact.SourceName}: {fact.Value}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var usefulSources = supplier.SourceChecks
            .Where(sourceCheck => sourceCheck.Status == SourceCheckStatus.Reachable)
            .Select(sourceCheck => new
            {
                sourceCheck.SourceName,
                sourceCheck.Url,
                Snippet = ExtractUsefulSnippet(sourceCheck.Notes)
            })
            .Where(source => !string.IsNullOrWhiteSpace(source.Snippet))
            .Where(source => !IsLowInformationSnippet(source.Snippet))
            .OrderByDescending(source => SourcePriority(source.SourceName, source.Snippet))
            .ToList();

        var ownWebsiteSources = usefulSources
            .Where(source => IsOwnWebsiteSource(source.SourceName))
            .Take(2)
            .Select(source => FormatEvidenceHighlight(source.SourceName, source.Snippet))
            .ToList();

        var externalSources = usefulSources
            .Where(source => !IsOwnWebsiteSource(source.SourceName))
            .Take(5)
            .Select(source => FormatEvidenceHighlight(source.SourceName, source.Snippet))
            .ToList();

        var bestDescription = usefulSources
            .FirstOrDefault(source => !IsOwnWebsiteSource(source.SourceName))
            ?.Snippet ??
            usefulSources.FirstOrDefault(source => IsOwnWebsiteSource(source.SourceName))?.Snippet ??
            $"{supplier.Name} is listed as a supplier in {supplier.Industry}, but the current evidence does not yet explain the company in detail.";

        return new CompanySummary(
            CleanHighlight(factDescription?.Value ?? bestDescription),
            ownWebsiteSources,
            factExternalHighlights.Count > 0 ? factExternalHighlights : externalSources,
            usefulSources.Select(source => new EvidenceSourceSummary(
                    source.SourceName,
                    source.Url,
                    CleanHighlight(source.Snippet)))
                .Take(7)
                .ToList());
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

    private static bool IsLowInformationSnippet(string snippet)
    {
        var normalized = snippet.Trim();
        return normalized.Length < 40 ||
            normalized.Equals("SKF", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("No description found.", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("enable JavaScript", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("verify you are human", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Captcha", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("HAProxy Challenge", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("DuckDuckGo All Regions", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("bots use DuckDuckGo", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Select all squares", StringComparison.OrdinalIgnoreCase) ||
            normalized.Contains("Jump to content", StringComparison.OrdinalIgnoreCase);
    }

    private static int SourcePriority(string sourceName, string snippet)
    {
        if (sourceName.Contains("summary", StringComparison.OrdinalIgnoreCase))
        {
            return 100;
        }

        if (snippet.Contains("manufactures", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("supplies", StringComparison.OrdinalIgnoreCase) ||
            snippet.Contains("founded", StringComparison.OrdinalIgnoreCase))
        {
            return 80;
        }

        if (sourceName.Contains("Wikipedia", StringComparison.OrdinalIgnoreCase))
        {
            return 60;
        }

        if (sourceName.Contains("Wikidata", StringComparison.OrdinalIgnoreCase))
        {
            return 40;
        }

        if (IsOwnWebsiteSource(sourceName))
        {
            return 20;
        }

        return 10;
    }

    private static bool IsOwnWebsiteSource(string sourceName)
    {
        return sourceName.Contains("supplier website", StringComparison.OrdinalIgnoreCase) ||
            sourceName.Contains("website research", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatEvidenceHighlight(string sourceName, string snippet)
    {
        return $"{sourceName}: {snippet}";
    }

    private static string CleanHighlight(string value)
    {
        var cleaned = value
            .Replace("Reached source with HTTP 200.", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("Title:", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Trim();

        return cleaned.Length <= 450
            ? cleaned
            : cleaned[..447].TrimEnd() + "...";
    }

    private static string BuildSourceCheckNotesForModel(SourceCheck sourceCheck)
    {
        if (sourceCheck.Status == SourceCheckStatus.Blocked)
        {
            return "Source was blocked or did not provide usable supplier evidence.";
        }

        if (sourceCheck.Status == SourceCheckStatus.Failed)
        {
            return "Source could not be verified.";
        }

        var snippet = ExtractUsefulSnippet(sourceCheck.Notes);
        if (string.IsNullOrWhiteSpace(snippet) || IsLowInformationSnippet(snippet))
        {
            return "No useful supplier-specific text extracted from this source.";
        }

        return CleanHighlight(snippet);
    }

    private static IReadOnlyList<string> BuildExpectedEvidence(Supplier supplier)
    {
        var industry = supplier.Industry.Trim().ToLowerInvariant();
        var expected = new List<string>
        {
            "Company registry extract or official registration page",
            "Public website imprint, legal notice, or corporate profile",
            "Validated VAT or tax identifier where applicable"
        };

        if (industry.Contains("manufactur") ||
            industry.Contains("automotive") ||
            industry.Contains("defense") ||
            industry.Contains("aerospace") ||
            industry.Contains("industrial"))
        {
            expected.Add("ISO 9001 quality management certificate");
            expected.Add("ISO 14001 environmental management certificate when sustainability is relevant");
        }

        if (industry.Contains("automotive"))
        {
            expected.Add("IATF 16949 automotive quality certificate");
        }

        if (industry.Contains("aerospace") || industry.Contains("defense"))
        {
            expected.Add("AS9100 or comparable aerospace and defense quality evidence");
        }

        if (industry.Contains("software") || industry.Contains("it") || industry.Contains("cloud") || industry.Contains("data"))
        {
            expected.Add("ISO 27001 or comparable information security evidence");
        }

        return expected.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static IReadOnlyList<string> BuildNextChecks(
        Supplier supplier,
        EvidenceQualitySummary evidenceQuality)
    {
        var checks = new List<string>();

        if (string.IsNullOrWhiteSpace(supplier.RegistryNumber))
        {
            checks.Add("Find the official company registry number before treating the supplier as verified.");
        }

        if (string.IsNullOrWhiteSpace(supplier.VatNumber))
        {
            checks.Add("Find and validate the VAT or tax identifier if the supplier operates in a VAT jurisdiction.");
        }

        if (!evidenceQuality.HasVerifiedCertification)
        {
            checks.Add("Confirm at least one relevant certification through an issuer, registry, or certificate document.");
        }

        if (!evidenceQuality.HasReachableWebsite)
        {
            checks.Add("Confirm the official supplier website and legal company imprint.");
        }

        if (!evidenceQuality.HasReachableRegistrySource)
        {
            checks.Add("Check an official registry or OpenCorporates-style source for company existence.");
        }

        return checks.Take(5).ToList();
    }

    private static string BuildRiskDecisionReason(
        Supplier supplier,
        EvidenceQualitySummary evidenceQuality)
    {
        if (evidenceQuality.Score == 0)
        {
            return "No strong external evidence was confirmed yet.";
        }

        if (evidenceQuality.HasBlockedOrFailedSource)
        {
            return "At least one public evidence source could not be verified.";
        }

        if (evidenceQuality.HasVerifiedCertification && evidenceQuality.HasReachableRegistrySource)
        {
            return "Certification and registry evidence are both present.";
        }

        if (IsSparseInput(supplier))
        {
            return "Only basic supplier attributes were supplied.";
        }

        return "Evidence is partial and still needs stronger external verification.";
    }

    private static int CalculateInputDepth(Supplier supplier)
    {
        return new[]
        {
            !string.IsNullOrWhiteSpace(supplier.Name),
            !string.IsNullOrWhiteSpace(supplier.CountryCode),
            !string.IsNullOrWhiteSpace(supplier.Industry),
            !string.IsNullOrWhiteSpace(supplier.WebsiteUrl),
            !string.IsNullOrWhiteSpace(supplier.RegistryNumber),
            !string.IsNullOrWhiteSpace(supplier.VatNumber),
            !string.IsNullOrWhiteSpace(supplier.CertificationHints)
        }.Count(BooleanIsTrue);
    }

    private static bool IsSparseInput(Supplier supplier)
    {
        return string.IsNullOrWhiteSpace(supplier.RegistryNumber) &&
            string.IsNullOrWhiteSpace(supplier.VatNumber) &&
            string.IsNullOrWhiteSpace(supplier.CertificationHints);
    }

    private static bool BooleanIsTrue(bool value)
    {
        return value;
    }
}

public sealed record CompanySummary(
    string Description,
    IReadOnlyList<string> OwnWebsiteHighlights,
    IReadOnlyList<string> ExternalHighlights,
    IReadOnlyList<EvidenceSourceSummary> EvidenceSources);

public sealed record EvidenceSourceSummary(
    string SourceName,
    string Url,
    string Snippet);
