namespace SupplierIntelligence.Api.Models;

public class SourceCheck
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public string SourceName { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public SourceCheckStatus Status { get; set; } = SourceCheckStatus.NotChecked;
    public string Notes { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
}
