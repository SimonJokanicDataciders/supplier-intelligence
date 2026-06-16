namespace SupplierIntelligence.Api.Models;

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string CountryCode { get; set; } = string.Empty;
    public string Industry { get; set; } = string.Empty;
    public string? WebsiteUrl { get; set; }
    public string? RegistryNumber { get; set; }
    public string? VatNumber { get; set; }
    public string? CertificationHints { get; set; }
    public SupplierRiskLevel RiskLevel { get; set; } = SupplierRiskLevel.Unknown;
    public bool IsArchived { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<Certification> Certifications { get; set; } = [];
    public List<SourceCheck> SourceChecks { get; set; } = [];
    public List<ResearchSource> ResearchSources { get; set; } = [];
    public List<SupplierFact> SupplierFacts { get; set; } = [];
    public List<RiskAssessment> RiskAssessments { get; set; } = [];
    public List<AnalysisJob> AnalysisJobs { get; set; } = [];
}
