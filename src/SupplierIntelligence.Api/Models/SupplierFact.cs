namespace SupplierIntelligence.Api.Models;

public class SupplierFact
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int? ResearchSourceId { get; set; }
    public ResearchSource? ResearchSource { get; set; }

    public SupplierFactType FactType { get; set; }
    public string Value { get; set; } = string.Empty;
    public string EvidenceText { get; set; } = string.Empty;
    public string SourceName { get; set; } = string.Empty;
    public string SourceUrl { get; set; } = string.Empty;
    public FactConfidence Confidence { get; set; } = FactConfidence.Low;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
