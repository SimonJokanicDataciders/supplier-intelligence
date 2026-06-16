namespace SupplierIntelligence.Api.Models;

public class Certification
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public string Standard { get; set; } = string.Empty;
    public string CertificateNumber { get; set; } = string.Empty;
    public string Issuer { get; set; } = string.Empty;
    public DateOnly? ValidUntil { get; set; }
    public bool IsVerified { get; set; }
    public string VerificationNotes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
