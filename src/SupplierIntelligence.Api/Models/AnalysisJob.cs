namespace SupplierIntelligence.Api.Models;

public class AnalysisJob
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public string JobType { get; set; } = "InitialSupplierAnalysis";
    public AnalysisJobStatus Status { get; set; } = AnalysisJobStatus.Queued;
    public string ProgressMessage { get; set; } = "Queued for analysis.";
    public string? ErrorMessage { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
}
