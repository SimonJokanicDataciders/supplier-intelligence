using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Services;

public static class EvidenceQualityCalculator
{
    public static EvidenceQualitySummary Calculate(Supplier supplier)
    {
        var hasVerifiedCertification = supplier.Certifications.Any(certification => certification.IsVerified);
        var hasWebsiteCertificationClaim = supplier.Certifications.Any(certification =>
            certification.Issuer.Contains("website", StringComparison.OrdinalIgnoreCase) ||
            certification.VerificationNotes.Contains("website", StringComparison.OrdinalIgnoreCase));
        var hasReachableWebsite = supplier.SourceChecks.Any(sourceCheck =>
            sourceCheck.Status == SourceCheckStatus.Reachable &&
            IsWebsiteSource(sourceCheck));
        var hasReachableRegistrySource = supplier.SourceChecks.Any(sourceCheck =>
            sourceCheck.Status == SourceCheckStatus.Reachable &&
            IsRegistrySource(sourceCheck));
        var hasReachableVatSource = supplier.SourceChecks.Any(sourceCheck =>
            sourceCheck.Status == SourceCheckStatus.Reachable &&
            IsVatSource(sourceCheck));
        var hasBlockedOrFailedSource = supplier.SourceChecks.Any(sourceCheck =>
            sourceCheck.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed);

        var score = 0;

        if (hasVerifiedCertification)
        {
            score += 40;
        }
        else if (hasWebsiteCertificationClaim)
        {
            score += 10;
        }

        if (hasReachableWebsite)
        {
            score += 10;
        }

        if (hasReachableRegistrySource)
        {
            score += 25;
        }

        if (hasReachableVatSource)
        {
            score += 15;
        }

        if (hasBlockedOrFailedSource)
        {
            score -= 20;
        }

        score = Math.Clamp(score, 0, 100);

        return new EvidenceQualitySummary(
            score,
            ToBand(score),
            hasVerifiedCertification,
            hasWebsiteCertificationClaim,
            hasReachableWebsite,
            hasReachableRegistrySource,
            hasReachableVatSource,
            hasBlockedOrFailedSource);
    }

    public static SupplierRiskLevel EstimateRiskLevel(Supplier supplier)
    {
        var quality = Calculate(supplier);

        if (quality.HasBlockedOrFailedSource)
        {
            return SupplierRiskLevel.Medium;
        }

        if (quality is { Score: >= 75, HasVerifiedCertification: true })
        {
            return SupplierRiskLevel.Low;
        }

        if (quality.Score >= 35)
        {
            return SupplierRiskLevel.Medium;
        }

        return SupplierRiskLevel.Unknown;
    }

    public static int EstimateRiskScore(Supplier supplier)
    {
        var quality = Calculate(supplier);
        var score = quality.HasVerifiedCertification
            ? 80 - quality.Score
            : 100 - quality.Score;

        if (quality.HasBlockedOrFailedSource)
        {
            score += 20;
        }

        return Math.Clamp(score, 0, 100);
    }

    public static IReadOnlyList<string> BuildMissingEvidenceSuggestions(Supplier supplier)
    {
        var quality = Calculate(supplier);
        var suggestions = new List<string>();

        if (!quality.HasVerifiedCertification)
        {
            suggestions.Add("Add at least one registry-verified certification, for example ISO 9001 or an industry-specific certificate.");
        }

        if (!quality.HasReachableRegistrySource)
        {
            suggestions.Add(
                string.IsNullOrWhiteSpace(supplier.RegistryNumber)
                    ? "Add company registry evidence."
                    : "Verify the provided registry identifier against an external registry.");
        }

        if (!quality.HasReachableVatSource && !string.IsNullOrWhiteSpace(supplier.VatNumber))
        {
            suggestions.Add("Verify the provided VAT identifier through a VAT validation source.");
        }

        if (!quality.HasReachableWebsite)
        {
            suggestions.Add("Add or check a reachable supplier website.");
        }

        if (quality.HasBlockedOrFailedSource)
        {
            suggestions.Add("Review blocked or failed source checks.");
        }

        return suggestions;
    }

    public static string ClassifySource(SourceCheck sourceCheck)
    {
        if (IsVatSource(sourceCheck))
        {
            return "VatReference";
        }

        if (IsRegistrySource(sourceCheck))
        {
            return "RegistryReference";
        }

        if (IsWebsiteSource(sourceCheck))
        {
            return "SupplierWebsite";
        }

        return "Other";
    }

    private static string ToBand(int score)
    {
        return score switch
        {
            >= 75 => "Strong",
            >= 35 => "Partial",
            _ => "Sparse"
        };
    }

    private static bool IsWebsiteSource(SourceCheck sourceCheck)
    {
        return sourceCheck.SourceName.Contains("website", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsRegistrySource(SourceCheck sourceCheck)
    {
        if (sourceCheck.SourceName.Contains("search", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return sourceCheck.SourceName.Contains("registry reference", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.SourceName.Contains("company registry", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Url.Contains("opencorporates", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Url.Contains("firmenbuch", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsVatSource(SourceCheck sourceCheck)
    {
        return sourceCheck.SourceName.Contains("VAT", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Url.Contains("vies", StringComparison.OrdinalIgnoreCase);
    }
}

public sealed record EvidenceQualitySummary(
    int Score,
    string Band,
    bool HasVerifiedCertification,
    bool HasWebsiteCertificationClaim,
    bool HasReachableWebsite,
    bool HasReachableRegistrySource,
    bool HasReachableVatSource,
    bool HasBlockedOrFailedSource);
