using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using SupplierIntelligence.Api.Contracts;
using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Models;
using SupplierIntelligence.Api.Services;

namespace SupplierIntelligence.Api.Endpoints;

public static class SupplierEndpoints
{
    public static IEndpointRouteBuilder MapSupplierEndpoints(this IEndpointRouteBuilder app)
    {
        var suppliers = app.MapGroup("/api/suppliers")
            .WithTags("Suppliers");

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
        var verifiedCertificationCount = supplier.Certifications.Count(certification => certification.IsVerified);
        var hasRegistrationEvidence = supplier.SourceChecks.Any(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsRegistrationEvidence(source));
        var hasCertificationEvidence = supplier.SourceChecks.Any(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsCertificationEvidence(source)) || verifiedCertificationCount > 0;
        var missingInformation = BuildMissingInformation(
            confirmedIdentity,
            reachableSourceCount,
            verifiedCertificationCount,
            hasRegistrationEvidence,
            hasCertificationEvidence);
        var knownInformation = BuildKnownInformation(
            supplier,
            confirmedIdentity,
            latestAssessment,
            reachableSourceCount,
            verifiedCertificationCount,
            hasRegistrationEvidence,
            hasCertificationEvidence);
        var trustSignals = new SupplierTrustSignalsResponse(
            confirmedIdentity is null ? "Needs review" : "Confirmed",
            reachableSourceCount == 0 ? "Missing" : reachableSourceCount >= 3 ? "Good" : "Partial",
            verifiedCertificationCount > 0 ? "Verified" : hasCertificationEvidence ? "Claimed" : "Missing",
            latestAssessment is null ? "Not assessed" : latestAssessment.RiskLevel.ToString());
        var nextAction = BuildReviewNextAction(
            confirmedIdentity,
            missingInformation,
            latestAssessment);

        return new SupplierReviewSummaryResponse(
            supplier.Id,
            supplier.Name,
            BuildReviewHeadline(confirmedIdentity, missingInformation, latestAssessment),
            nextAction,
            knownInformation,
            missingInformation,
            trustSignals);
    }

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
            verifiedCertifications,
            hasRegistrationEvidence,
            latestAssessment);
        var weakestGaps = BuildAnalyticsGaps(
            confirmedIdentity,
            reachableSources,
            failedSources,
            verifiedCertifications,
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
        var certificationCount = supplier.SourceChecks.Count(source =>
            source.Status == SourceCheckStatus.Reachable &&
            IsCertificationEvidence(source));
        var failedOrBlockedCount = supplier.SourceChecks.Count(source =>
            source.Status is SourceCheckStatus.Blocked or SourceCheckStatus.Failed);
        var categorizedReachableCount = officialWebsiteCount + registrationCount + certificationCount;
        var generalEvidenceCount = Math.Max(0, supplier.SourceChecks.Count(source => source.Status == SourceCheckStatus.Reachable) - categorizedReachableCount);

        return new List<SourceMixItemResponse>
        {
            new("Official website", officialWebsiteCount, officialWebsiteCount > 0 ? "Present" : "Missing"),
            new("Registration source", registrationCount, registrationCount > 0 ? "Present" : "Missing"),
            new("Certification source", certificationCount, certificationCount > 0 ? "Present" : "Missing"),
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
        int verifiedCertifications,
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

        if (verifiedCertifications > 0)
        {
            items.Add($"{verifiedCertifications} verified certification record{(verifiedCertifications == 1 ? string.Empty : "s")}.");
        }

        if (hasRegistrationEvidence)
        {
            items.Add("Registration-style evidence is present.");
        }

        if (latestAssessment is not null)
        {
            items.Add($"Latest risk memo is {latestAssessment.RiskLevel} with {FormatRiskScore(latestAssessment)}.");
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
        int verifiedCertifications,
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

        if (verifiedCertifications == 0)
        {
            items.Add("No certification has been verified.");
        }

        if (latestAssessment is null)
        {
            items.Add("No risk memo has been stored.");
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
        int verifiedCertificationCount,
        bool hasRegistrationEvidence,
        bool hasCertificationEvidence)
    {
        var items = new List<string>
        {
            confirmedIdentity is null
                ? $"Supplier record: {supplier.Name} in {supplier.CountryCode} for {supplier.Industry}"
                : $"Confirmed identity: {FormatIdentityName(confirmedIdentity.CandidateName)}"
        };

        var website = supplier.WebsiteUrl ?? confirmedIdentity?.WebsiteUrl;
        if (!string.IsNullOrWhiteSpace(website))
        {
            items.Add($"Website available: {FormatHost(website)}");
        }

        items.Add(hasRegistrationEvidence
            ? "Legal registration evidence found in public sources"
            : "Legal registration evidence still needs confirmation");
        items.Add(verifiedCertificationCount > 0
            ? $"Verified certifications saved: {verifiedCertificationCount}"
            : hasCertificationEvidence
                ? "Certification evidence found but not verified"
                : "No verified certification saved yet");
        items.Add(reachableSourceCount > 0
            ? $"{reachableSourceCount} reachable public evidence source{(reachableSourceCount == 1 ? string.Empty : "s")}"
            : "No reachable public evidence source yet");

        if (latestAssessment is not null)
        {
            items.Add($"Current risk view: {latestAssessment.RiskLevel}");
        }

        return items;
    }

    private static List<string> BuildMissingInformation(
        SupplierMatchCandidate? confirmedIdentity,
        int reachableSourceCount,
        int verifiedCertificationCount,
        bool hasRegistrationEvidence,
        bool hasCertificationEvidence)
    {
        var items = new List<string>();

        if (confirmedIdentity is null)
        {
            items.Add("Supplier identity is not confirmed.");
        }

        if (!hasRegistrationEvidence)
        {
            items.Add("Legal registration evidence still needs confirmation.");
        }

        if (verifiedCertificationCount == 0)
        {
            items.Add(hasCertificationEvidence
                ? "Certification evidence exists but no certification is verified."
                : "No verified certification evidence found.");
        }

        if (reachableSourceCount == 0)
        {
            items.Add("No reachable public evidence source confirmed.");
        }

        return items;
    }

    private static SupplierReviewNextActionResponse BuildReviewNextAction(
        SupplierMatchCandidate? confirmedIdentity,
        IReadOnlyList<string> missingInformation,
        RiskAssessment? latestAssessment)
    {
        if (confirmedIdentity is null)
        {
            return new SupplierReviewNextActionResponse(
                "ConfirmIdentity",
                "Confirm supplier identity",
                "Review possible legal entities and save the supplier identity before trusting evidence or reports.",
                "Review matches",
                "identity");
        }

        if (missingInformation.Count > 0)
        {
            return new SupplierReviewNextActionResponse(
                "ReviewEvidence",
                "Close evidence gaps",
                "Important verification evidence is still missing or unverified.",
                "Review evidence",
                "evidence");
        }

        if (latestAssessment is null)
        {
            return new SupplierReviewNextActionResponse(
                "ReviewRisk",
                "Review risk",
                "Evidence exists, but no stored risk decision is available yet.",
                "Open risk",
                "risk");
        }

        return new SupplierReviewNextActionResponse(
            "PrepareReport",
            "Prepare report",
            "The main review inputs are available. Check recommendations before exporting.",
            "Open report",
            "report");
    }

    private static string BuildReviewHeadline(
        SupplierMatchCandidate? confirmedIdentity,
        IReadOnlyList<string> missingInformation,
        RiskAssessment? latestAssessment)
    {
        if (confirmedIdentity is null)
        {
            return "Identity needs review";
        }

        if (missingInformation.Count > 0)
        {
            return "Identity confirmed, evidence gaps remain";
        }

        return latestAssessment is null
            ? "Identity confirmed, risk pending"
            : "Supplier review is ready";
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

    private static string BuildRiskAssessmentSystemPrompt()
    {
        return """
            You are a supplier-intelligence analyst.
            Use only the evidence provided by the application.
            Do not invent certifications, sources, facts, or risk signals.
            If evidence is missing, name only supplier evidence that a user can add.
            Internal calculation fields are not missing supplier evidence.
            Use riskDecision.level and riskDecision.score from the evidence JSON.
            Use evidenceQuality.score and evidenceQuality.band to explain confidence.
            Use supplierFacts as the primary trusted fact list.
            Use companySummary to explain what kind of company this appears to be.
            Prefer externalHighlights over ownWebsiteHighlights when describing the company.
            Use supplierProfile, supplierFacts, companySummary, expectedEvidence, and recommendedNextChecks when evidence is sparse.
            Use sourceChecks only as supporting technical evidence or to explain blocked and failed checks.
            For the Gaps or risks section, use missingEvidenceSuggestions.
            Keep the answer short enough for an operational UI.
            Return markdown with exactly these sections:
            ## Company profile
            Explain in 1 or 2 sentences what the company appears to do, using only evidence from companySummary and sourceChecks.
            ## Risk decision
            State the calculated risk level and score in one sentence, and say it is a draft if evidence is sparse.
            ## Evidence used
            Mention certifications and source checks separately. Cite only categories and names from the JSON.
            ## Gaps or risks
            List missing certifications, missing registry evidence, expired certifications, blocked sources, or failed sources.
            Do not list JSON field names as gaps.
            ## Recommended next checks
            Give 2 or 3 concrete next checks, prioritizing certification and registry evidence before broad research.
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
