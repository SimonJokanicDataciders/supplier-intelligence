namespace SupplierIntelligence.Api.Models;

public class RiskAssessment
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public SupplierRiskLevel RiskLevel { get; set; } = SupplierRiskLevel.Unknown;
    public int Score { get; set; }
    public string Focus { get; set; } = string.Empty;
    public string SummaryMarkdown { get; set; } = string.Empty;
    public string Provider { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string? PromptFocus { get; set; }
    public string? EvidenceSnapshotJson { get; set; }
    public int? GenerationDurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
