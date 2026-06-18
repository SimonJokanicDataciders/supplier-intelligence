namespace SupplierIntelligence.Api.Models;

public class CompareBoardSupplier
{
    public int Id { get; set; }
    public int CompareBoardId { get; set; }
    public CompareBoard CompareBoard { get; set; } = null!;

    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public int SortOrder { get; set; }
    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
