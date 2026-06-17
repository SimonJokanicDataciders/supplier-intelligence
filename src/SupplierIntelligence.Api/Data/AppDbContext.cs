using Microsoft.EntityFrameworkCore;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<Certification> Certifications => Set<Certification>();
    public DbSet<SourceCheck> SourceChecks => Set<SourceCheck>();
    public DbSet<ResearchSource> ResearchSources => Set<ResearchSource>();
    public DbSet<SupplierFact> SupplierFacts => Set<SupplierFact>();
    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();
    public DbSet<AnalysisJob> AnalysisJobs => Set<AnalysisJob>();
    public DbSet<SupplierMatchCandidate> SupplierMatchCandidates => Set<SupplierMatchCandidate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Supplier>(supplier =>
        {
            supplier.Property(s => s.Name)
                .HasMaxLength(200)
                .IsRequired();

            supplier.Property(s => s.CountryCode)
                .HasMaxLength(2)
                .IsRequired();

            supplier.Property(s => s.Industry)
                .HasMaxLength(120)
                .IsRequired();

            supplier.Property(s => s.WebsiteUrl)
                .HasMaxLength(500);

            supplier.Property(s => s.RegistryNumber)
                .HasMaxLength(80);

            supplier.Property(s => s.VatNumber)
                .HasMaxLength(80);

            supplier.Property(s => s.CertificationHints)
                .HasMaxLength(500);

            supplier.Property(s => s.RiskLevel)
                .HasConversion<string>()
                .HasMaxLength(20);

            supplier.Property(s => s.IsArchived)
                .HasDefaultValue(false);
        });

        modelBuilder.Entity<AnalysisJob>(analysisJob =>
        {
            analysisJob.Property(j => j.JobType)
                .HasMaxLength(80)
                .IsRequired();

            analysisJob.Property(j => j.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            analysisJob.Property(j => j.ProgressMessage)
                .HasMaxLength(500)
                .IsRequired();

            analysisJob.Property(j => j.ErrorMessage)
                .HasMaxLength(2000);

            analysisJob.HasOne(j => j.Supplier)
                .WithMany(s => s.AnalysisJobs)
                .HasForeignKey(j => j.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SupplierMatchCandidate>(candidate =>
        {
            candidate.Property(c => c.CandidateName)
                .HasMaxLength(220)
                .IsRequired();

            candidate.Property(c => c.CountryCode)
                .HasMaxLength(2);

            candidate.Property(c => c.WebsiteUrl)
                .HasMaxLength(500);

            candidate.Property(c => c.SourceName)
                .HasMaxLength(160);

            candidate.Property(c => c.SourceUrl)
                .HasMaxLength(500);

            candidate.Property(c => c.MatchReason)
                .HasMaxLength(1000);

            candidate.Property(c => c.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            candidate.HasOne(c => c.Supplier)
                .WithMany(s => s.MatchCandidates)
                .HasForeignKey(c => c.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Certification>(certification =>
        {
            certification.Property(c => c.Standard)
                .HasMaxLength(80)
                .IsRequired();

            certification.Property(c => c.CertificateNumber)
                .HasMaxLength(120)
                .IsRequired();

            certification.Property(c => c.Issuer)
                .HasMaxLength(160)
                .IsRequired();

            certification.Property(c => c.VerificationNotes)
                .HasMaxLength(1000);

            certification.HasOne(c => c.Supplier)
                .WithMany(s => s.Certifications)
                .HasForeignKey(c => c.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<SourceCheck>(sourceCheck =>
        {
            sourceCheck.Property(s => s.SourceName)
                .HasMaxLength(120)
                .IsRequired();

            sourceCheck.Property(s => s.Url)
                .HasMaxLength(500)
                .IsRequired();

            sourceCheck.Property(s => s.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            sourceCheck.Property(s => s.Notes)
                .HasMaxLength(2500);

            sourceCheck.HasOne(s => s.Supplier)
                .WithMany(s => s.SourceChecks)
                .HasForeignKey(s => s.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ResearchSource>(researchSource =>
        {
            researchSource.Property(r => r.SourceName)
                .HasMaxLength(160)
                .IsRequired();

            researchSource.Property(r => r.Url)
                .HasMaxLength(500)
                .IsRequired();

            researchSource.Property(r => r.Kind)
                .HasConversion<string>()
                .HasMaxLength(40);

            researchSource.Property(r => r.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            researchSource.Property(r => r.Relevance)
                .HasConversion<string>()
                .HasMaxLength(20);

            researchSource.Property(r => r.Summary)
                .HasMaxLength(1000);

            researchSource.HasOne(r => r.Supplier)
                .WithMany(s => s.ResearchSources)
                .HasForeignKey(r => r.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);

            researchSource.HasOne(r => r.SourceCheck)
                .WithMany()
                .HasForeignKey(r => r.SourceCheckId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<SupplierFact>(supplierFact =>
        {
            supplierFact.Property(f => f.FactType)
                .HasConversion<string>()
                .HasMaxLength(40);

            supplierFact.Property(f => f.Value)
                .HasMaxLength(1200)
                .IsRequired();

            supplierFact.Property(f => f.EvidenceText)
                .HasMaxLength(1200);

            supplierFact.Property(f => f.SourceName)
                .HasMaxLength(160);

            supplierFact.Property(f => f.SourceUrl)
                .HasMaxLength(500);

            supplierFact.Property(f => f.Confidence)
                .HasConversion<string>()
                .HasMaxLength(20);

            supplierFact.HasOne(f => f.Supplier)
                .WithMany(s => s.SupplierFacts)
                .HasForeignKey(f => f.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);

            supplierFact.HasOne(f => f.ResearchSource)
                .WithMany()
                .HasForeignKey(f => f.ResearchSourceId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<RiskAssessment>(riskAssessment =>
        {
            riskAssessment.Property(r => r.RiskLevel)
                .HasConversion<string>()
                .HasMaxLength(20);

            riskAssessment.Property(r => r.Focus)
                .HasMaxLength(500);

            riskAssessment.Property(r => r.SummaryMarkdown)
                .HasMaxLength(8000)
                .IsRequired();

            riskAssessment.Property(r => r.Provider)
                .HasMaxLength(80)
                .IsRequired();

            riskAssessment.Property(r => r.Model)
                .HasMaxLength(120)
                .IsRequired();

            riskAssessment.Property(r => r.PromptFocus)
                .HasMaxLength(500);

            riskAssessment.Property(r => r.EvidenceSnapshotJson)
                .HasMaxLength(12000);

            riskAssessment.HasOne(r => r.Supplier)
                .WithMany(s => s.RiskAssessments)
                .HasForeignKey(r => r.SupplierId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
