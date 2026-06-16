using Microsoft.EntityFrameworkCore;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Data;

public static class LearningDataSeeder
{
    public static async Task ResetAsync(AppDbContext db, CancellationToken cancellationToken)
    {
        var suppliers = await db.Suppliers.ToListAsync(cancellationToken);
        db.Suppliers.RemoveRange(suppliers);
        await db.SaveChangesAsync(cancellationToken);

        var supplier = new Supplier
        {
            Name = "Learning Demo Supplier",
            CountryCode = "AT",
            Industry = "Industrial testing",
            WebsiteUrl = "https://learning-demo-supplier.test",
            RiskLevel = SupplierRiskLevel.Low,
            Certifications =
            [
                new Certification
                {
                    Standard = "ISO 9001",
                    CertificateNumber = "LEARN-ISO-9001-001",
                    Issuer = "Learning Certification Authority",
                    ValidUntil = DateOnly.FromDateTime(DateTime.UtcNow.Date.AddYears(1)),
                    IsVerified = true,
                    VerificationNotes = "Seeded verified learning certificate."
                }
            ],
            SourceChecks =
            [
                new SourceCheck
                {
                    SourceName = "Company website",
                    Url = "https://learning-demo-supplier.test",
                    Status = SourceCheckStatus.Reachable,
                    Notes = "Demo source check for learning the evidence workflow."
                },
                new SourceCheck
                {
                    SourceName = "Company registry",
                    Url = "https://learning-demo-supplier.test/registry",
                    Status = SourceCheckStatus.Reachable,
                    Notes = "Demo registry source check for learning validation and audit snapshots."
                }
            ],
            RiskAssessments =
            [
                new RiskAssessment
                {
                    RiskLevel = SupplierRiskLevel.Low,
                    Score = 20,
                    Focus = "Seeded learning assessment.",
                    SummaryMarkdown = "Seeded assessment: verified ISO 9001 and two reachable source checks support a low initial risk level.",
                    Provider = "Seed",
                    Model = "LearningDataSeeder",
                    PromptFocus = "Seeded learning assessment.",
                    EvidenceSnapshotJson = """
                        {
                          "note": "Seeded evidence snapshot for the learning demo supplier."
                        }
                        """
                }
            ]
        };

        db.Suppliers.Add(supplier);
        await db.SaveChangesAsync(cancellationToken);
    }
}
