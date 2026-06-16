using Microsoft.EntityFrameworkCore;
using SupplierIntelligence.Api.Data;

namespace SupplierIntelligence.Api.Endpoints;

public static class DevelopmentDataEndpoints
{
    public static IEndpointRouteBuilder MapDevelopmentDataEndpoints(this IEndpointRouteBuilder app)
    {
        var development = app.MapGroup("/api/development")
            .WithTags("Development");

        development.MapPost("/learning-data/reset", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            await LearningDataSeeder.ResetAsync(db, cancellationToken);

            var suppliers = await db.Suppliers
                .AsNoTracking()
                .OrderBy(supplier => supplier.Name)
                .Select(supplier => new
                {
                    supplier.Id,
                    supplier.Name,
                    supplier.CountryCode,
                    supplier.Industry,
                    supplier.RiskLevel,
                    CertificationCount = supplier.Certifications.Count,
                    SourceCheckCount = supplier.SourceChecks.Count,
                    RiskAssessmentCount = supplier.RiskAssessments.Count
                })
                .ToListAsync(cancellationToken);

            return Results.Ok(new
            {
                message = "Learning data reset. Only fake demo records were seeded.",
                suppliers
            });
        })
        .Produces(StatusCodes.Status200OK)
        .WithSummary("Reset local learning data")
        .WithDescription("Development-only endpoint that deletes local supplier learning records and seeds one fake demo supplier.")
        .WithName("ResetLearningData");

        development.MapGet("/sample-certification-claims", () => Results.Text(
            """
            <html>
              <body>
                <h1>Learning supplier quality page</h1>
                <p>Our management systems reference ISO 9001 and ISO 14001 for this local development sample.</p>
              </body>
            </html>
            """,
            "text/html"))
        .Produces<string>(StatusCodes.Status200OK, "text/html")
        .WithSummary("Return a sample supplier certification claims page")
        .WithDescription("Development-only HTML page used to test website-based certification discovery.")
        .WithName("GetSampleCertificationClaims");

        return app;
    }
}
