using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SupplierIntelligence.Api.Contracts;
using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Models;
using SupplierIntelligence.Api.Options;
using SupplierIntelligence.Api.Services;

namespace SupplierIntelligence.Api.Endpoints;

public static class SupplierEndpoints
{
    private const int MaxCompareBoardSuppliers = 6;

    public static IEndpointRouteBuilder MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var suppliers = app.MapGroup("/api/suppliers")
            .WithTags("Suppliers");
        var compareBoards = app.MapGroup("/api/compare-boards")
            .WithTags("Compare Boards");

        suppliers.MapGet("/", async (
            bool? includeArchived,
            bool? includeDevelopment,
            bool? compact,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            IQueryable<Supplier> query = db.Suppliers.AsNoTracking();

            if (includeArchived != true)
            {
                query = query.Where(s => !s.IsArchived);
            }

            var result = await query
                .AsNoTracking()
                .OrderByDescending(s => s.CreatedAt)
                .Select(s => new SupplierSummaryResponse(
                    s.Id,
                    s.Name,
                    s.CountryCode,
                    s.Industry,
                    s.WebsiteUrl,
                    s.RegistryNumber,
                    s.VatNumber,
                    s.CertificationHints,
                    s.RiskLevel,
                    s.IsArchived,
                    s.Certifications.Count,
                    s.SourceChecks.Count,
                    s.RiskAssessments.Count))
                .ToListAsync(cancellationToken);

            if (includeDevelopment != true)
            {
                result = result
                    .Where(supplier => !IsDevelopmentSupplier(supplier))
                    .ToList();
            }

            if (compact != false)
            {
                result = result
                    .GroupBy(BuildSupplierIdentityKey, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .ToList();
            }

            return Results.Ok(result);
        })
        .Produces<List<SupplierSummaryResponse>>()
        .WithSummary("List suppliers")
        .WithDescription("Returns all suppliers with compact counts for certifications, source checks, and saved risk assessments.")
        .WithName("GetSuppliers");

        suppliers.MapPost("/compare", async (
            CompareSuppliersRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplierIds = request.SupplierIds?
                .Distinct()
                .ToList() ?? [];

            if (supplierIds.Count < 2)
            {
                return Results.BadRequest(new { error = "Select at least 2 suppliers to compare." });
            }

            if (supplierIds.Count > 4)
            {
                return Results.BadRequest(new { error = "Compare supports up to 4 suppliers at once." });
            }

            var suppliersWithResearch = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.SourceChecks)
                .Include(s => s.ResearchSources)
                .Include(s => s.SupplierFacts)
                .Include(s => s.RiskAssessments)
                .Where(s => !s.IsArchived)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            var selectedSuppliers = suppliersWithResearch
                .Where(supplier => supplierIds.Contains(supplier.Id))
                .OrderBy(supplier => supplierIds.IndexOf(supplier.Id))
                .ToList();

            if (selectedSuppliers.Count != supplierIds.Count)
            {
                return Results.BadRequest(new { error = "One or more selected suppliers do not exist or are archived." });
            }

            return Results.Ok(BuildSupplierComparison(selectedSuppliers, suppliersWithResearch));
        })
        .Accepts<CompareSuppliersRequest>("application/json")
        .Produces<SupplierComparisonResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .WithSummary("Compare suppliers")
        .WithDescription("Compares 2-4 non-archived suppliers using stored facts, source checks, research notes, and connection terms.")
        .WithName("CompareSuppliers");

        compareBoards.MapGet("/", async (
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var boards = await db.CompareBoards
                .AsNoTracking()
                .Include(board => board.Suppliers)
                .ThenInclude(boardSupplier => boardSupplier.Supplier)
                .OrderByDescending(board => board.UpdatedAt)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            var suppliersWithResearch = await LoadSuppliersWithResearchAsync(db, cancellationToken);

            return Results.Ok(boards
                .Select(board => ToCompareBoardResponse(board, suppliersWithResearch))
                .ToList());
        })
        .Produces<List<CompareBoardResponse>>()
        .WithSummary("List compare boards")
        .WithDescription("Returns saved supplier comparison workspaces with their current supplier members and comparison data.")
        .WithName("GetCompareBoards");

        compareBoards.MapPost("/", async (
            CreateCompareBoardRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var name = NormalizeCompareBoardName(request.Name);
            if (name is null)
            {
                return Results.BadRequest(new { error = "Board name is required and must be 160 characters or shorter." });
            }

            var supplierIds = (request.SupplierIds ?? [])
                .Distinct()
                .Take(MaxCompareBoardSuppliers + 1)
                .ToList();
            if (supplierIds.Count > MaxCompareBoardSuppliers)
            {
                return Results.BadRequest(new { error = $"A compare board can contain up to {MaxCompareBoardSuppliers} suppliers." });
            }

            var suppliersToAdd = await db.Suppliers
                .Where(supplier => supplierIds.Contains(supplier.Id) && !supplier.IsArchived)
                .ToListAsync(cancellationToken);
            if (suppliersToAdd.Count != supplierIds.Count)
            {
                return Results.BadRequest(new { error = "One or more selected suppliers do not exist or are archived." });
            }

            var now = DateTime.UtcNow;
            var board = new CompareBoard
            {
                Name = name,
                CreatedAt = now,
                UpdatedAt = now,
                Suppliers = supplierIds
                    .Select((supplierId, index) => new CompareBoardSupplier
                    {
                        SupplierId = supplierId,
                        SortOrder = index,
                        AddedAt = now
                    })
                    .ToList()
            };

            db.CompareBoards.Add(board);
            await db.SaveChangesAsync(cancellationToken);

            board = await LoadCompareBoardAsync(db, board.Id, cancellationToken) ?? board;
            var suppliersWithResearch = await LoadSuppliersWithResearchAsync(db, cancellationToken);

            return Results.Created(
                $"/api/compare-boards/{board.Id}",
                ToCompareBoardResponse(board, suppliersWithResearch));
        })
        .Accepts<CreateCompareBoardRequest>("application/json")
        .Produces<CompareBoardResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .WithSummary("Create compare board")
        .WithDescription("Creates a saved comparison workspace. It can start empty or with up to six suppliers.")
        .WithName("CreateCompareBoard");

        compareBoards.MapGet("/{boardId:int}", async (
            int boardId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var board = await LoadCompareBoardAsync(db, boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            var suppliersWithResearch = await LoadSuppliersWithResearchAsync(db, cancellationToken);
            return Results.Ok(ToCompareBoardResponse(board, suppliersWithResearch));
        })
        .Produces<CompareBoardResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get compare board")
        .WithDescription("Returns one saved comparison workspace with comparison data.")
        .WithName("GetCompareBoard");

        compareBoards.MapPatch("/{boardId:int}", async (
            int boardId,
            UpdateCompareBoardRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var name = NormalizeCompareBoardName(request.Name);
            if (name is null)
            {
                return Results.BadRequest(new { error = "Board name is required and must be 160 characters or shorter." });
            }

            var board = await db.CompareBoards
                .FirstOrDefaultAsync(item => item.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            board.Name = name;
            board.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            var reloadedBoard = await LoadCompareBoardAsync(db, boardId, cancellationToken);
            var suppliersWithResearch = await LoadSuppliersWithResearchAsync(db, cancellationToken);
            return Results.Ok(ToCompareBoardResponse(reloadedBoard!, suppliersWithResearch));
        })
        .Accepts<UpdateCompareBoardRequest>("application/json")
        .Produces<CompareBoardResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Rename compare board")
        .WithName("RenameCompareBoard");

        compareBoards.MapPost("/{boardId:int}/suppliers/{supplierId:int}", async (
            int boardId,
            int supplierId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var board = await db.CompareBoards
                .Include(item => item.Suppliers)
                .FirstOrDefaultAsync(item => item.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            if (board.Suppliers.Any(item => item.SupplierId == supplierId))
            {
                var existingBoard = await LoadCompareBoardAsync(db, boardId, cancellationToken);
                var existingSuppliersWithResearch = await LoadSuppliersWithResearchAsync(db, cancellationToken);
                return Results.Ok(ToCompareBoardResponse(existingBoard!, existingSuppliersWithResearch));
            }

            if (board.Suppliers.Count >= MaxCompareBoardSuppliers)
            {
                return Results.BadRequest(new { error = $"A compare board can contain up to {MaxCompareBoardSuppliers} suppliers." });
            }

            var supplierExists = await db.Suppliers
                .AnyAsync(supplier => supplier.Id == supplierId && !supplier.IsArchived, cancellationToken);
            if (!supplierExists)
            {
                return Results.BadRequest(new { error = "Supplier does not exist or is archived." });
            }

            var now = DateTime.UtcNow;
            board.Suppliers.Add(new CompareBoardSupplier
            {
                SupplierId = supplierId,
                SortOrder = board.Suppliers.Count == 0 ? 0 : board.Suppliers.Max(item => item.SortOrder) + 1,
                AddedAt = now
            });
            board.UpdatedAt = now;
            await db.SaveChangesAsync(cancellationToken);

            var reloadedBoard = await LoadCompareBoardAsync(db, boardId, cancellationToken);
            var suppliersWithResearch = await LoadSuppliersWithResearchAsync(db, cancellationToken);
            return Results.Ok(ToCompareBoardResponse(reloadedBoard!, suppliersWithResearch));
        })
        .Produces<CompareBoardResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Add supplier to compare board")
        .WithName("AddSupplierToCompareBoard");

        compareBoards.MapDelete("/{boardId:int}/suppliers/{supplierId:int}", async (
            int boardId,
            int supplierId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var board = await db.CompareBoards
                .Include(item => item.Suppliers)
                .FirstOrDefaultAsync(item => item.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            var boardSupplier = board.Suppliers.FirstOrDefault(item => item.SupplierId == supplierId);
            if (boardSupplier is not null)
            {
                db.CompareBoardSuppliers.Remove(boardSupplier);
                board.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);
            }

            var reloadedBoard = await LoadCompareBoardAsync(db, boardId, cancellationToken);
            var suppliersWithResearch = await LoadSuppliersWithResearchAsync(db, cancellationToken);
            return Results.Ok(ToCompareBoardResponse(reloadedBoard!, suppliersWithResearch));
        })
        .Produces<CompareBoardResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Remove supplier from compare board")
        .WithName("RemoveSupplierFromCompareBoard");

        compareBoards.MapDelete("/{boardId:int}", async (
            int boardId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var board = await db.CompareBoards
                .FirstOrDefaultAsync(item => item.Id == boardId, cancellationToken);
            if (board is null)
            {
                return Results.NotFound();
            }

            db.CompareBoards.Remove(board);
            await db.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Delete compare board")
        .WithName("DeleteCompareBoard");

        suppliers.MapGet("/{id:int}/report.md", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var suppliersWithResearch = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.SourceChecks)
                .Include(s => s.ResearchSources)
                .Include(s => s.SupplierFacts)
                .Include(s => s.RiskAssessments)
                .Where(s => !s.IsArchived)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            var supplier = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.Certifications)
                .Include(s => s.SourceChecks)
                .Include(s => s.ResearchSources)
                .Include(s => s.SupplierFacts)
                .Include(s => s.RiskAssessments)
                .Include(s => s.AnalysisJobs)
                .Include(s => s.MatchCandidates)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == id && !s.IsArchived, cancellationToken);

            if (supplier is null)
            {
                return Results.NotFound();
            }

            var markdown = BuildSupplierMarkdownReport(
                supplier,
                BuildSupplierConnections(supplier, suppliersWithResearch));
            var fileName = $"{SlugifyFileName(supplier.Name)}-supplier-report.md";

            return Results.File(
                Encoding.UTF8.GetBytes(markdown),
                "text/markdown; charset=utf-8",
                fileName);
        })
        .Produces(StatusCodes.Status200OK, contentType: "text/markdown")
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Export supplier report as Markdown")
        .WithDescription("Exports the selected supplier briefing, facts, sources, open gaps, and related suppliers as a Markdown report.")
        .WithName("ExportSupplierMarkdownReport");

        suppliers.MapGet("/{id:int}", async (int id, AppDbContext db) =>
        {
            var supplier = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.Certifications)
                .Include(s => s.SourceChecks)
                .Include(s => s.ResearchSources)
                .Include(s => s.SupplierFacts)
                .Include(s => s.RiskAssessments)
                .Include(s => s.AnalysisJobs)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == id);

            return supplier is null
                ? Results.NotFound()
                : Results.Ok(ToDetailResponse(supplier));
        })
        .Produces<SupplierDetailResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get supplier detail")
        .WithDescription("Returns one supplier with certifications, source checks, and saved risk assessments.")
        .WithName("GetSupplierById");

        suppliers.MapGet("/{id:int}/review-summary", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.Certifications)
                .Include(s => s.SourceChecks)
                .Include(s => s.SupplierFacts)
                .Include(s => s.RiskAssessments)
                .Include(s => s.MatchCandidates)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            return supplier is null
                ? Results.NotFound()
                : Results.Ok(BuildReviewSummary(supplier));
        })
        .Produces<SupplierReviewSummaryResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get supplier review summary")
        .WithDescription("Returns a generic review summary for the selected supplier: next action, known information, missing information, and trust signals.")
        .WithName("GetSupplierReviewSummary");

        suppliers.MapGet("/{id:int}/connections", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var suppliersWithResearch = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.SourceChecks)
                .Include(s => s.SupplierFacts)
                .Where(s => !s.IsArchived)
                .AsSplitQuery()
                .ToListAsync(cancellationToken);
            var supplier = suppliersWithResearch.FirstOrDefault(s => s.Id == id);

            return supplier is null
                ? Results.NotFound()
                : Results.Ok(BuildSupplierConnections(supplier, suppliersWithResearch));
        })
        .Produces<List<SupplierConnectionResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get supplier connections")
        .WithDescription("Returns research-based supplier similarities from shared country, industry, source hosts, and extracted fact terms.")
        .WithName("GetSupplierConnections");

        suppliers.MapGet("/{id:int}/analytics", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.Certifications)
                .Include(s => s.SourceChecks)
                .Include(s => s.RiskAssessments)
                .Include(s => s.AnalysisJobs)
                .Include(s => s.MatchCandidates)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            return supplier is null
                ? Results.NotFound()
                : Results.Ok(BuildAnalytics(supplier));
        })
        .Produces<SupplierAnalyticsResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get supplier analytics")
        .WithDescription("Returns trust breakdown bars, source mix, review timeline, strongest signals, and weakest gaps for one supplier.")
        .WithName("GetSupplierAnalytics");

        suppliers.MapPost("/", async (
            CreateSupplierRequest request,
            AppDbContext db,
            ISupplierAnalysisQueue analysisQueue,
            CancellationToken cancellationToken) =>
        {
            var validationError = ValidateSupplierRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var duplicate = await FindExistingSupplierAsync(request, db, cancellationToken);
            if (duplicate is not null)
            {
                return Results.Conflict(new
                {
                    error = "A matching non-archived supplier already exists.",
                    supplierId = duplicate.Id
                });
            }

            var supplier = new Supplier
            {
                Name = request.Name.Trim(),
                CountryCode = request.CountryCode.Trim().ToUpperInvariant(),
                Industry = request.Industry.Trim(),
                WebsiteUrl = NormalizeOptionalText(request.WebsiteUrl),
                RegistryNumber = NormalizeOptionalText(request.RegistryNumber),
                VatNumber = NormalizeOptionalText(request.VatNumber),
                CertificationHints = NormalizeOptionalText(request.CertificationHints)
            };

            db.Suppliers.Add(supplier);

            AnalysisJob? analysisJob = null;
            if (request.RunInitialAnalysis)
            {
                analysisJob = new AnalysisJob
                {
                    Supplier = supplier,
                    JobType = "InitialSupplierAnalysis",
                    Status = AnalysisJobStatus.Queued,
                    ProgressMessage = "Queued for website, evidence, and local-model analysis."
                };
                supplier.AnalysisJobs.Add(analysisJob);
            }

            await db.SaveChangesAsync(cancellationToken);

            if (analysisJob is not null)
            {
                await analysisQueue.EnqueueAsync(analysisJob.Id, cancellationToken);
            }

            return Results.Created($"/api/suppliers/{supplier.Id}", ToDetailResponse(supplier));
        })
        .Produces<SupplierDetailResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status409Conflict)
        .WithSummary("Create supplier")
        .WithDescription("Creates a supplier record that can later receive certifications, source checks, and risk assessments.")
        .WithName("CreateSupplier");

        suppliers.MapPatch("/{id:int}/industry", async (
            int id,
            UpdateSupplierIndustryRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            if (string.IsNullOrWhiteSpace(request.Industry))
            {
                return Results.BadRequest(new { error = "Industry is required." });
            }

            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (supplier is null)
            {
                return Results.NotFound();
            }

            supplier.Industry = request.Industry.Trim();
            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToDetailResponse(supplier));
        })
        .Produces<SupplierDetailResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Update supplier industry")
        .WithDescription("Moves a supplier into another sidebar industry folder.")
        .WithName("UpdateSupplierIndustry");

        suppliers.MapPost("/{id:int}/archive", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (supplier is null)
            {
                return Results.NotFound();
            }

            supplier.IsArchived = true;
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Archive supplier")
        .WithDescription("Hides a supplier from the default supplier list without deleting its assessments or evidence.")
        .WithName("ArchiveSupplier");

        suppliers.MapPost("/{id:int}/restore", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == id, cancellationToken);
            if (supplier is null)
            {
                return Results.NotFound();
            }

            supplier.IsArchived = false;
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Restore supplier")
        .WithDescription("Restores an archived supplier to the default supplier list.")
        .WithName("RestoreSupplier");

        suppliers.MapPost("/{id:int}/risk-assessments/generate", async (
            int id,
            GenerateSupplierBriefingRequest? request,
            AppDbContext db,
            IResearchFactExtractor researchFactExtractor,
            ILocalModelClient localModel,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers
                .Include(s => s.Certifications)
                .Include(s => s.SourceChecks)
                .Include(s => s.ResearchSources)
                .Include(s => s.SupplierFacts)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (supplier is null)
            {
                return Results.NotFound();
            }

            var model = string.IsNullOrWhiteSpace(request?.Model)
                ? localModel.DefaultModel
                : request.Model.Trim();

            try
            {
                var estimatedRiskLevel = EvidenceQualityCalculator.EstimateRiskLevel(supplier);
                var estimatedRiskScore = EvidenceQualityCalculator.EstimateRiskScore(supplier);
                var promptFocus = NormalizePromptFocus(request?.Focus);

                await researchFactExtractor.RefreshFactsAsync(supplier, db, cancellationToken);

                var evidenceSnapshotJson = SupplierEvidenceSnapshotBuilder.Build(
                    supplier,
                    estimatedRiskLevel,
                    estimatedRiskScore);

                var generationTimer = Stopwatch.StartNew();
                var briefing = await localModel.ChatAsync(
                    model,
                    BuildRiskAssessmentSystemPrompt(),
                    BuildRiskAssessmentUserPrompt(promptFocus, evidenceSnapshotJson),
                    cancellationToken);
                generationTimer.Stop();

                var riskAssessment = new RiskAssessment
                {
                    SupplierId = supplier.Id,
                    RiskLevel = estimatedRiskLevel,
                    Score = estimatedRiskScore,
                    Focus = promptFocus,
                    SummaryMarkdown = briefing,
                    Provider = localModel.Provider,
                    Model = model,
                    PromptFocus = promptFocus,
                    EvidenceSnapshotJson = evidenceSnapshotJson,
                    GenerationDurationMs = (int)Math.Min(generationTimer.ElapsedMilliseconds, int.MaxValue)
                };

                supplier.RiskLevel = estimatedRiskLevel;
                db.RiskAssessments.Add(riskAssessment);
                await db.SaveChangesAsync(cancellationToken);

                return Results.Created(
                    $"/api/suppliers/{supplier.Id}/risk-assessments/{riskAssessment.Id}",
                    new GenerateRiskAssessmentResponse(
                        supplier.Id,
                        supplier.Name,
                        ToRiskAssessmentResponse(riskAssessment)));
            }
            catch (LocalModelException exception)
            {
                return Results.Problem(
                    title: "Model generation failed",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .Accepts<GenerateSupplierBriefingRequest>("application/json")
        .Produces<GenerateRiskAssessmentResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .WithSummary("Generate supplier risk assessment with configured model provider")
        .WithDescription("Builds an evidence prompt from stored supplier data, sends it to the configured model provider, saves the generated assessment, and returns the saved result.")
        .WithName("GenerateSupplierRiskAssessment");

        suppliers.MapGet("/{id:int}/risk-assessments", async (
            int id,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var supplierExists = await db.Suppliers.AnyAsync(s => s.Id == id, cancellationToken);
            if (!supplierExists)
            {
                return Results.NotFound();
            }

            var riskAssessments = await db.RiskAssessments
                .AsNoTracking()
                .Where(r => r.SupplierId == id)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(cancellationToken);

            var assessments = riskAssessments
                .Select(ToRiskAssessmentResponse)
                .ToList();

            return Results.Ok(assessments);
        })
        .Produces<List<RiskAssessmentResponse>>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("List supplier risk assessments")
        .WithDescription("Returns saved risk assessments for one supplier, newest first.")
        .WithName("GetSupplierRiskAssessments");

        suppliers.MapGet("/{supplierId:int}/risk-assessments/{assessmentId:int}", async (
            int supplierId,
            int assessmentId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var riskAssessment = await db.RiskAssessments
                .AsNoTracking()
                .FirstOrDefaultAsync(
                    r => r.Id == assessmentId && r.SupplierId == supplierId,
                    cancellationToken);

            return riskAssessment is null
                ? Results.NotFound()
                : Results.Ok(ToRiskAssessmentResponse(riskAssessment));
        })
        .Produces<RiskAssessmentResponse>()
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Get one supplier risk assessment")
        .WithDescription("Returns a single risk assessment only when it belongs to the supplier in the route.")
        .WithName("GetSupplierRiskAssessmentById");

        suppliers.MapPost("/{id:int}/risk-assessments", async (
            int id,
            CreateRiskAssessmentRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var validationError = ValidateRiskAssessmentRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var supplier = await db.Suppliers
                .FirstOrDefaultAsync(s => s.Id == id, cancellationToken);

            if (supplier is null)
            {
                return Results.NotFound();
            }

            var riskAssessment = new RiskAssessment
            {
                SupplierId = supplier.Id,
                RiskLevel = request.RiskLevel,
                Score = request.Score,
                Focus = request.Focus.Trim(),
                SummaryMarkdown = request.SummaryMarkdown.Trim(),
                Provider = "Manual",
                Model = "Human"
            };

            supplier.RiskLevel = request.RiskLevel;
            db.RiskAssessments.Add(riskAssessment);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/suppliers/{supplier.Id}/risk-assessments/{riskAssessment.Id}",
                ToRiskAssessmentResponse(riskAssessment));
        })
        .Accepts<CreateRiskAssessmentRequest>("application/json")
        .Produces<RiskAssessmentResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Create manual supplier risk assessment")
        .WithDescription("Creates a human-entered risk assessment, saves it to SQLite through EF Core, and updates the supplier's current risk level.")
        .WithName("CreateSupplierRiskAssessment");

        suppliers.MapPut("/{supplierId:int}/risk-assessments/{assessmentId:int}", async (
            int supplierId,
            int assessmentId,
            UpdateRiskAssessmentRequest request,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var validationError = ValidateRiskAssessmentRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var riskAssessment = await db.RiskAssessments
                .Include(r => r.Supplier)
                .FirstOrDefaultAsync(
                    r => r.Id == assessmentId && r.SupplierId == supplierId,
                    cancellationToken);

            if (riskAssessment is null)
            {
                return Results.NotFound();
            }

            riskAssessment.RiskLevel = request.RiskLevel;
            riskAssessment.Score = request.Score;
            riskAssessment.Focus = request.Focus.Trim();
            riskAssessment.SummaryMarkdown = request.SummaryMarkdown.Trim();
            riskAssessment.Supplier.RiskLevel = request.RiskLevel;

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToRiskAssessmentResponse(riskAssessment));
        })
        .Accepts<UpdateRiskAssessmentRequest>("application/json")
        .Produces<RiskAssessmentResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Update supplier risk assessment")
        .WithDescription("Updates a saved risk assessment that belongs to the supplier in the route.")
        .WithName("UpdateSupplierRiskAssessment");

        suppliers.MapDelete("/{supplierId:int}/risk-assessments/{assessmentId:int}", async (
            int supplierId,
            int assessmentId,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var riskAssessment = await db.RiskAssessments
                .Include(r => r.Supplier)
                .FirstOrDefaultAsync(
                    r => r.Id == assessmentId && r.SupplierId == supplierId,
                    cancellationToken);

            if (riskAssessment is null)
            {
                return Results.NotFound();
            }

            var newestRemainingRiskLevel = await db.RiskAssessments
                .AsNoTracking()
                .Where(r => r.SupplierId == supplierId && r.Id != assessmentId)
                .OrderByDescending(r => r.CreatedAt)
                .Select(r => (SupplierRiskLevel?)r.RiskLevel)
                .FirstOrDefaultAsync(cancellationToken);

            riskAssessment.Supplier.RiskLevel = newestRemainingRiskLevel ?? SupplierRiskLevel.Unknown;
            db.RiskAssessments.Remove(riskAssessment);
            await db.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Delete supplier risk assessment")
        .WithDescription("Deletes a saved risk assessment and recalculates the supplier's current risk level from the newest remaining assessment.")
        .WithName("DeleteSupplierRiskAssessment");

        suppliers.MapPost("/{supplierId:int}/certifications", async (
            int supplierId,
            AddCertificationRequest request,
            AppDbContext db) =>
        {
            var supplierExists = await db.Suppliers.AnyAsync(s => s.Id == supplierId);
            if (!supplierExists)
            {
                return Results.NotFound();
            }

            var validationError = ValidateCertificationRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var certification = new Certification
            {
                SupplierId = supplierId,
                Standard = request.Standard.Trim(),
                CertificateNumber = request.CertificateNumber.Trim(),
                Issuer = request.Issuer.Trim(),
                ValidUntil = request.ValidUntil,
                IsVerified = request.IsVerified,
                VerificationNotes = request.IsVerified
                    ? "Manual certification entry marked verified by the user."
                    : "Manual certification entry; not automatically verified."
            };

            db.Certifications.Add(certification);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/suppliers/{supplierId}/certifications/{certification.Id}",
                ToCertificationResponse(certification));
        })
        .Produces<CertificationResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Add supplier certification")
        .WithDescription("Adds a certification record to a supplier, such as ISO 9001 evidence.")
        .WithName("AddSupplierCertification");

        suppliers.MapPost("/{supplierId:int}/certifications/verify", async (
            int supplierId,
            VerifyCertificationRequest request,
            AppDbContext db,
            ICertificationVerifier certificationVerifier,
            CancellationToken cancellationToken) =>
        {
            var supplierExists = await db.Suppliers.AnyAsync(s => s.Id == supplierId, cancellationToken);
            if (!supplierExists)
            {
                return Results.NotFound();
            }

            var validationError = ValidateCertificationVerificationRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var verification = await certificationVerifier.VerifyAsync(
                new CertificationVerificationInput(
                    request.Standard,
                    request.CertificateNumber,
                    request.Issuer,
                    request.ValidUntil),
                cancellationToken);

            var certification = new Certification
            {
                SupplierId = supplierId,
                Standard = request.Standard.Trim(),
                CertificateNumber = request.CertificateNumber.Trim(),
                Issuer = request.Issuer.Trim(),
                ValidUntil = request.ValidUntil,
                IsVerified = verification.IsVerified,
                VerificationNotes = verification.Notes
            };

            db.Certifications.Add(certification);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/suppliers/{supplierId}/certifications/{certification.Id}",
                ToCertificationResponse(certification));
        })
        .Produces<CertificationResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Verify and add supplier certification")
        .WithDescription("Runs local certification verification rules, stores the certification, and records verification notes.")
        .WithName("VerifySupplierCertification");

        suppliers.MapPost("/{supplierId:int}/certifications/discover-from-website", async (
            int supplierId,
            AppDbContext db,
            IWebsiteCertificationDiscovery websiteCertificationDiscovery,
            CancellationToken cancellationToken) =>
        {
            var supplier = await db.Suppliers
                .Include(s => s.Certifications)
                .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);

            if (supplier is null)
            {
                return Results.NotFound();
            }

            if (string.IsNullOrWhiteSpace(supplier.WebsiteUrl) ||
                !Uri.TryCreate(supplier.WebsiteUrl, UriKind.Absolute, out var websiteUrl))
            {
                return Results.BadRequest(new { error = "Supplier must have a valid WebsiteUrl before certification discovery can run." });
            }

            var discovered = await websiteCertificationDiscovery.DiscoverAsync(websiteUrl, cancellationToken);
            var certifications = AddDiscoveredCertifications(supplier, discovered);

            if (certifications.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            return Results.Ok(certifications.Select(ToCertificationResponse).ToList());
        })
        .Produces<List<CertificationResponse>>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Discover supplier certifications from website")
        .WithDescription("Scans the supplier website for common certification claims and stores discovered unverified certification evidence.")
        .WithName("DiscoverSupplierCertificationsFromWebsite");

        suppliers.MapPost("/{supplierId:int}/open-questions/recheck", async (
            int supplierId,
            RecheckOpenQuestionsRequest request,
            AppDbContext db,
            ILocalModelClient localModel,
            IOptions<LocalModelOptions> localModelOptions,
            CancellationToken cancellationToken) =>
        {
            var questions = request.Questions
                .Select(question => question.Trim())
                .Where(question => !string.IsNullOrWhiteSpace(question))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(12)
                .ToList();
            if (questions.Count == 0)
            {
                return Results.BadRequest(new { error = "At least one open question is required." });
            }

            var supplier = await db.Suppliers
                .AsNoTracking()
                .Include(s => s.SourceChecks)
                .Include(s => s.SupplierFacts)
                .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
            if (supplier is null)
            {
                return Results.NotFound();
            }

            var evidence = BuildOpenQuestionEvidence(supplier, questions);
            try
            {
                var modelResponse = await localModel.ChatAsync(
                    localModelOptions.Value.Model,
                    BuildOpenQuestionRecheckSystemPrompt(),
                    JsonSerializer.Serialize(evidence),
                    cancellationToken);
                var response = ParseOpenQuestionRecheckResponse(modelResponse, questions);

                return Results.Ok(response);
            }
            catch (LocalModelException exception)
            {
                return Results.Problem(
                    title: "Open question recheck failed",
                    detail: exception.Message,
                    statusCode: StatusCodes.Status503ServiceUnavailable);
            }
        })
        .Produces<RecheckOpenQuestionsResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .Produces(StatusCodes.Status503ServiceUnavailable)
        .WithSummary("Recheck supplier open questions")
        .WithDescription("Uses the configured model to resolve open questions from already stored supplier facts and source checks.")
        .WithName("RecheckSupplierOpenQuestions");

        suppliers.MapPost("/{supplierId:int}/source-checks", async (
            int supplierId,
            AddSourceCheckRequest request,
            AppDbContext db) =>
        {
            var supplierExists = await db.Suppliers.AnyAsync(s => s.Id == supplierId);
            if (!supplierExists)
            {
                return Results.NotFound();
            }

            var validationError = ValidateSourceCheckRequest(request);
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var sourceCheck = new SourceCheck
            {
                SupplierId = supplierId,
                SourceName = request.SourceName.Trim(),
                Url = request.Url.Trim(),
                Status = request.Status,
                Notes = request.Notes.Trim()
            };

            db.SourceChecks.Add(sourceCheck);
            await db.SaveChangesAsync();

            return Results.Created(
                $"/api/suppliers/{supplierId}/source-checks/{sourceCheck.Id}",
                ToSourceCheckResponse(sourceCheck));
        })
        .Produces<SourceCheckResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Add supplier source check")
        .WithDescription("Adds a source-check record for supplier evidence, such as a reachable company website or blocked source.")
        .WithName("AddSupplierSourceCheck");

        suppliers.MapPut("/{supplierId:int}/source-checks/{sourceCheckId:int}", async (
            int supplierId,
            int sourceCheckId,
            UpdateSourceCheckRequest request,
            AppDbContext db,
            IResearchFactExtractor researchFactExtractor,
            CancellationToken cancellationToken) =>
        {
            var sourceCheck = await db.SourceChecks
                .FirstOrDefaultAsync(
                    source => source.Id == sourceCheckId && source.SupplierId == supplierId,
                    cancellationToken);
            if (sourceCheck is null)
            {
                return Results.NotFound();
            }

            var validationError = ValidateSourceCheckRequest(new AddSourceCheckRequest(
                request.SourceName,
                request.Url,
                request.Status,
                request.Notes));
            if (validationError is not null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            sourceCheck.SourceName = request.SourceName.Trim();
            sourceCheck.Url = request.Url.Trim();
            sourceCheck.Status = request.Status;
            sourceCheck.Notes = request.Notes.Trim();
            sourceCheck.CheckedAt = DateTime.UtcNow;

            var supplier = await LoadSupplierForFactRefreshAsync(db, supplierId, cancellationToken);
            if (supplier is not null)
            {
                await researchFactExtractor.RefreshFactsAsync(supplier, db, cancellationToken);
            }

            await db.SaveChangesAsync(cancellationToken);

            return Results.Ok(ToSourceCheckResponse(sourceCheck));
        })
        .Produces<SourceCheckResponse>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Update supplier source check")
        .WithDescription("Updates a stored source-check record and refreshes extracted supplier facts.")
        .WithName("UpdateSupplierSourceCheck");

        suppliers.MapDelete("/{supplierId:int}/source-checks/{sourceCheckId:int}", async (
            int supplierId,
            int sourceCheckId,
            AppDbContext db,
            IResearchFactExtractor researchFactExtractor,
            CancellationToken cancellationToken) =>
        {
            var sourceCheck = await db.SourceChecks
                .FirstOrDefaultAsync(
                    source => source.Id == sourceCheckId && source.SupplierId == supplierId,
                    cancellationToken);
            if (sourceCheck is null)
            {
                return Results.NotFound();
            }

            var linkedResearchSourceIds = await db.ResearchSources
                .Where(source => source.SourceCheckId == sourceCheckId)
                .Select(source => source.Id)
                .ToListAsync(cancellationToken);
            var linkedFacts = await db.SupplierFacts
                .Where(fact => fact.ResearchSourceId.HasValue && linkedResearchSourceIds.Contains(fact.ResearchSourceId.Value))
                .ToListAsync(cancellationToken);
            var linkedResearchSources = await db.ResearchSources
                .Where(source => source.SourceCheckId == sourceCheckId)
                .ToListAsync(cancellationToken);

            db.SupplierFacts.RemoveRange(linkedFacts);
            db.ResearchSources.RemoveRange(linkedResearchSources);
            db.SourceChecks.Remove(sourceCheck);
            await db.SaveChangesAsync(cancellationToken);

            var supplier = await LoadSupplierForFactRefreshAsync(db, supplierId, cancellationToken);
            if (supplier is not null)
            {
                await researchFactExtractor.RefreshFactsAsync(supplier, db, cancellationToken);
            }

            return Results.NoContent();
        })
        .Produces(StatusCodes.Status204NoContent)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Delete supplier source check")
        .WithDescription("Deletes one source check and removes extracted facts tied to that source.")
        .WithName("DeleteSupplierSourceCheck");

        suppliers.MapPost("/{supplierId:int}/source-checks/check", async (
            int supplierId,
            CheckSourceEvidenceRequest request,
            AppDbContext db,
            ISourceEvidenceChecker sourceEvidenceChecker,
            CancellationToken cancellationToken) =>
        {
            var supplierExists = await db.Suppliers.AnyAsync(s => s.Id == supplierId, cancellationToken);
            if (!supplierExists)
            {
                return Results.NotFound();
            }

            var validationError = ValidateSourceEvidenceCheckRequest(request, out var url);
            if (validationError is not null || url is null)
            {
                return Results.BadRequest(new { error = validationError });
            }

            var checkResult = await sourceEvidenceChecker.CheckAsync(url, cancellationToken);
            var sourceCheck = new SourceCheck
            {
                SupplierId = supplierId,
                SourceName = request.SourceName.Trim(),
                Url = url.ToString(),
                Status = checkResult.Status,
                Notes = checkResult.Notes
            };

            db.SourceChecks.Add(sourceCheck);
            await db.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/suppliers/{supplierId}/source-checks/{sourceCheck.Id}",
                ToSourceCheckResponse(sourceCheck));
        })
        .Produces<SourceCheckResponse>(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Check and add supplier source evidence")
        .WithDescription("Checks a source URL from the C# API, maps the result to a source-check status, and stores the evidence record.")
        .WithName("CheckSupplierSourceEvidence");

        suppliers.MapPost("/{supplierId:int}/source-checks/research-website", async (
            int supplierId,
            ResearchWebsiteSourceRequest request,
            AppDbContext db,
            IWebsiteResearcher websiteResearcher,
            IResearchFactExtractor researchFactExtractor,
            CancellationToken cancellationToken) =>
        {
            if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var websiteUrl) ||
                websiteUrl.Scheme is not ("http" or "https"))
            {
                return Results.BadRequest(new { error = "Url must be an absolute http or https URL." });
            }

            var supplier = await db.Suppliers
                .Include(s => s.Certifications)
                .Include(s => s.SourceChecks)
                .Include(s => s.ResearchSources)
                .Include(s => s.SupplierFacts)
                .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
            if (supplier is null)
            {
                return Results.NotFound();
            }

            var research = await websiteResearcher.ResearchAsync(websiteUrl, cancellationToken);
            var sourceChecks = AddWebsiteResearchSourceChecks(supplier, research);
            if (sourceChecks.Count > 0)
            {
                await db.SaveChangesAsync(cancellationToken);
            }

            await researchFactExtractor.RefreshFactsAsync(supplier, db, cancellationToken);

            return Results.Ok(sourceChecks.Select(ToSourceCheckResponse).ToList());
        })
        .Produces<List<SourceCheckResponse>>()
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status404NotFound)
        .WithSummary("Research website source")
        .WithDescription("Researches a website URL, stores discovered pages as source checks, and refreshes supplier facts.")
        .WithName("ResearchWebsiteSource");

        return app;
    }

    private static string? ValidateSupplierRequest(CreateSupplierRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return "Name is required.";
        }

        if (string.IsNullOrWhiteSpace(request.CountryCode) || request.CountryCode.Trim().Length != 2)
        {
            return "CountryCode must be a two-letter ISO country code.";
        }

        if (string.IsNullOrWhiteSpace(request.Industry))
        {
            return "Industry is required.";
        }

        if (!string.IsNullOrWhiteSpace(request.WebsiteUrl) &&
            (!Uri.TryCreate(request.WebsiteUrl.Trim(), UriKind.Absolute, out var websiteUrl) ||
                websiteUrl.Scheme is not ("http" or "https")))
        {
            return "WebsiteUrl must be an absolute http or https URL when provided.";
        }

        if (request.RegistryNumber?.Trim().Length > 80)
        {
            return "RegistryNumber cannot be longer than 80 characters.";
        }

        if (request.VatNumber?.Trim().Length > 80)
        {
            return "VatNumber cannot be longer than 80 characters.";
        }

        if (request.CertificationHints?.Trim().Length > 500)
        {
            return "CertificationHints cannot be longer than 500 characters.";
        }

        return null;
    }

    private static string? NormalizeOptionalText(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim();
    }

    private static async Task<Supplier?> FindExistingSupplierAsync(
        CreateSupplierRequest request,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeIdentityPart(request.Name);
        var normalizedCountry = request.CountryCode.Trim().ToUpperInvariant();
        var normalizedIndustry = NormalizeIdentityPart(request.Industry);

        var candidates = await db.Suppliers
            .AsNoTracking()
            .Where(s => !s.IsArchived && s.CountryCode == normalizedCountry)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(candidate =>
            NormalizeIdentityPart(candidate.Name) == normalizedName &&
            NormalizeIdentityPart(candidate.Industry) == normalizedIndustry);
    }

    private static string BuildSupplierIdentityKey(SupplierSummaryResponse supplier)
    {
        return string.Join(
            "|",
            NormalizeIdentityPart(supplier.Name),
            supplier.CountryCode.Trim().ToUpperInvariant(),
            NormalizeIdentityPart(supplier.Industry));
    }

    private static bool IsDevelopmentSupplier(SupplierSummaryResponse supplier)
    {
        var name = supplier.Name.Trim();
        var website = supplier.WebsiteUrl ?? string.Empty;

        return ContainsDevelopmentMarker(name) ||
            website.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            website.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            website.Contains("example.com", StringComparison.OrdinalIgnoreCase) ||
            website.Contains("learning-demo", StringComparison.OrdinalIgnoreCase);
    }

    private static bool ContainsDevelopmentMarker(string value)
    {
        return value.Contains("smoke", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("debug", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("demo", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("prompt", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("learning", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("test", StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeIdentityPart(string value)
    {
        return value.Trim().ToUpperInvariant();
    }

    private static string? ValidateRiskAssessmentRequest(CreateRiskAssessmentRequest request)
    {
        return ValidateRiskAssessment(request.RiskLevel, request.Score, request.SummaryMarkdown);
    }

    private static string? ValidateRiskAssessmentRequest(UpdateRiskAssessmentRequest request)
    {
        return ValidateRiskAssessment(request.RiskLevel, request.Score, request.SummaryMarkdown);
    }

    private static string? ValidateCertificationRequest(AddCertificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Standard))
        {
            return "Standard is required.";
        }

        if (string.IsNullOrWhiteSpace(request.CertificateNumber))
        {
            return "CertificateNumber is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Issuer))
        {
            return "Issuer is required.";
        }

        if (request.Standard.Trim().Length < 3)
        {
            return "Standard must be at least 3 characters.";
        }

        if (request.CertificateNumber.Trim().Length < 5)
        {
            return "CertificateNumber must be at least 5 characters.";
        }

        if (request.ValidUntil is not null && request.ValidUntil < DateOnly.FromDateTime(DateTime.UtcNow.Date))
        {
            return "ValidUntil cannot be in the past.";
        }

        if (request.IsVerified && request.ValidUntil is null)
        {
            return "Verified certifications must include a validUntil date.";
        }

        return null;
    }

    private static string? ValidateCertificationVerificationRequest(VerifyCertificationRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Standard))
        {
            return "Standard is required.";
        }

        if (string.IsNullOrWhiteSpace(request.CertificateNumber))
        {
            return "CertificateNumber is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Issuer))
        {
            return "Issuer is required.";
        }

        if (request.Standard.Trim().Length < 3)
        {
            return "Standard must be at least 3 characters.";
        }

        if (request.CertificateNumber.Trim().Length < 5)
        {
            return "CertificateNumber must be at least 5 characters.";
        }

        return null;
    }

    private static string? ValidateSourceCheckRequest(AddSourceCheckRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.SourceName))
        {
            return "SourceName is required.";
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return "Url is required.";
        }

        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var url) ||
            url.Scheme is not ("http" or "https"))
        {
            return "Url must be an absolute http or https URL.";
        }

        if (request.SourceName.Trim().Length < 3)
        {
            return "SourceName must be at least 3 characters.";
        }

        if (request.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed &&
            string.IsNullOrWhiteSpace(request.Notes))
        {
            return "Blocked or failed source checks must include notes.";
        }

        if (request.Status is SourceCheckStatus.Reachable &&
            string.IsNullOrWhiteSpace(request.Notes))
        {
            return "Reachable source checks must include notes explaining what was checked.";
        }

        return null;
    }

    private static string? ValidateSourceEvidenceCheckRequest(
        CheckSourceEvidenceRequest request,
        out Uri? url)
    {
        url = null;

        if (string.IsNullOrWhiteSpace(request.SourceName))
        {
            return "SourceName is required.";
        }

        if (request.SourceName.Trim().Length < 3)
        {
            return "SourceName must be at least 3 characters.";
        }

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            return "Url is required.";
        }

        if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out url) ||
            url.Scheme is not ("http" or "https"))
        {
            return "Url must be an absolute http or https URL.";
        }

        return null;
    }

    private static string? ValidateRiskAssessment(
        SupplierRiskLevel riskLevel,
        int score,
        string summaryMarkdown)
    {
        if (riskLevel is SupplierRiskLevel.Unknown)
        {
            return "RiskLevel must be Low, Medium, or High.";
        }

        if (score is < 0 or > 100)
        {
            return "Score must be between 0 and 100.";
        }

        if (string.IsNullOrWhiteSpace(summaryMarkdown))
        {
            return "SummaryMarkdown is required.";
        }

        return null;
    }

    private static SupplierDetailResponse ToDetailResponse(Supplier supplier)
    {
        return new SupplierDetailResponse(
            supplier.Id,
            supplier.Name,
            supplier.CountryCode,
            supplier.Industry,
            supplier.WebsiteUrl,
            supplier.RegistryNumber,
            supplier.VatNumber,
            supplier.CertificationHints,
            supplier.RiskLevel,
            supplier.IsArchived,
            supplier.CreatedAt,
            supplier.AnalysisJobs
                .OrderByDescending(j => j.CreatedAt)
                .Select(ToAnalysisJobResponse)
                .ToList(),
            supplier.Certifications
                .OrderBy(c => c.Standard)
                .Select(ToCertificationResponse)
                .ToList(),
            supplier.SourceChecks
                .OrderByDescending(s => s.CheckedAt)
                .Select(ToSourceCheckResponse)
                .ToList(),
            supplier.ResearchSources
                .OrderByDescending(r => r.CreatedAt)
                .Select(ToResearchSourceResponse)
                .ToList(),
            supplier.SupplierFacts
                .OrderBy(f => f.FactType)
                .ThenByDescending(f => f.Confidence)
                .Select(ToSupplierFactResponse)
                .ToList(),
            supplier.RiskAssessments
                .OrderByDescending(r => r.CreatedAt)
                .Select(ToRiskAssessmentResponse)
                .ToList());
    }

    private static AnalysisJobResponse ToAnalysisJobResponse(AnalysisJob analysisJob)
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

    private static CertificationResponse ToCertificationResponse(Certification certification)
    {
        return new CertificationResponse(
            certification.Id,
            certification.Standard,
            certification.CertificateNumber,
            certification.Issuer,
            certification.ValidUntil,
            certification.IsVerified,
            certification.VerificationNotes,
            certification.CreatedAt);
    }

    private static SourceCheckResponse ToSourceCheckResponse(SourceCheck sourceCheck)
    {
        return new SourceCheckResponse(
            sourceCheck.Id,
            sourceCheck.SourceName,
            sourceCheck.Url,
            sourceCheck.Status,
            sourceCheck.Notes,
            sourceCheck.CheckedAt);
    }

    private static ResearchSourceResponse ToResearchSourceResponse(ResearchSource researchSource)
    {
        return new ResearchSourceResponse(
            researchSource.Id,
            researchSource.SourceCheckId,
            researchSource.SourceName,
            researchSource.Url,
            researchSource.Kind,
            researchSource.Status,
            researchSource.Relevance,
            researchSource.Summary,
            researchSource.CreatedAt);
    }

    private static SupplierFactResponse ToSupplierFactResponse(SupplierFact supplierFact)
    {
        return new SupplierFactResponse(
            supplierFact.Id,
            supplierFact.ResearchSourceId,
            supplierFact.FactType,
            supplierFact.Value,
            supplierFact.EvidenceText,
            supplierFact.SourceName,
            supplierFact.SourceUrl,
            supplierFact.Confidence,
            supplierFact.CreatedAt);
    }

    private static RiskAssessmentResponse ToRiskAssessmentResponse(RiskAssessment riskAssessment)
    {
        return new RiskAssessmentResponse(
            riskAssessment.Id,
            riskAssessment.RiskLevel,
            riskAssessment.Score,
            riskAssessment.Focus,
            riskAssessment.SummaryMarkdown,
            riskAssessment.Provider,
            riskAssessment.Model,
            riskAssessment.PromptFocus,
            riskAssessment.EvidenceSnapshotJson,
            riskAssessment.GenerationDurationMs,
            riskAssessment.CreatedAt);
    }

    private static async Task RunInitialSupplierAnalysisAsync(
        Supplier supplier,
        ISourceEvidenceChecker sourceEvidenceChecker,
        IWebsiteCertificationDiscovery websiteCertificationDiscovery,
        ILocalModelClient localModel,
        CancellationToken cancellationToken)
    {
        if (supplier.WebsiteUrl is not null &&
            Uri.TryCreate(supplier.WebsiteUrl, UriKind.Absolute, out var websiteUrl))
        {
            var checkResult = await sourceEvidenceChecker.CheckAsync(websiteUrl, cancellationToken);
            supplier.SourceChecks.Add(new SourceCheck
            {
                SupplierId = supplier.Id,
                SourceName = "Supplier website",
                Url = websiteUrl.ToString(),
                Status = checkResult.Status,
                Notes = checkResult.Notes
            });

            var discoveredCertifications = await websiteCertificationDiscovery.DiscoverAsync(
                websiteUrl,
                cancellationToken);
            AddDiscoveredCertifications(supplier, discoveredCertifications);
        }

        try
        {
            var estimatedRiskLevel = EvidenceQualityCalculator.EstimateRiskLevel(supplier);
            var estimatedRiskScore = EvidenceQualityCalculator.EstimateRiskScore(supplier);
            var promptFocus = "Initial supplier intelligence review. Identify missing supplier evidence clearly.";
            var evidenceSnapshotJson = SupplierEvidenceSnapshotBuilder.Build(
                supplier,
                estimatedRiskLevel,
                estimatedRiskScore);

            var generationTimer = Stopwatch.StartNew();
            var briefing = await localModel.ChatAsync(
                localModel.DefaultModel,
                BuildRiskAssessmentSystemPrompt(),
                BuildRiskAssessmentUserPrompt(promptFocus, evidenceSnapshotJson),
                cancellationToken);
            generationTimer.Stop();

            supplier.RiskLevel = estimatedRiskLevel;
            supplier.RiskAssessments.Add(new RiskAssessment
            {
                SupplierId = supplier.Id,
                RiskLevel = estimatedRiskLevel,
                Score = estimatedRiskScore,
                Focus = promptFocus,
                SummaryMarkdown = briefing,
                Provider = localModel.Provider,
                Model = localModel.DefaultModel,
                PromptFocus = promptFocus,
                EvidenceSnapshotJson = evidenceSnapshotJson,
                GenerationDurationMs = (int)Math.Min(generationTimer.ElapsedMilliseconds, int.MaxValue)
            });
        }
        catch (LocalModelException)
        {
        }
    }

    private static List<Certification> AddDiscoveredCertifications(
        Supplier supplier,
        IReadOnlyList<DiscoveredCertification> discoveredCertifications)
    {
        var certifications = new List<Certification>();

        foreach (var discovered in discoveredCertifications)
        {
            var alreadyExists = supplier.Certifications.Any(certification =>
                certification.Standard.Equals(discovered.Standard, StringComparison.OrdinalIgnoreCase) &&
                certification.CertificateNumber.Equals(discovered.CertificateNumber, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                continue;
            }

            var certification = new Certification
            {
                SupplierId = supplier.Id,
                Standard = discovered.Standard,
                CertificateNumber = discovered.CertificateNumber,
                Issuer = discovered.Issuer,
                ValidUntil = discovered.ValidUntil,
                IsVerified = false,
                VerificationNotes = discovered.Notes
            };

            supplier.Certifications.Add(certification);
            certifications.Add(certification);
        }

        return certifications;
    }

    private static SupplierReviewSummaryResponse BuildReviewSummary(Supplier supplier)
    {
        var latestAssessment = supplier.RiskAssessments
            .OrderByDescending(assessment => assessment.CreatedAt)
            .FirstOrDefault();
        var confirmedIdentity = supplier.MatchCandidates
            .Where(candidate => candidate.Status == SupplierMatchCandidateStatus.Confirmed)
            .OrderByDescending(candidate => candidate.ReviewedAt ?? candidate.CreatedAt)
            .FirstOrDefault();
        var reachableSourceCount = supplier.SourceChecks.Count(source => source.Status == SourceCheckStatus.Reachable);
        var failedSourceCount = supplier.SourceChecks.Count(source => source.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed);
        var hasRegistrationEvidence = supplier.SourceChecks.Any(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsRegistrationEvidence(source));
        var missingInformation = BuildMissingInformation(
            reachableSourceCount,
            failedSourceCount,
            hasRegistrationEvidence,
            supplier.SupplierFacts.Count);
        var knownInformation = BuildKnownInformation(
            supplier,
            confirmedIdentity,
            latestAssessment,
            reachableSourceCount,
            hasRegistrationEvidence);
        var trustSignals = new SupplierTrustSignalsResponse(
            supplier.SupplierFacts.Count > 0 ? $"{supplier.SupplierFacts.Count} facts" : "No facts yet",
            reachableSourceCount == 0 ? "Missing" : reachableSourceCount >= 3 ? "Good" : "Partial",
            failedSourceCount == 0 ? "No blockers" : $"{failedSourceCount} blocked",
            hasRegistrationEvidence ? "Company source found" : "Company source unclear");
        var nextAction = BuildReviewNextAction(
            missingInformation,
            latestAssessment);

        return new SupplierReviewSummaryResponse(
            supplier.Id,
            supplier.Name,
            BuildReviewHeadline(reachableSourceCount, missingInformation, latestAssessment),
            nextAction,
            knownInformation,
            missingInformation,
            trustSignals);
    }

    private static List<SupplierConnectionResponse> BuildSupplierConnections(
        Supplier supplier,
        IReadOnlyList<Supplier> suppliers)
    {
        var currentTerms = ExtractSupplierConnectionTerms(supplier);
        var currentHosts = ExtractSourceHosts(supplier);

        return suppliers
            .Where(other => other.Id != supplier.Id && !IsDevelopmentSupplier(other))
            .Select(other =>
            {
                var reasons = new List<string>();
                var sharedTerms = currentTerms
                    .Intersect(ExtractSupplierConnectionTerms(other), StringComparer.OrdinalIgnoreCase)
                    .Take(6)
                    .ToList();
                var sharedHosts = currentHosts
                    .Intersect(ExtractSourceHosts(other), StringComparer.OrdinalIgnoreCase)
                    .Take(3)
                    .ToList();
                var score = 0;

                if (supplier.CountryCode.Equals(other.CountryCode, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"Same country: {other.CountryCode}");
                }

                if (supplier.Industry.Equals(other.Industry, StringComparison.OrdinalIgnoreCase))
                {
                    reasons.Add($"Same industry: {other.Industry}");
                }

                if (sharedTerms.Count > 0)
                {
                    score += Math.Min(60, sharedTerms.Count * 12);
                    reasons.Add($"Shared research terms: {string.Join(", ", sharedTerms.Take(3))}");
                }

                if (sharedHosts.Count > 0)
                {
                    score += Math.Min(40, sharedHosts.Count * 20);
                    reasons.Add($"Shared source host: {string.Join(", ", sharedHosts)}");
                }

                if (score > 0 &&
                    supplier.CountryCode.Equals(other.CountryCode, StringComparison.OrdinalIgnoreCase))
                {
                    score += 10;
                }

                if (score > 0 &&
                    supplier.Industry.Equals(other.Industry, StringComparison.OrdinalIgnoreCase))
                {
                    score += 15;
                }

                return new
                {
                    Supplier = other,
                    Score = score,
                    Reasons = reasons,
                    SharedTerms = sharedTerms
                };
            })
            .Where(connection => connection.Score >= 35)
            .OrderByDescending(connection => connection.Score)
            .ThenBy(connection => connection.Supplier.Name)
            .Take(8)
            .Select(connection => new SupplierConnectionResponse(
                connection.Supplier.Id,
                connection.Supplier.Name,
                connection.Supplier.CountryCode,
                connection.Supplier.Industry,
                connection.Supplier.WebsiteUrl,
                FormatConnectionStrength(connection.Score),
                connection.Reasons,
                connection.SharedTerms))
            .ToList();
    }

    private static SupplierComparisonResponse BuildSupplierComparison(
        IReadOnlyList<Supplier> selectedSuppliers,
        IReadOnlyList<Supplier> suppliersWithResearch)
    {
        var items = selectedSuppliers
            .Select(supplier =>
            {
                var connections = BuildSupplierConnections(supplier, suppliersWithResearch);
                return BuildSupplierComparisonItem(supplier, connections.Count);
            })
            .ToList();

        var selectedTerms = selectedSuppliers
            .Select(supplier => ExtractSupplierConnectionTerms(supplier))
            .ToList();
        var overlappingTerms = selectedTerms.Count == 0
            ? []
            : selectedTerms
                .Skip(1)
                .Aggregate(
                    selectedTerms[0],
                    (current, next) => current.Intersect(next, StringComparer.OrdinalIgnoreCase).ToHashSet(StringComparer.OrdinalIgnoreCase))
                .Where(term => term.Length >= 4)
                .OrderBy(term => term)
                .Take(8)
                .ToList();

        var strongestEvidence = items
            .OrderByDescending(item => item.ReachableSourceCount)
            .ThenByDescending(item => item.KnownFacts.Count)
            .Take(2)
            .Select(item => $"{item.SupplierName}: {item.ReachableSourceCount} reachable sources, {item.KnownFacts.Count} facts")
            .ToList();
        var weakestCoverage = items
            .OrderBy(item => item.ReachableSourceCount)
            .ThenBy(item => item.KnownFacts.Count)
            .Take(2)
            .Select(item => $"{item.SupplierName}: {item.ReachableSourceCount} reachable sources, {item.OpenQuestions.Count} open gaps")
            .ToList();

        return new SupplierComparisonResponse(
            items,
            new SupplierComparisonInsightsResponse(
                selectedSuppliers
                    .GroupBy(supplier => supplier.CountryCode, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .Order()
                    .ToList(),
                selectedSuppliers
                    .GroupBy(supplier => supplier.Industry, StringComparer.OrdinalIgnoreCase)
                    .Where(group => group.Count() > 1)
                    .Select(group => group.Key)
                    .Order()
                    .ToList(),
                overlappingTerms,
                strongestEvidence,
                weakestCoverage));
    }

    private static async Task<List<Supplier>> LoadSuppliersWithResearchAsync(
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        return await db.Suppliers
            .AsNoTracking()
            .Include(s => s.SourceChecks)
            .Include(s => s.ResearchSources)
            .Include(s => s.SupplierFacts)
            .Include(s => s.RiskAssessments)
            .Where(s => !s.IsArchived)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);
    }

    private static async Task<CompareBoard?> LoadCompareBoardAsync(
        AppDbContext db,
        int boardId,
        CancellationToken cancellationToken)
    {
        return await db.CompareBoards
            .AsNoTracking()
            .Include(board => board.Suppliers)
            .ThenInclude(boardSupplier => boardSupplier.Supplier)
            .AsSplitQuery()
            .FirstOrDefaultAsync(board => board.Id == boardId, cancellationToken);
    }

    private static CompareBoardResponse ToCompareBoardResponse(
        CompareBoard board,
        IReadOnlyList<Supplier> suppliersWithResearch)
    {
        var supplierOrder = board.Suppliers
            .Where(boardSupplier => !boardSupplier.Supplier.IsArchived)
            .OrderBy(boardSupplier => boardSupplier.SortOrder)
            .ThenBy(boardSupplier => boardSupplier.AddedAt)
            .ToList();
        var selectedSuppliers = supplierOrder
            .Select(boardSupplier => suppliersWithResearch.FirstOrDefault(supplier => supplier.Id == boardSupplier.SupplierId))
            .Where(supplier => supplier is not null)
            .Cast<Supplier>()
            .ToList();

        return new CompareBoardResponse(
            board.Id,
            board.Name,
            board.CreatedAt,
            board.UpdatedAt,
            supplierOrder
                .Select(boardSupplier => new CompareBoardSupplierResponse(
                    boardSupplier.SupplierId,
                    boardSupplier.Supplier.Name,
                    boardSupplier.Supplier.CountryCode,
                    boardSupplier.Supplier.Industry,
                    boardSupplier.Supplier.WebsiteUrl,
                    boardSupplier.SortOrder,
                    boardSupplier.AddedAt))
                .ToList(),
            BuildSupplierComparison(selectedSuppliers, suppliersWithResearch));
    }

    private static string? NormalizeCompareBoardName(string value)
    {
        var name = value.Trim();

        return string.IsNullOrWhiteSpace(name) || name.Length > 160
            ? null
            : name;
    }

    private static string BuildSupplierMarkdownReport(
        Supplier supplier,
        IReadOnlyList<SupplierConnectionResponse> connections)
    {
        var latestAssessment = supplier.RiskAssessments
            .OrderByDescending(assessment => assessment.CreatedAt)
            .FirstOrDefault();
        var facts = supplier.SupplierFacts
            .Where(fact => fact.FactType is not SupplierFactType.MissingEvidence and not SupplierFactType.SourceLimitation)
            .OrderByDescending(fact => FactConfidenceScore(fact.Confidence))
            .ThenByDescending(fact => fact.CreatedAt)
            .ToList();
        var reachableSourceCount = supplier.SourceChecks.Count(source => source.Status == SourceCheckStatus.Reachable);
        var report = new StringBuilder();

        report.AppendLine($"# {MarkdownText(supplier.Name)}");
        report.AppendLine();
        report.AppendLine($"- Country: {MarkdownText(supplier.CountryCode)}");
        report.AppendLine($"- Industry: {MarkdownText(supplier.Industry)}");
        report.AppendLine($"- Website: {(string.IsNullOrWhiteSpace(supplier.WebsiteUrl) ? "Not provided" : MarkdownText(supplier.WebsiteUrl))}");
        report.AppendLine($"- Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC");
        report.AppendLine();

        report.AppendLine("## Briefing Summary");
        report.AppendLine();
        report.AppendLine(MarkdownText(BuildComparisonCompanySummary(supplier, latestAssessment, facts)));
        report.AppendLine();

        AppendMarkdownList(
            report,
            "## Products / Services",
            BuildComparisonFactItems(facts, SupplierFactType.ProductsAndServices));
        AppendMarkdownList(
            report,
            "## Locations / Markets",
            BuildComparisonLocationItems(supplier, facts));
        AppendMarkdownList(
            report,
            "## Useful Facts",
            facts
                .Select(fact => $"{FormatFactType(fact.FactType)}: {ShortenText(CleanComparisonText(fact.Value), 220)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .ToList());

        report.AppendLine("## Useful Sources");
        report.AppendLine();
        var sources = BuildComparisonSources(supplier);
        if (sources.Count == 0)
        {
            report.AppendLine("- No sources stored yet.");
        }
        else
        {
            foreach (var source in sources)
            {
                report.AppendLine($"- {MarkdownText(source.SourceName)} ({MarkdownText(source.Status)}): {MarkdownText(source.Url)}");
                if (!string.IsNullOrWhiteSpace(source.Summary))
                {
                    report.AppendLine($"  - {MarkdownText(source.Summary)}");
                }
            }
        }
        report.AppendLine();

        AppendMarkdownList(
            report,
            "## Open Gaps",
            BuildComparisonOpenQuestions(supplier, latestAssessment, facts, reachableSourceCount));
        AppendMarkdownList(
            report,
            "## Related Suppliers",
            connections
                .Take(8)
                .Select(connection => $"{connection.SupplierName} ({connection.CountryCode} · {connection.Industry}) - {connection.StrengthLabel}: {string.Join("; ", connection.Reasons.Take(2))}")
                .ToList());

        report.AppendLine("## Analysis Runs");
        report.AppendLine();
        var analysisJobs = supplier.AnalysisJobs
            .OrderByDescending(job => job.CreatedAt)
            .Take(6)
            .ToList();
        if (analysisJobs.Count == 0)
        {
            report.AppendLine("- No analysis jobs stored.");
        }
        else
        {
            foreach (var job in analysisJobs)
            {
                report.AppendLine($"- {job.CreatedAt:yyyy-MM-dd HH:mm}: {job.Status} / {job.JobType} - {MarkdownText(job.ProgressMessage)}");
                if (!string.IsNullOrWhiteSpace(job.ErrorMessage))
                {
                    report.AppendLine($"  - Error: {MarkdownText(job.ErrorMessage)}");
                }
            }
        }

        return report.ToString();
    }

    private static void AppendMarkdownList(
        StringBuilder report,
        string title,
        IReadOnlyList<string> items)
    {
        report.AppendLine(title);
        report.AppendLine();

        if (items.Count == 0)
        {
            report.AppendLine("- No stored information yet.");
        }
        else
        {
            foreach (var item in items)
            {
                report.AppendLine($"- {MarkdownText(item)}");
            }
        }

        report.AppendLine();
    }

    private static string MarkdownText(string value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? "Not available"
            : value.Replace("\r", " ").Replace("\n", " ").Trim();
    }

    private static string SlugifyFileName(string value)
    {
        var slug = new string(value
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray())
            .Trim('-');

        while (slug.Contains("--", StringComparison.Ordinal))
        {
            slug = slug.Replace("--", "-", StringComparison.Ordinal);
        }

        return string.IsNullOrWhiteSpace(slug) ? "supplier" : slug;
    }

    private static SupplierComparisonItemResponse BuildSupplierComparisonItem(
        Supplier supplier,
        int relatedSupplierCount)
    {
        var reachableSourceCount = supplier.SourceChecks.Count(source => source.Status == SourceCheckStatus.Reachable);
        var failedSourceCount = supplier.SourceChecks.Count(source =>
            source.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed);
        var latestAssessment = supplier.RiskAssessments
            .OrderByDescending(assessment => assessment.CreatedAt)
            .FirstOrDefault();
        var facts = supplier.SupplierFacts
            .Where(fact => fact.FactType is not SupplierFactType.MissingEvidence and not SupplierFactType.SourceLimitation)
            .OrderByDescending(fact => FactConfidenceScore(fact.Confidence))
            .ThenByDescending(fact => fact.CreatedAt)
            .ToList();

        return new SupplierComparisonItemResponse(
            supplier.Id,
            supplier.Name,
            supplier.CountryCode,
            supplier.Industry,
            supplier.WebsiteUrl,
            BuildComparisonCompanySummary(supplier, latestAssessment, facts),
            BuildComparisonFactItems(facts, SupplierFactType.ProductsAndServices),
            BuildComparisonLocationItems(supplier, facts),
            reachableSourceCount,
            failedSourceCount,
            BuildComparisonSources(supplier),
            facts
                .Select(fact => $"{FormatFactType(fact.FactType)}: {ShortenText(fact.Value, 180)}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(6)
                .ToList(),
            BuildComparisonOpenQuestions(supplier, latestAssessment, facts, reachableSourceCount),
            relatedSupplierCount);
    }

    private static string BuildComparisonCompanySummary(
        Supplier supplier,
        RiskAssessment? latestAssessment,
        IReadOnlyList<SupplierFact> facts)
    {
        var description = facts
            .Where(fact => fact.FactType is SupplierFactType.CompanyDescription or SupplierFactType.IndustryProfile)
            .Select(fact => fact.Value)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

        if (!string.IsNullOrWhiteSpace(description))
        {
            return ShortenText(CleanComparisonText(description), 260);
        }

        if (!string.IsNullOrWhiteSpace(latestAssessment?.SummaryMarkdown))
        {
            return ShortenText(CleanComparisonText(latestAssessment.SummaryMarkdown), 260);
        }

        return $"{supplier.Name} is tracked as a supplier in {supplier.Industry} for country {supplier.CountryCode}.";
    }

    private static List<string> BuildComparisonFactItems(
        IReadOnlyList<SupplierFact> facts,
        params SupplierFactType[] factTypes)
    {
        var allowedTypes = factTypes.ToHashSet();

        return facts
            .Where(fact => allowedTypes.Contains(fact.FactType))
            .Select(fact => ShortenText(CleanComparisonText(fact.Value), 150))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();
    }

    private static List<string> BuildComparisonLocationItems(
        Supplier supplier,
        IReadOnlyList<SupplierFact> facts)
    {
        var items = BuildComparisonFactItems(
            facts,
            SupplierFactType.LocationsAndMarkets,
            SupplierFactType.LegalIdentity,
            SupplierFactType.RegistryEvidence);

        if (items.Count == 0)
        {
            items.Add($"{supplier.CountryCode} · {supplier.Industry}");
        }

        return items.Take(4).ToList();
    }

    private static List<SupplierComparisonSourceResponse> BuildComparisonSources(Supplier supplier)
    {
        var sourceChecks = supplier.SourceChecks
            .OrderBy(source => source.Status == SourceCheckStatus.Reachable ? 0 : 1)
            .ThenByDescending(source => source.CheckedAt)
            .Take(4)
            .Select(source => new SupplierComparisonSourceResponse(
                source.SourceName,
                source.Url,
                source.Status.ToString(),
                ShortenText(CleanComparisonText(source.Notes), 170)))
            .ToList();

        if (sourceChecks.Count > 0)
        {
            return sourceChecks;
        }

        return supplier.ResearchSources
            .OrderByDescending(source => FactConfidenceScore(source.Relevance))
            .ThenByDescending(source => source.CreatedAt)
            .Take(4)
            .Select(source => new SupplierComparisonSourceResponse(
                source.SourceName,
                source.Url,
                source.Status.ToString(),
                ShortenText(CleanComparisonText(source.Summary), 170)))
            .ToList();
    }

    private static List<string> BuildComparisonOpenQuestions(
        Supplier supplier,
        RiskAssessment? latestAssessment,
        IReadOnlyList<SupplierFact> facts,
        int reachableSourceCount)
    {
        var questions = new List<string>();

        if (reachableSourceCount == 0)
        {
            questions.Add("No reachable public source is stored yet.");
        }

        if (!supplier.SupplierFacts.Any(fact => fact.FactType == SupplierFactType.CompanyDescription))
        {
            questions.Add("Company description still needs stronger source support.");
        }

        if (!facts.Any(fact => fact.FactType == SupplierFactType.ProductsAndServices))
        {
            questions.Add("Products or services are not clearly extracted yet.");
        }

        if (latestAssessment is null)
        {
            questions.Add("No research memo has been generated yet.");
        }

        questions.AddRange(supplier.SupplierFacts
            .Where(fact => fact.FactType is SupplierFactType.MissingEvidence or SupplierFactType.SourceLimitation)
            .OrderByDescending(fact => fact.CreatedAt)
            .Select(fact => ShortenText(CleanComparisonText(fact.Value), 150)));

        return questions
            .Where(question => !string.IsNullOrWhiteSpace(question))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToList();
    }

    private static int FactConfidenceScore(FactConfidence confidence)
    {
        return confidence switch
        {
            FactConfidence.High => 3,
            FactConfidence.Medium => 2,
            _ => 1
        };
    }

    private static string CleanComparisonText(string value)
    {
        return value
            .Replace("**", string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("  ", " ")
            .Trim(' ', '-', '•');
    }

    private static HashSet<string> ExtractSupplierConnectionTerms(Supplier supplier)
    {
        var terms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AddConnectionTerms(terms, supplier.Name);
        AddConnectionTerms(terms, supplier.Industry);

        foreach (var fact in supplier.SupplierFacts)
        {
            if (fact.FactType is SupplierFactType.MissingEvidence or SupplierFactType.SourceLimitation)
            {
                continue;
            }

            AddConnectionTerms(terms, fact.Value);
        }

        foreach (var source in supplier.SourceChecks)
        {
            AddConnectionTerms(terms, source.Notes);
        }

        return terms;
    }

    private static void AddConnectionTerms(HashSet<string> terms, string value)
    {
        foreach (var part in value.Split(new[] { ' ', '\t', '\r', '\n', ',', '.', ';', ':', '/', '\\', '-', '_', '(', ')', '[', ']', '{', '}', '"' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var term = part.Trim().ToLowerInvariant();
            if (term.Length < 4 ||
                int.TryParse(term, out _) ||
                ConnectionStopWords.Contains(term))
            {
                continue;
            }

            terms.Add(term);
        }
    }

    private static HashSet<string> ExtractSourceHosts(Supplier supplier)
    {
        return supplier.SourceChecks
            .Select(source => FormatHost(source.Url))
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsDevelopmentSupplier(Supplier supplier)
    {
        var website = supplier.WebsiteUrl ?? string.Empty;

        return ContainsDevelopmentMarker(supplier.Name) ||
            website.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            website.Contains("localhost", StringComparison.OrdinalIgnoreCase) ||
            website.Contains("example.com", StringComparison.OrdinalIgnoreCase) ||
            website.Contains("learning-demo", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<Supplier?> LoadSupplierForFactRefreshAsync(
        AppDbContext db,
        int supplierId,
        CancellationToken cancellationToken)
    {
        return await db.Suppliers
            .Include(s => s.Certifications)
            .Include(s => s.SourceChecks)
            .Include(s => s.ResearchSources)
            .Include(s => s.SupplierFacts)
            .FirstOrDefaultAsync(s => s.Id == supplierId, cancellationToken);
    }

    private static List<SourceCheck> AddWebsiteResearchSourceChecks(
        Supplier supplier,
        WebsiteResearchResult research)
    {
        var sourceChecks = new List<SourceCheck>();

        foreach (var page in research.Pages)
        {
            var alreadyExists = supplier.SourceChecks.Any(sourceCheck =>
                sourceCheck.Url.Equals(page.Url, StringComparison.OrdinalIgnoreCase) &&
                sourceCheck.SourceName.StartsWith("Website research", StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                continue;
            }

            var sourceCheck = new SourceCheck
            {
                SupplierId = supplier.Id,
                SourceName = $"Website research: {page.PageType}",
                Url = page.Url,
                Status = SourceCheckStatus.Reachable,
                Notes = BuildWebsiteResearchNotes(page)
            };

            supplier.SourceChecks.Add(sourceCheck);
            sourceChecks.Add(sourceCheck);
        }

        return sourceChecks;
    }

    private static string BuildWebsiteResearchNotes(WebsiteResearchPage page)
    {
        var terms = page.MatchedTerms.Count == 0
            ? "No certification or compliance terms found."
            : $"Matched terms: {string.Join(", ", page.MatchedTerms)}.";

        var notes = $"Title: {page.Title}. Description: {page.Description}. {terms} Text: {page.TextSnippet}";
        return notes.Length <= 2200
            ? notes
            : notes[..2197] + "...";
    }

    private static string FormatConnectionStrength(int score)
    {
        return score switch
        {
            >= 70 => "Strong similarity",
            >= 40 => "Useful similarity",
            _ => "Light similarity"
        };
    }

    private static readonly HashSet<string> ConnectionStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "about",
        "after",
        "also",
        "and",
        "are",
        "available",
        "based",
        "been",
        "business",
        "company",
        "evidence",
        "from",
        "have",
        "http",
        "https",
        "information",
        "into",
        "limited",
        "notes",
        "public",
        "source",
        "supplier",
        "their",
        "this",
        "with"
    };

    private static SupplierAnalyticsResponse BuildAnalytics(Supplier supplier)
    {
        var latestAssessment = supplier.RiskAssessments
            .OrderByDescending(assessment => assessment.CreatedAt)
            .FirstOrDefault();
        var confirmedIdentity = supplier.MatchCandidates
            .Where(candidate => candidate.Status == SupplierMatchCandidateStatus.Confirmed)
            .OrderByDescending(candidate => candidate.ReviewedAt ?? candidate.CreatedAt)
            .FirstOrDefault();
        var proposedIdentityCount = supplier.MatchCandidates.Count(candidate => candidate.Status == SupplierMatchCandidateStatus.Proposed);
        var reachableSources = supplier.SourceChecks.Count(source => source.Status == SourceCheckStatus.Reachable);
        var failedSources = supplier.SourceChecks.Count(source => source.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed);
        var verifiedCertifications = supplier.Certifications.Count(certification => certification.IsVerified);
        var claimedCertifications = supplier.Certifications.Count(certification => !certification.IsVerified);
        var hasRegistrationEvidence = supplier.SourceChecks.Any(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsRegistrationEvidence(source));
        var hasCertificationEvidence = supplier.SourceChecks.Any(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsCertificationEvidence(source)) || supplier.Certifications.Count > 0;

        var trustBreakdown = new List<TrustBreakdownItemResponse>
        {
            BuildIdentityTrustItem(confirmedIdentity, proposedIdentityCount),
            BuildEvidenceTrustItem(reachableSources, failedSources),
            BuildCertificationTrustItem(verifiedCertifications, claimedCertifications, hasCertificationEvidence),
            BuildRegistrationTrustItem(hasRegistrationEvidence),
            BuildRiskTrustItem(latestAssessment, reachableSources, confirmedIdentity is not null)
        };
        var overallTrustScore = (int)Math.Round(trustBreakdown.Average(item => item.Score));
        var strongestSignals = BuildStrongestSignals(
            supplier,
            confirmedIdentity,
            reachableSources,
            hasRegistrationEvidence,
            latestAssessment);
        var weakestGaps = BuildAnalyticsGaps(
            confirmedIdentity,
            reachableSources,
            failedSources,
            hasRegistrationEvidence,
            latestAssessment);

        return new SupplierAnalyticsResponse(
            supplier.Id,
            supplier.Name,
            overallTrustScore,
            trustBreakdown,
            BuildSourceMix(supplier),
            BuildTimeline(supplier),
            strongestSignals,
            weakestGaps);
    }

    private static object BuildOpenQuestionEvidence(Supplier supplier, IReadOnlyList<string> questions)
    {
        return new
        {
            supplier = new
            {
                supplier.Id,
                supplier.Name,
                supplier.CountryCode,
                supplier.Industry,
                supplier.WebsiteUrl
            },
            questions,
            facts = supplier.SupplierFacts
                .Where(fact => fact.FactType is not SupplierFactType.MissingEvidence and not SupplierFactType.SourceLimitation)
                .OrderByDescending(fact => fact.Confidence)
                .ThenByDescending(fact => fact.CreatedAt)
                .Take(40)
                .Select(fact => new
                {
                    type = fact.FactType.ToString(),
                    fact.Value,
                    fact.SourceName,
                    fact.SourceUrl,
                    confidence = fact.Confidence.ToString()
                }),
            sources = supplier.SourceChecks
                .OrderByDescending(source => source.CheckedAt)
                .Take(30)
                .Select(source => new
                {
                    source.SourceName,
                    source.Url,
                    status = source.Status.ToString(),
                    notes = ShortenText(source.Notes, 600)
                })
        };
    }

    private static string BuildOpenQuestionRecheckSystemPrompt()
    {
        return """
            You resolve supplier open questions using only the JSON evidence supplied by the app.
            Do not browse the web. Do not invent missing facts.
            For every input question, return whether existing facts or source notes resolve it.
            Mark a question "resolved" only if the evidence directly answers it.
            Mark it "unresolved" if the evidence is missing, vague, contradictory, or only indirectly related.
            Use short evidence notes. Prefer exact facts, source names, addresses, products, websites, countries, and markets.
            Return JSON only with this exact shape:
            {
              "resolved": [
                { "question": "...", "status": "resolved", "evidenceNote": "...", "sourceName": "..." }
              ],
              "unresolved": [
                { "question": "...", "status": "unresolved", "evidenceNote": "...", "sourceName": "" }
              ]
            }
            """;
    }

    private static RecheckOpenQuestionsResponse ParseOpenQuestionRecheckResponse(
        string modelResponse,
        IReadOnlyList<string> fallbackQuestions)
    {
        try
        {
            var json = ExtractJsonObject(modelResponse);
            var parsed = JsonSerializer.Deserialize<OpenQuestionRecheckModelResponse>(
                json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null)
            {
                return BuildFallbackOpenQuestionResponse(fallbackQuestions);
            }

            var resolved = MapQuestionResolutions(parsed.Resolved, "resolved");
            var unresolved = MapQuestionResolutions(parsed.Unresolved, "unresolved");
            var returnedQuestions = resolved
                .Concat(unresolved)
                .Select(item => item.Question)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            unresolved.AddRange(fallbackQuestions
                .Where(question => !returnedQuestions.Contains(question))
                .Select(question => new OpenQuestionResolutionResponse(
                    question,
                    "unresolved",
                    "The model did not return a resolution for this question.",
                    string.Empty)));

            return new RecheckOpenQuestionsResponse(resolved, unresolved);
        }
        catch (JsonException)
        {
            return BuildFallbackOpenQuestionResponse(fallbackQuestions);
        }
    }

    private static string ExtractJsonObject(string value)
    {
        var start = value.IndexOf('{', StringComparison.Ordinal);
        var end = value.LastIndexOf('}');

        return start >= 0 && end > start
            ? value[start..(end + 1)]
            : value;
    }

    private static List<OpenQuestionResolutionResponse> MapQuestionResolutions(
        IReadOnlyList<OpenQuestionResolutionModel>? items,
        string status)
    {
        return (items ?? [])
            .Where(item => !string.IsNullOrWhiteSpace(item.Question))
            .Select(item => new OpenQuestionResolutionResponse(
                item.Question.Trim(),
                status,
                string.IsNullOrWhiteSpace(item.EvidenceNote) ? "No evidence note returned." : item.EvidenceNote.Trim(),
                item.SourceName?.Trim() ?? string.Empty))
            .ToList();
    }

    private static RecheckOpenQuestionsResponse BuildFallbackOpenQuestionResponse(IReadOnlyList<string> questions)
    {
        return new RecheckOpenQuestionsResponse(
            [],
            questions.Select(question => new OpenQuestionResolutionResponse(
                question,
                "unresolved",
                "The model response could not be parsed, so the question was kept open.",
                string.Empty)).ToList());
    }

    private sealed record OpenQuestionRecheckModelResponse(
        IReadOnlyList<OpenQuestionResolutionModel>? Resolved,
        IReadOnlyList<OpenQuestionResolutionModel>? Unresolved);

    private sealed record OpenQuestionResolutionModel(
        string Question,
        string Status,
        string EvidenceNote,
        string? SourceName);

    private static TrustBreakdownItemResponse BuildIdentityTrustItem(
        SupplierMatchCandidate? confirmedIdentity,
        int proposedIdentityCount)
    {
        if (confirmedIdentity is not null)
        {
            return new TrustBreakdownItemResponse(
                "Identity",
                Math.Clamp(confirmedIdentity.ConfidenceScore, 70, 95),
                "Confirmed",
                $"Saved identity: {FormatIdentityName(confirmedIdentity.CandidateName)}");
        }

        if (proposedIdentityCount > 0)
        {
            return new TrustBreakdownItemResponse(
                "Identity",
                55,
                "Needs decision",
                $"{proposedIdentityCount} proposed identity match{(proposedIdentityCount == 1 ? string.Empty : "es")} waiting for review.");
        }

        return new TrustBreakdownItemResponse(
            "Identity",
            20,
            "Missing",
            "No confirmed or proposed legal identity is saved.");
    }

    private static TrustBreakdownItemResponse BuildEvidenceTrustItem(int reachableSources, int failedSources)
    {
        var score = Math.Clamp(20 + reachableSources * 18 - failedSources * 8, 10, 90);
        var status = reachableSources switch
        {
            >= 4 => "Strong",
            >= 2 => "Usable",
            1 => "Thin",
            _ => "Missing"
        };
        var explanation = reachableSources == 0
            ? "No reachable public evidence source is saved."
            : $"{reachableSources} reachable source{(reachableSources == 1 ? string.Empty : "s")} and {failedSources} blocked or failed source{(failedSources == 1 ? string.Empty : "s")}.";

        return new TrustBreakdownItemResponse("Public evidence", score, status, explanation);
    }

    private static TrustBreakdownItemResponse BuildCertificationTrustItem(
        int verifiedCertifications,
        int claimedCertifications,
        bool hasCertificationEvidence)
    {
        if (verifiedCertifications > 0)
        {
            return new TrustBreakdownItemResponse(
                "Certifications",
                Math.Clamp(70 + verifiedCertifications * 8, 70, 95),
                "Verified",
                $"{verifiedCertifications} verified certification record{(verifiedCertifications == 1 ? string.Empty : "s")} saved.");
        }

        if (claimedCertifications > 0 || hasCertificationEvidence)
        {
            return new TrustBreakdownItemResponse(
                "Certifications",
                48,
                "Claimed",
                "Certification evidence exists, but no certificate is verified.");
        }

        return new TrustBreakdownItemResponse(
            "Certifications",
            15,
            "Missing",
            "No certification evidence is saved.");
    }

    private static TrustBreakdownItemResponse BuildRegistrationTrustItem(bool hasRegistrationEvidence)
    {
        return hasRegistrationEvidence
            ? new TrustBreakdownItemResponse(
                "Registration",
                78,
                "Found",
                "A reachable public source looks like registration or company information evidence.")
            : new TrustBreakdownItemResponse(
                "Registration",
                25,
                "Unconfirmed",
                "No public registration evidence has been identified.");
    }

    private static TrustBreakdownItemResponse BuildRiskTrustItem(
        RiskAssessment? latestAssessment,
        int reachableSources,
        bool hasConfirmedIdentity)
    {
        if (latestAssessment is null)
        {
            return new TrustBreakdownItemResponse(
                "Risk confidence",
                20,
                "Not assessed",
                "No risk assessment has been generated or saved.");
        }

        if (latestAssessment.RiskLevel == SupplierRiskLevel.Unknown)
        {
            return new TrustBreakdownItemResponse(
                "Risk confidence",
                35,
                "Unresolved",
                "A risk memo exists, but the risk level is still unknown. The stored numeric score is not treated as confidence.");
        }

        var score = 40 + Math.Min(reachableSources, 3) * 10 + (hasConfirmedIdentity ? 15 : 0);
        var status = score >= 75 ? "Grounded" : score >= 55 ? "Draft" : "Provisional";

        return new TrustBreakdownItemResponse(
            "Risk confidence",
            Math.Clamp(score, 35, 90),
            status,
            $"Latest risk decision is {latestAssessment.RiskLevel} at {latestAssessment.Score}/100.");
    }

    private static List<SourceMixItemResponse> BuildSourceMix(Supplier supplier)
    {
        var officialWebsiteCount = supplier.SourceChecks.Count(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsOfficialWebsiteEvidence(source));
        var registrationCount = supplier.SourceChecks.Count(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsRegistrationEvidence(source));
        var claimCount = supplier.SourceChecks.Count(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsCertificationEvidence(source));
        var failedOrBlockedCount = supplier.SourceChecks.Count(source =>
            source.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed);
        var categorizedReachableCount = officialWebsiteCount + registrationCount + claimCount;
        var generalEvidenceCount = Math.Max(0, supplier.SourceChecks.Count(source => source.Status == SourceCheckStatus.Reachable) - categorizedReachableCount);

        return new List<SourceMixItemResponse>
        {
            new("Official website", officialWebsiteCount, officialWebsiteCount > 0 ? "Present" : "Missing"),
            new("Company source", registrationCount, registrationCount > 0 ? "Present" : "Missing"),
            new("Claim source", claimCount, claimCount > 0 ? "Present" : "Missing"),
            new("General public evidence", generalEvidenceCount, generalEvidenceCount > 0 ? "Present" : "Missing"),
            new("Blocked or failed", failedOrBlockedCount, failedOrBlockedCount > 0 ? "Needs review" : "Clear")
        };
    }

    private static List<TimelineItemResponse> BuildTimeline(Supplier supplier)
    {
        var items = new List<TimelineItemResponse>
        {
            new(
                supplier.CreatedAt,
                "Supplier",
                "Supplier created",
                $"{supplier.Name} was added for {supplier.CountryCode} / {supplier.Industry}.",
                "Stored")
        };

        items.AddRange(supplier.AnalysisJobs.Select(job => new TimelineItemResponse(
            job.CompletedAt ?? job.StartedAt ?? job.CreatedAt,
            "Analysis",
            $"{job.Status} analysis",
            job.ErrorMessage ?? job.ProgressMessage,
            job.Status.ToString())));
        items.AddRange(supplier.MatchCandidates.Select(candidate => new TimelineItemResponse(
            candidate.ReviewedAt ?? candidate.CreatedAt,
            "Identity",
            $"{candidate.Status} identity",
            FormatIdentityName(candidate.CandidateName),
            candidate.Status.ToString())));
        items.AddRange(supplier.SourceChecks.Select(source => new TimelineItemResponse(
            source.CheckedAt,
            "Evidence",
            source.SourceName,
            FormatSourceTimelineDescription(source),
            source.Status.ToString())));
        items.AddRange(supplier.Certifications.Select(certification => new TimelineItemResponse(
            certification.CreatedAt,
            "Certification",
            certification.Standard,
            certification.IsVerified ? "Verified certification record saved." : "Unverified certification claim saved.",
            certification.IsVerified ? "Verified" : "Unverified")));
        items.AddRange(supplier.RiskAssessments.Select(assessment => new TimelineItemResponse(
            assessment.CreatedAt,
            "Risk",
            $"{assessment.RiskLevel} risk assessment",
            $"{assessment.Focus} ({FormatRiskScore(assessment)})",
            assessment.RiskLevel.ToString())));

        return items
            .OrderByDescending(item => item.OccurredAt)
            .Take(12)
            .OrderBy(item => item.OccurredAt)
            .ToList();
    }

    private static List<string> BuildStrongestSignals(
        Supplier supplier,
        SupplierMatchCandidate? confirmedIdentity,
        int reachableSources,
        bool hasRegistrationEvidence,
        RiskAssessment? latestAssessment)
    {
        var items = new List<string>();

        if (confirmedIdentity is not null)
        {
            items.Add($"Identity saved as {FormatIdentityName(confirmedIdentity.CandidateName)}.");
        }

        if (!string.IsNullOrWhiteSpace(supplier.WebsiteUrl))
        {
            items.Add($"Supplier website is available at {FormatHost(supplier.WebsiteUrl)}.");
        }

        if (reachableSources > 0)
        {
            items.Add($"{reachableSources} reachable public evidence source{(reachableSources == 1 ? string.Empty : "s")}.");
        }

        if (hasRegistrationEvidence)
        {
            items.Add("Company-information evidence is present.");
        }

        if (latestAssessment is not null)
        {
            items.Add("A research memo is available for open questions.");
        }

        if (items.Count == 0)
        {
            items.Add("Only the basic supplier name, country, and industry are available.");
        }

        return items.Take(5).ToList();
    }

    private static List<string> BuildAnalyticsGaps(
        SupplierMatchCandidate? confirmedIdentity,
        int reachableSources,
        int failedSources,
        bool hasRegistrationEvidence,
        RiskAssessment? latestAssessment)
    {
        var items = new List<string>();

        if (confirmedIdentity is null)
        {
            items.Add("Save one identity match before treating evidence as final.");
        }

        if (reachableSources == 0)
        {
            items.Add("Add at least one reachable public source.");
        }

        if (!hasRegistrationEvidence)
        {
            items.Add("Find registration or company-information evidence.");
        }

        if (latestAssessment is null)
        {
            items.Add("No research memo has been stored.");
        }

        if (failedSources > 0)
        {
            items.Add($"{failedSources} source{(failedSources == 1 ? " is" : "s are")} blocked or failed.");
        }

        return items.Take(6).ToList();
    }

    private static List<string> BuildKnownInformation(
        Supplier supplier,
        SupplierMatchCandidate? confirmedIdentity,
        RiskAssessment? latestAssessment,
        int reachableSourceCount,
        bool hasRegistrationEvidence)
    {
        var items = new List<string>
        {
            $"Supplier record: {supplier.Name} in {supplier.CountryCode} for {supplier.Industry}"
        };

        var website = supplier.WebsiteUrl ?? confirmedIdentity?.WebsiteUrl;
        if (!string.IsNullOrWhiteSpace(website))
        {
            items.Add($"Website available: {FormatHost(website)}");
        }

        items.Add(hasRegistrationEvidence
            ? "Company-information source found in public research"
            : "Company-information source still needs confirmation");
        items.Add(reachableSourceCount > 0
            ? $"{reachableSourceCount} reachable public source{(reachableSourceCount == 1 ? string.Empty : "s")}"
            : "No reachable public evidence source yet");

        foreach (var fact in supplier.SupplierFacts
            .Where(fact => fact.FactType is not SupplierFactType.MissingEvidence and not SupplierFactType.SourceLimitation)
            .OrderByDescending(fact => fact.Confidence)
            .ThenByDescending(fact => fact.CreatedAt)
            .Take(4))
        {
            items.Add($"{FormatFactType(fact.FactType)}: {ShortenText(fact.Value, 160)}");
        }

        if (latestAssessment is not null)
        {
            items.Add("A research memo is available in Open questions.");
        }

        return items;
    }

    private static List<string> BuildMissingInformation(
        int reachableSourceCount,
        int failedSourceCount,
        bool hasRegistrationEvidence,
        int factCount)
    {
        var items = new List<string>();

        if (!hasRegistrationEvidence)
        {
            items.Add("A clear company-information source was not found yet.");
        }

        if (factCount == 0)
        {
            items.Add("No extracted supplier facts are stored yet.");
        }

        if (reachableSourceCount == 0)
        {
            items.Add("No reachable public source has been confirmed.");
        }

        if (failedSourceCount > 0)
        {
            items.Add($"{failedSourceCount} public source{(failedSourceCount == 1 ? " is" : "s are")} blocked or failed.");
        }

        return items;
    }

    private static SupplierReviewNextActionResponse BuildReviewNextAction(
        IReadOnlyList<string> missingInformation,
        RiskAssessment? latestAssessment)
    {
        if (missingInformation.Count > 0)
        {
            return new SupplierReviewNextActionResponse(
                "ReviewSources",
                "Review source gaps",
                "The research found useful material, but some source or fact gaps still need attention.",
                "Open sources",
                "sources");
        }

        if (latestAssessment is null)
        {
            return new SupplierReviewNextActionResponse(
                "ReviewQuestions",
                "Review open questions",
                "Sources exist, but no research memo has been stored for the unresolved questions yet.",
                "Open questions",
                "questions");
        }

        return new SupplierReviewNextActionResponse(
            "PrepareReport",
            "Research briefing ready",
            "The main public research inputs are available. Review sources, open questions, and similar suppliers.",
            "Open briefing",
            "briefing");
    }

    private static string BuildReviewHeadline(
        int reachableSourceCount,
        IReadOnlyList<string> missingInformation,
        RiskAssessment? latestAssessment)
    {
        if (reachableSourceCount == 0)
        {
            return "Research has not found usable sources yet";
        }

        if (missingInformation.Count > 0)
        {
            return "Useful sources found, open questions remain";
        }

        return latestAssessment is null
            ? "Sources found, memo pending"
            : "Supplier research briefing is ready";
    }

    private static bool IsRegistrationEvidence(SourceCheck sourceCheck)
    {
        return sourceCheck.SourceName.Contains("registry", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.SourceName.Contains("company information", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Url.Contains("company-information", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Url.Contains("opencorporates", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCertificationEvidence(SourceCheck sourceCheck)
    {
        return sourceCheck.SourceName.Contains("cert", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Notes.Contains("ISO ", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Notes.Contains("certificate", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.Url.Contains("cert", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOfficialWebsiteEvidence(SourceCheck sourceCheck)
    {
        return sourceCheck.SourceName.Contains("supplier website", StringComparison.OrdinalIgnoreCase) ||
            sourceCheck.SourceName.Contains("website research", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatSourceTimelineDescription(SourceCheck sourceCheck)
    {
        var category = IsRegistrationEvidence(sourceCheck)
            ? "registration evidence"
            : IsCertificationEvidence(sourceCheck)
                ? "certification evidence"
                : IsOfficialWebsiteEvidence(sourceCheck)
                    ? "official website evidence"
                    : "public evidence";

        return $"{formatSourceStatus(sourceCheck.Status)} {category}: {FormatHost(sourceCheck.Url)}";

        static string formatSourceStatus(SourceCheckStatus status)
        {
            return status switch
            {
                SourceCheckStatus.Reachable => "Reachable",
                SourceCheckStatus.Blocked => "Blocked",
                SourceCheckStatus.Failed => "Failed",
                _ => "Unchecked"
            };
        }
    }

    private static string FormatRiskScore(RiskAssessment assessment)
    {
        return assessment.RiskLevel == SupplierRiskLevel.Unknown
            ? "not scored"
            : $"{assessment.Score}/100";
    }

    private static string FormatIdentityName(string value)
    {
        var normalized = value
            .Replace("**", string.Empty, StringComparison.Ordinal)
            .Trim();
        var companyNumberIndex = normalized.IndexOf(" (company number", StringComparison.OrdinalIgnoreCase);
        if (companyNumberIndex > 0)
        {
            return normalized[..companyNumberIndex].Trim();
        }

        foreach (var marker in new[] { " is ", " operates ", " appears ", " runs ", " specializes ", " specialises " })
        {
            var markerIndex = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (markerIndex > 0 && markerIndex <= 80)
            {
                return normalized[..markerIndex].Trim();
            }
        }

        return normalized.Length <= 90 ? normalized : normalized[..87].Trim() + "...";
    }

    private static string FormatHost(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host
            : url;
    }

    private static string FormatFactType(SupplierFactType factType)
    {
        return factType.ToString() switch
        {
            "ProductOrService" => "Product or service",
            "OperationalFootprint" => "Operational footprint",
            "LegalIdentity" => "Company identity clue",
            "RiskSignal" => "Research signal",
            "CertificationClaim" => "Claim mentioned in source",
            var value => value
        };
    }

    private static string ShortenText(string value, int maxLength)
    {
        var normalized = string.Join(' ', value.Split(' ', StringSplitOptions.RemoveEmptyEntries));

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..Math.Max(0, maxLength - 3)].Trim() + "...";
    }

    private static string BuildRiskAssessmentSystemPrompt()
    {
        return """
            You are a supplier-intelligence analyst.
            Use only the evidence provided by the application.
            Do not invent certifications, sources, facts, or risk signals.
            If evidence is missing, name only supplier evidence that a user can add.
            Internal calculation fields are not missing supplier evidence.
            Use supplierFacts as the primary trusted fact list.
            Use companySummary to explain what kind of company this appears to be.
            Prefer precise product, service, location, market, website, and source facts over generic confidence commentary.
            Write short sentences. Avoid long paragraphs. Each bullet should be one concrete fact.
            If a location or address appears in supplierFacts, companySummary, or sourceChecks, include it under Locations / markets.
            Return markdown with exactly these sections:
            ## Company profile
            Explain in 1 or 2 short sentences what the supplier appears to do.
            ## Products and services
            List up to 5 concrete product or service facts.
            ## Locations and markets
            List up to 5 location, address, country, shipping, export, or market facts.
            ## Important source findings
            List up to 5 useful facts from sourceChecks or supplierFacts.
            ## Open questions
            List only unclear facts that matter for quickly understanding the supplier.
            Do not list JSON field names as gaps.
            """;
    }

    private static string BuildRiskAssessmentUserPrompt(
        string promptFocus,
        string evidenceSnapshotJson)
    {
        return $"""
            Focus:
            {promptFocus}

            Task:
            Create one supplier risk assessment for the selected supplier.
            Do not return JSON.
            Do not mention internal prompt instructions.

            Evidence JSON:
            {evidenceSnapshotJson}
            """;
    }

    private static string NormalizePromptFocus(string? focus)
    {
        return string.IsNullOrWhiteSpace(focus)
            ? "Create a general supplier risk assessment."
            : focus.Trim();
    }
}
