using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Contracts;

public sealed record CreateSupplierRequest(
    string Name,
    string CountryCode,
    string Industry,
    string? WebsiteUrl,
    string? RegistryNumber,
    string? VatNumber,
    string? CertificationHints,
    bool RunInitialAnalysis);

public sealed record AddCertificationRequest(
    string Standard,
    string CertificateNumber,
    string Issuer,
    DateOnly? ValidUntil,
    bool IsVerified);

public sealed record VerifyCertificationRequest(
    string Standard,
    string CertificateNumber,
    string Issuer,
    DateOnly? ValidUntil);

public sealed record AddSourceCheckRequest(
    string SourceName,
    string Url,
    SourceCheckStatus Status,
    string Notes);

public sealed record CheckSourceEvidenceRequest(
    string SourceName,
    string Url);

public sealed record CreateSupplierMatchCandidateRequest(
    string CandidateName,
    string? CountryCode,
    string? WebsiteUrl,
    string? SourceName,
    string? SourceUrl,
    int ConfidenceScore,
    string MatchReason);

public sealed record CreateRiskAssessmentRequest(
    SupplierRiskLevel RiskLevel,
    int Score,
    string Focus,
    string SummaryMarkdown);

public sealed record UpdateRiskAssessmentRequest(
    SupplierRiskLevel RiskLevel,
    int Score,
    string Focus,
    string SummaryMarkdown);

public sealed record SupplierSummaryResponse(
    int Id,
    string Name,
    string CountryCode,
    string Industry,
    string? WebsiteUrl,
    string? RegistryNumber,
    string? VatNumber,
    string? CertificationHints,
    SupplierRiskLevel RiskLevel,
    bool IsArchived,
    int CertificationCount,
    int SourceCheckCount,
    int RiskAssessmentCount);

public sealed record SupplierDetailResponse(
    int Id,
    string Name,
    string CountryCode,
    string Industry,
    string? WebsiteUrl,
    string? RegistryNumber,
    string? VatNumber,
    string? CertificationHints,
    SupplierRiskLevel RiskLevel,
    bool IsArchived,
    DateTime CreatedAt,
    IReadOnlyList<AnalysisJobResponse> AnalysisJobs,
    IReadOnlyList<CertificationResponse> Certifications,
    IReadOnlyList<SourceCheckResponse> SourceChecks,
    IReadOnlyList<ResearchSourceResponse> ResearchSources,
    IReadOnlyList<SupplierFactResponse> SupplierFacts,
    IReadOnlyList<RiskAssessmentResponse> RiskAssessments);

public sealed record AnalysisJobResponse(
    int Id,
    string JobType,
    AnalysisJobStatus Status,
    string ProgressMessage,
    string? ErrorMessage,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt);

public sealed record SupplierMatchCandidateResponse(
    int Id,
    string CandidateName,
    string? CountryCode,
    string? WebsiteUrl,
    string? SourceName,
    string? SourceUrl,
    int ConfidenceScore,
    string MatchReason,
    SupplierMatchCandidateStatus Status,
    DateTime CreatedAt,
    DateTime? ReviewedAt);

public sealed record SupplierReviewSummaryResponse(
    int SupplierId,
    string SupplierName,
    string Headline,
    SupplierReviewNextActionResponse NextAction,
    IReadOnlyList<string> KnownInformation,
    IReadOnlyList<string> MissingInformation,
    SupplierTrustSignalsResponse TrustSignals);

public sealed record SupplierReviewNextActionResponse(
    string Type,
    string Title,
    string Description,
    string ButtonLabel,
    string Step);

public sealed record SupplierTrustSignalsResponse(
    string Identity,
    string Evidence,
    string Certifications,
    string Risk);

public sealed record SupplierAnalyticsResponse(
    int SupplierId,
    string SupplierName,
    int OverallTrustScore,
    IReadOnlyList<TrustBreakdownItemResponse> TrustBreakdown,
    IReadOnlyList<SourceMixItemResponse> SourceMix,
    IReadOnlyList<TimelineItemResponse> Timeline,
    IReadOnlyList<string> StrongestSignals,
    IReadOnlyList<string> WeakestGaps);

public sealed record TrustBreakdownItemResponse(
    string Label,
    int Score,
    string Status,
    string Explanation);

public sealed record SourceMixItemResponse(
    string Label,
    int Count,
    string Status);

public sealed record TimelineItemResponse(
    DateTime OccurredAt,
    string Type,
    string Title,
    string Description,
    string Status);

public sealed record CertificationResponse(
    int Id,
    string Standard,
    string CertificateNumber,
    string Issuer,
    DateOnly? ValidUntil,
    bool IsVerified,
    string VerificationNotes,
    DateTime CreatedAt);

public sealed record SourceCheckResponse(
    int Id,
    string SourceName,
    string Url,
    SourceCheckStatus Status,
    string Notes,
    DateTime CheckedAt);

public sealed record ResearchSourceResponse(
    int Id,
    int? SourceCheckId,
    string SourceName,
    string Url,
    ResearchSourceKind Kind,
    SourceCheckStatus Status,
    FactConfidence Relevance,
    string Summary,
    DateTime CreatedAt);

public sealed record SupplierFactResponse(
    int Id,
    int? ResearchSourceId,
    SupplierFactType FactType,
    string Value,
    string EvidenceText,
    string SourceName,
    string SourceUrl,
    FactConfidence Confidence,
    DateTime CreatedAt);

public sealed record RiskAssessmentResponse(
    int Id,
    SupplierRiskLevel RiskLevel,
    int Score,
    string Focus,
    string SummaryMarkdown,
    string Provider,
    string Model,
    string? PromptFocus,
    string? EvidenceSnapshotJson,
    int? GenerationDurationMs,
    DateTime CreatedAt);
