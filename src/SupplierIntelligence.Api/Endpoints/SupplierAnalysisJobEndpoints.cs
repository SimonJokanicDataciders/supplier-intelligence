using Microsoft.EntityFrameworkCore;
using SupplierIntelligence.Api.Contracts;
using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Models;
using SupplierIntelligence.Api.Services;

namespace SupplierIntelligence.Api.Endpoints;

public static class SupplierAnalysisJobEndpoints
{
    public static IEndpointRouteBuilder MapSupplierAnalysisJobEndpoints(this IEndpointRouteBuilder app)
    {
        var suppliers = app.MapGroup("/api/suppliers")
            .WithTags("Suppliers");

        suppliers.MapGet("/{id:int}/analysis-jobs", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplierExists = await db.Suppliers
                .AnyAsync(supplier => supplier.Id == id, cancellationToken);

            if (!supplierExists)
            {
                return Results.NotFound();
            }

            var jobs = await db.AnalysisJobs
                .AsNoTracking()
                .Where(job => job.SupplierId == id)
                .OrderByDescending(job => job.CreatedAt)
                .ToListAsync(cancellationToken);

            return Results.Ok(jobs.Select(ToResponse).ToList());
        })
        .Produces<List<AnalysisJobResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("List supplier analysis jobs")
        .WithDescription("Returns analysis jobs for one supplier, newest first.")
        .WithName("GetSupplierAnalysisJobs");

        suppliers.MapGet("/{id:int}/analysis-jobs/{jobId:int}", async (
            int id,
            int jobId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var job = await db.AnalysisJobs
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    analysisJob => analysisJob.SupplierId == id && analysisJob.Id == jobId,
                    cancellationToken);

            return job is null
                ? Results.NotFound()
                : Results.Ok(ToResponse(job));
        })
        .Produces<AnalysisJobResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get supplier analysis job")
        .WithDescription("Returns one analysis job for polling progress.")
        .WithName("GetSupplierAnalysisJob");

        suppliers.MapPost("/{id:int}/analysis-jobs", async (
            int id,
            AppDbContext db,
            ISupplierAnalysisQueue analysisQueue,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers
                .Include(s => s.AnalysisJobs)
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (supplier is null)
            {
                return Results.NotFound();
            }

            var hasActiveJob = supplier.AnalysisJobs.Any(job =>
                job.Status is AnalysisJobStatus.Queued or AnalysisJobStatus.Running);

            if (hasActiveJob)
            {
                return Results.Conflict(new { error = "An analysis job is already queued or running for this supplier." });
            }

            var analysisJob = new AnalysisJob
            {
                SupplierId = supplier.Id,
                JobType = "DeepSupplierAnalysis",
                Status = AnalysisJobStatus.Queued,
                ProgressMessage = "Queued for supplier analysis."
            };

            supplier.AnalysisJobs.Add(analysisJob);
            await db.SaveChangesAsync(cancellationToken);
            await analysisQueue.EnqueueAsync(analysisJob.Id, cancellationToken);

            return Results.Accepted($"/api/suppliers/{supplier.Id}/analysis-jobs/{analysisJob.Id}", ToResponse(analysisJob));
        })
        .Produces<AnalysisJobResponse>(StatusCodes.Status202Accepted)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithSummary("Queue supplier analysis")
        .WithDescription("Queues a new background supplier analysis job unless one is already queued or running.")
        .WithName("QueueSupplierAnalysis");

        return app;
    }

    private static AnalysisJobResponse ToResponse(AnalysisJob analysisJob)
    {
        return new AnalysisJobResponse(
            analysisJob.Id,
            analysisJob.JobType,
            analysisJob.Status,
            analysisJob.ProgressMessage,
            analysisJob.ErrorMessage,
            analysisJob.CreatedAt,
            analysisJob.StartedAt,
            analysisJob.CompletedAt);
    }
}
