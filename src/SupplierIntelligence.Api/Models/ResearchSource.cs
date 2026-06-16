namespace SupplierIntelligence.Api.Models;

public class ResearchSource
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int? SourceCheckId { get; set; }
    public SourceCheck? SourceCheck { get; set; }

    public string SourceName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public ResearchSourceKind Kind { get; set; } = ResearchSourceKind.Other;
    public SourceCheckStatus Status { get; set; } = SourceCheckStatus.NotChecked;
    public FactConfidence Relevance { get; set; } = FactConfidence.Low;
    public string Summary { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
