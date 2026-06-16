namespace SupplierIntelligence.Api.Contracts;

public sealed record LocalModelInfo(
    string Name,
    long SizeBytes,
    DateTimeOffset? ModifiedAt);

public sealed record LocalModelStatusResponse(
    string Provider,
    string BaseUrl,
    string DefaultModel,
    bool IsReachable,
    string? ErrorMessage,
    IReadOnlyList<LocalModelInfo> Models);

public sealed record GenerateSupplierBriefingRequest
{
    public string? Model { get; init; }
    public string? Focus { get; init; }
}

public sealed record GenerateRiskAssessmentResponse(
    int SupplierId,
    string SupplierName,
    RiskAssessmentResponse RiskAssessment);
