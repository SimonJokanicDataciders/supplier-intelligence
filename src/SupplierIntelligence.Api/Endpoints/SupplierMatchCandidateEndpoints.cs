using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SupplierIntelligence.Api.Contracts;
using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Models;

namespace SupplierIntelligence.Api.Endpoints;

public static partial class SupplierMatchCandidateEndpoints
{
    public static IEndpointRouteBuilder MapSupplierMatchCandidateEndpoints(this IEndpointRouteBuilder app)
    {
        var suppliers = app.MapGroup("/api/suppliers")
            .WithTags("Suppliers");

        suppliers.MapGet("/{supplierId:int}/match-candidates", async (
            int supplierId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var exists = await db.Suppliers.AnyAsync(s => s.Id == supplierId, cancellationToken);
            if (!exists)
            {
                return Results.NotFound();
            }

            var candidates = await db.SupplierMatchCandidates
                .AsNoTracking()
                .Where(candidate => candidate.SupplierId == supplierId)
                .OrderByDescending(candidate => candidate.Status == SupplierMatchCandidateStatus.Confirmed)
                .ThenByDescending(candidate => candidate.ConfidenceScore)
                .ThenByDescending(candidate => candidate.CreatedAt)
                .Select(candidate => ToResponse(candidate))
                .ToListAsync(cancellationToken);

            return Results.Ok(candidates);
        })
        .Produces<List<SupplierMatchCandidateResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("List supplier match candidates")
        .WithDescription("Returns proposed supplier identity matches for the selected supplier.")
        .WithName("GetSupplierMatchCandidates");

        suppliers.MapPost("/{supplierId:int}/match-candidates", async (
            int supplierId,
            CreateSupplierMatchCandidateRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers
                .Include(s => s.MatchCandidates)
                .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);

            if (supplier is null)
            {
                return Results.NotFound();
            }

            var validationError = ValidateRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var normalizedName = NormalizeIdentity(request.CandidateName);
            var duplicate = supplier.MatchCandidates.Any(candidate =>
                NormalizeIdentity(candidate.CandidateName) == normalizedName &&
                string.Equals(candidate.WebsiteUrl, NormalizeOptionalText(request.WebsiteUrl), StringComparison.OrdinalIgnoreCase));

            if (duplicate)
            {
                return Results.Conflict(new { error = "A matching candidate already exists for this supplier." });
            }

            var matchCandidate = new SupplierMatchCandidate
            {
                SupplierId = supplier.Id,
                CandidateName = request.CandidateName.Trim(),
                CountryCode = NormalizeCountryCode(request.CountryCode),
                WebsiteUrl = NormalizeOptionalText(request.WebsiteUrl),
                SourceName = NormalizeOptionalText(request.SourceName),
                SourceUrl = NormalizeOptionalText(request.SourceUrl),
                ConfidenceScore = Math.Clamp(request.ConfidenceScore, 0, 100),
                MatchReason = request.MatchReason.Trim(),
                Status = SupplierMatchCandidateStatus.Proposed
            };

            supplier.MatchCandidates.Add(matchCandidate);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/suppliers/{supplier.Id}/match-candidates/{matchCandidate.Id}",
                ToResponse(matchCandidate));
        })
        .Produces<SupplierMatchCandidateResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status409Conflict)
        .WithSummary("Create supplier match candidate")
        .WithDescription("Stores one possible supplier identity match for human review.")
        .WithName("CreateSupplierMatchCandidate");

        suppliers.MapPost("/{supplierId:int}/match-candidates/suggest", async (
            int supplierId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers
                .Include(s => s.SourceChecks)
                .Include(s => s.MatchCandidates)
                .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);

            if (supplier is null)
            {
                return Results.NotFound();
            }

            var proposed = BuildCandidatesFromEvidence(supplier);
            foreach (var candidate in proposed)
            {
                var exists = supplier.MatchCandidates.Any(existing =>
                    NormalizeIdentity(existing.CandidateName) == NormalizeIdentity(candidate.CandidateName) &&
                    string.Equals(existing.WebsiteUrl, candidate.WebsiteUrl, StringComparison.OrdinalIgnoreCase));

                if (!exists)
                {
                    supplier.MatchCandidates.Add(candidate);
                }
            }

            await db.SaveChangesAsync(cancellationToken);

            var result = supplier.MatchCandidates
                .OrderByDescending(candidate => candidate.ConfidenceScore)
                .Select(ToResponse)
                .ToList();

            return Results.Ok(result);
        })
        .Produces<List<SupplierMatchCandidateResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Suggest supplier match candidates")
        .WithDescription("Creates likely supplier identity candidates from stored source checks and intake data.")
        .WithName("SuggestSupplierMatchCandidates");

        suppliers.MapPost("/{supplierId:int}/match-candidates/{candidateId:int}/confirm", async (
            int supplierId,
            int candidateId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var candidates = await db.SupplierMatchCandidates
                .Where(candidate => candidate.SupplierId == supplierId)
                .ToListAsync(cancellationToken);

            var selected = candidates.FirstOrDefault(candidate => candidate.Id == candidateId);
            if (selected is null)
            {
                return Results.NotFound();
            }

            foreach (var candidate in candidates)
            {
                candidate.Status = candidate.Id == candidateId
                    ? SupplierMatchCandidateStatus.Confirmed
                    : candidate.Status == SupplierMatchCandidateStatus.Confirmed
                        ? SupplierMatchCandidateStatus.Proposed
                        : candidate.Status;
                candidate.ReviewedAt = candidate.Id == candidateId ? DateTime.UtcNow : candidate.ReviewedAt;
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToResponse(selected));
        })
        .Produces<SupplierMatchCandidateResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Confirm supplier match candidate")
        .WithDescription("Marks one candidate as the confirmed supplier identity match.")
        .WithName("ConfirmSupplierMatchCandidate");

        suppliers.MapPost("/{supplierId:int}/match-candidates/{candidateId:int}/reject", async (
            int supplierId,
            int candidateId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var candidate = await db.SupplierMatchCandidates
                .FirstOrDefaultAsync(candidate =>
                    candidate.SupplierId == supplierId &&
                    candidate.Id == candidateId,
                    cancellationToken);

            if (candidate is null)
            {
                return Results.NotFound();
            }

            candidate.Status = SupplierMatchCandidateStatus.Rejected;
            candidate.ReviewedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToResponse(candidate));
        })
        .Produces<SupplierMatchCandidateResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Reject supplier match candidate")
        .WithDescription("Marks one proposed supplier identity match as rejected.")
        .WithName("RejectSupplierMatchCandidate");

        return app;
    }

    private static IReadOnlyList<SupplierMatchCandidate> BuildCandidatesFromEvidence(Supplier supplier)
    {
        var candidates = new List<SupplierMatchCandidate>();

        if (!string.IsNullOrWhiteSpace(supplier.WebsiteUrl))
        {
            candidates.Add(new SupplierMatchCandidate
            {
                SupplierId = supplier.Id,
                CandidateName = supplier.Name,
                CountryCode = supplier.CountryCode,
                WebsiteUrl = supplier.WebsiteUrl,
                SourceName = "Supplier intake",
                SourceUrl = supplier.WebsiteUrl,
                ConfidenceScore = 72,
                MatchReason = "Supplier intake includes a website and matching country/industry context."
            });
        }

        foreach (var sourceCheck in supplier.SourceChecks.Where(source => source.Status == SourceCheckStatus.Reachable))
        {
            var urls = UrlRegex()
                .Matches(sourceCheck.Notes + " " + sourceCheck.Url)
                .Select(match => match.Value.TrimEnd('.', ',', ')', ']', '"', '\''))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(url => !url.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
                .Take(3)
                .ToList();

            foreach (var url in urls)
            {
                candidates.Add(new SupplierMatchCandidate
                {
                    SupplierId = supplier.Id,
                    CandidateName = InferCandidateName(supplier, sourceCheck.Notes),
                    CountryCode = supplier.CountryCode,
                    WebsiteUrl = url,
                    SourceName = sourceCheck.SourceName,
                    SourceUrl = url,
                    ConfidenceScore = ScoreCandidate(sourceCheck, supplier),
                    MatchReason = $"Candidate came from {sourceCheck.SourceName}. {BuildShortReason(sourceCheck.Notes)}"
                });
            }
        }

        return candidates
            .GroupBy(candidate => NormalizeIdentity(candidate.CandidateName) + "|" + candidate.WebsiteUrl, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.ConfidenceScore).First())
            .OrderByDescending(candidate => candidate.ConfidenceScore)
            .Take(5)
            .ToList();
    }

    private static string InferCandidateName(Supplier supplier, string notes)
    {
        var identityMarker = "Company identity:";
        var identityIndex = notes.IndexOf(identityMarker, StringComparison.OrdinalIgnoreCase);
        if (identityIndex >= 0)
        {
            var value = notes[(identityIndex + identityMarker.Length)..]
                .Split(["Products and services:", "Certification and quality evidence:", "Location or registry evidence:", "Source URLs:"], StringSplitOptions.RemoveEmptyEntries)
                .FirstOrDefault()
                ?.Trim(' ', '*', '-', ':', '\n', '\r');

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Length <= 220 ? value : value[..217] + "...";
            }
        }

        return supplier.Name;
    }

    private static int ScoreCandidate(SourceCheck sourceCheck, Supplier supplier)
    {
        var score = sourceCheck.SourceName.Contains("AI web search", StringComparison.OrdinalIgnoreCase) ? 58 : 45;

        if (sourceCheck.Notes.Contains(supplier.Name, StringComparison.OrdinalIgnoreCase))
        {
            score += 12;
        }

        if (sourceCheck.Notes.Contains(supplier.CountryCode, StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        if (sourceCheck.Notes.Contains(supplier.Industry, StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        if (sourceCheck.Notes.Contains("official", StringComparison.OrdinalIgnoreCase))
        {
            score += 8;
        }

        return Math.Clamp(score, 0, 95);
    }

    private static string BuildShortReason(string notes)
    {
        var normalized = WhiteSpaceRegex().Replace(notes.Trim(), " ");
        return normalized.Length <= 220 ? normalized : normalized[..217] + "...";
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string? NormalizeCountryCode(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToUpperInvariant();
    }

    private static string NormalizeIdentity(string value)
    {
        return WhiteSpaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");
    }

    private static string? ValidateRequest(CreateSupplierMatchCandidateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.CandidateName))
        {
            return "Candidate name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.MatchReason))
        {
            return "Match reason is required.";
        }

        return null;
    }

    private static SupplierMatchCandidateResponse ToResponse(SupplierMatchCandidate candidate)
    {
        return new SupplierMatchCandidateResponse(
            candidate.Id,
            candidate.CandidateName,
            candidate.CountryCode,
            candidate.WebsiteUrl,
            candidate.SourceName,
            candidate.SourceUrl,
            candidate.ConfidenceScore,
            candidate.MatchReason,
            candidate.Status,
            candidate.CreatedAt,
            candidate.ReviewedAt);
    }

    [GeneratedRegex("https?://[^\\s<>()\\[\\]\"']+", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UrlRegex();

    [GeneratedRegex("\\s+", RegexOptions.Compiled)]
    private static partial Regex WhiteSpaceRegex();
}
