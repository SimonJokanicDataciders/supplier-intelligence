namespace SupplierIntelligence.Api.Models;

public class SupplierMatchCandidate
{
    public int Id { get; set; }
    public int SupplierId { get; set; }
    public Supplier Supplier { get; set; } = null!;

    public string CandidateName { get; set; } = string.Empty;
    public string? CountryCode { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? SourceName { get; set; }
    public string? SourceUrl { get; set; }
    public int ConfidenceScore { get; set; }
    public string MatchReason { get; set; } = string.Empty;
    public SupplierMatchCandidateStatus Status { get; set; } = SupplierMatchCandidateStatus.Proposed;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReviewedAt { get; set; }
}
