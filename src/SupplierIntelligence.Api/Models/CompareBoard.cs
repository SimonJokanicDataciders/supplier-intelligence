namespace SupplierIntelligence.Api.Models;

public class CompareBoard
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<CompareBoardSupplier> Suppliers { get; set; } = [];
}
