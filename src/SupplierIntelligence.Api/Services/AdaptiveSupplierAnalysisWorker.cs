using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SupplierIntelligence.Api.Data;
using SupplierIntelligence.Api.Models;
using SupplierIntelligence.Api.Options;

namespace SupplierIntelligence.Api.Services;

public sealed class AdaptiveSupplierAnalysisWorker(
    IServiceScopeFactory scopeFactory,
    ISupplierAnalysisQueue queue,
    ILogger<AdaptiveSupplierAnalysisWorker> logger) : BackgroundService
{
    private static readonly Regex UrlRegex = new("https?://[^\\s<>()\\[\\]\"']+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();
        await EnqueueExistingQueuedJobsAsync(stoppingToken);

        await foreach (var analysisJobId in queue.ReadAllAsync(stoppingToken))
        {
            try
            {
                await RunAnalysisJobAsync(analysisJobId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Supplier analysis job {AnalysisJobId} failed before it could update its status.", analysisJobId);
            }
        }
    }

    private async Task EnqueueExistingQueuedJobsAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var jobIds = await db.AnalysisJobs
            .AsNoTracking()
            .Where(j => j.Status == AnalysisJobStatus.Queued)
            .OrderBy(j => j.CreatedAt)
            .Select(j => j.Id)
            .ToListAsync(cancellationToken);

        foreach (var jobId in jobIds)
        {
            await queue.EnqueueAsync(jobId, cancellationToken);
        }
    }

    private async Task RunAnalysisJobAsync(int analysisJobId, CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sourceEvidenceChecker = scope.ServiceProvider.GetRequiredService<ISourceEvidenceChecker>();
        var websiteCertificationDiscovery = scope.ServiceProvider.GetRequiredService<IWebsiteCertificationDiscovery>();
        var websiteResearcher = scope.ServiceProvider.GetRequiredService<IWebsiteResearcher>();
        var sourceDiscoveryPlanner = scope.ServiceProvider.GetRequiredService<ISourceDiscoveryPlanner>();
        var supplierResearchQueryPlanner = scope.ServiceProvider.GetRequiredService<ISupplierResearchQueryPlanner>();
        var researchFactExtractor = scope.ServiceProvider.GetRequiredService<IResearchFactExtractor>();
        var localModel = scope.ServiceProvider.GetRequiredService<ILocalModelClient>();
        var localModelOptions = scope.ServiceProvider.GetRequiredService<IOptions<LocalModelOptions>>().Value;

        var job = await db.AnalysisJobs
            .Include(j => j.Supplier)
                .ThenInclude(s => s.Certifications)
            .Include(j => j.Supplier)
                .ThenInclude(s => s.SourceChecks)
            .Include(j => j.Supplier)
                .ThenInclude(s => s.ResearchSources)
            .Include(j => j.Supplier)
                .ThenInclude(s => s.SupplierFacts)
            .Include(j => j.Supplier)
                .ThenInclude(s => s.RiskAssessments)
            .AsSplitQuery()
            .FirstOrDefaultAsync(j => j.Id == analysisJobId, cancellationToken);

        if (job is null || job.Status == AnalysisJobStatus.Completed)
        {
            return;
        }

        job.Status = AnalysisJobStatus.Running;
        job.StartedAt ??= DateTime.UtcNow;
        job.ProgressMessage = "Starting supplier analysis.";
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await AnalyzeSupplierAsync(
                job,
                sourceEvidenceChecker,
                websiteCertificationDiscovery,
                websiteResearcher,
                sourceDiscoveryPlanner,
                supplierResearchQueryPlanner,
                researchFactExtractor,
                localModel,
                localModelOptions,
                db,
                cancellationToken);

            job.Status = AnalysisJobStatus.Completed;
            job.ProgressMessage = "Analysis completed.";
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            job.Status = AnalysisJobStatus.Failed;
            job.ProgressMessage = "Analysis failed.";
            job.ErrorMessage = exception.Message;
            job.CompletedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(CancellationToken.None);
        }
    }

    private static async Task AnalyzeSupplierAsync(
        AnalysisJob job,
        ISourceEvidenceChecker sourceEvidenceChecker,
        IWebsiteCertificationDiscovery websiteCertificationDiscovery,
        IWebsiteResearcher websiteResearcher,
        ISourceDiscoveryPlanner sourceDiscoveryPlanner,
        ISupplierResearchQueryPlanner supplierResearchQueryPlanner,
        IResearchFactExtractor researchFactExtractor,
        ILocalModelClient localModel,
        LocalModelOptions localModelOptions,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var supplier = job.Supplier;

        if (supplier.WebsiteUrl is not null &&
            Uri.TryCreate(supplier.WebsiteUrl, UriKind.Absolute, out var websiteUrl))
        {
            job.ProgressMessage = "Checking supplier website.";
            await db.SaveChangesAsync(cancellationToken);

            var checkResult = await sourceEvidenceChecker.CheckAsync(websiteUrl, cancellationToken);
            supplier.SourceChecks.Add(new SourceCheck
            {
                SupplierId = supplier.Id,
                SourceName = "Supplier website",
                Url = websiteUrl.ToString(),
                Status = checkResult.Status,
                Notes = checkResult.Notes
            });
            await db.SaveChangesAsync(cancellationToken);

            job.ProgressMessage = "Discovering certification claims from website.";
            await db.SaveChangesAsync(cancellationToken);

            var discoveredCertifications = await websiteCertificationDiscovery.DiscoverAsync(
                websiteUrl,
                cancellationToken);
            AddDiscoveredCertifications(supplier, discoveredCertifications);
            await db.SaveChangesAsync(cancellationToken);

            job.ProgressMessage = "Researching public supplier pages.";
            await db.SaveChangesAsync(cancellationToken);

            var websiteResearch = await websiteResearcher.ResearchAsync(websiteUrl, cancellationToken);
            AddWebsiteResearchSourceChecks(supplier, websiteResearch);
            AddResearchCertificationClaims(supplier, websiteResearch);
            await db.SaveChangesAsync(cancellationToken);
        }

        await RunAiCompanyWebSearchAsync(
            job,
            supplier,
            supplierResearchQueryPlanner,
            localModel,
            localModelOptions,
            db,
            cancellationToken);

        await DiscoverAndCheckAdditionalSourcesAsync(
            job,
            supplier,
            sourceDiscoveryPlanner,
            sourceEvidenceChecker,
            db,
            cancellationToken);

        job.ProgressMessage = "Extracting validated supplier facts.";
        await db.SaveChangesAsync(cancellationToken);

        await researchFactExtractor.RefreshFactsAsync(supplier, db, cancellationToken);

        job.ProgressMessage = "Generating local-model risk assessment.";
        await db.SaveChangesAsync(cancellationToken);

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
        await db.SaveChangesAsync(cancellationToken);
    }

    private static async Task DiscoverAndCheckAdditionalSourcesAsync(
        AnalysisJob job,
        Supplier supplier,
        ISourceDiscoveryPlanner sourceDiscoveryPlanner,
        ISourceEvidenceChecker sourceEvidenceChecker,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        var targets = sourceDiscoveryPlanner
            .PlanTargets(supplier)
            .Where(target => !supplier.SourceChecks.Any(sourceCheck =>
                sourceCheck.Url.Equals(target.Url.ToString(), StringComparison.OrdinalIgnoreCase) ||
                sourceCheck.SourceName.Equals(target.SourceName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        if (targets.Count == 0)
        {
            return;
        }

        job.ProgressMessage = $"Checking {targets.Count} additional evidence source{(targets.Count == 1 ? string.Empty : "s")}.";
        await db.SaveChangesAsync(cancellationToken);

        foreach (var target in targets)
        {
            var checkResult = await sourceEvidenceChecker.CheckAsync(target.Url, cancellationToken);
            supplier.SourceChecks.Add(new SourceCheck
            {
                SupplierId = supplier.Id,
                SourceName = target.SourceName,
                Url = target.Url.ToString(),
                Status = checkResult.Status,
                Notes = $"{target.Reason} {checkResult.Notes}"
            });

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task RunAiCompanyWebSearchAsync(
        AnalysisJob job,
        Supplier supplier,
        ISupplierResearchQueryPlanner supplierResearchQueryPlanner,
        ILocalModelClient localModel,
        LocalModelOptions localModelOptions,
        AppDbContext db,
        CancellationToken cancellationToken)
    {
        if (!localModel.Provider.Equals("OpenRouter", StringComparison.OrdinalIgnoreCase) ||
            !localModelOptions.EnableWebSearch ||
            supplier.SourceChecks.Any(source =>
                source.SourceName.StartsWith("AI web search:", StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        var reachableSources = supplier.SourceChecks.Count(source => source.Status == SourceCheckStatus.Reachable);
        if (!string.IsNullOrWhiteSpace(supplier.WebsiteUrl) && reachableSources >= 8)
        {
            return;
        }

        var queries = supplierResearchQueryPlanner.PlanQueries(supplier);
        if (queries.Count == 0)
        {
            return;
        }

        job.ProgressMessage = $"Running {queries.Count} adaptive supplier web search{(queries.Count == 1 ? string.Empty : "es")}.";
        await db.SaveChangesAsync(cancellationToken);

        foreach (var query in queries)
        {
            try
            {
                var research = await localModel.ChatAsync(
                    localModel.DefaultModel,
                    BuildCompanyWebSearchSystemPrompt(),
                    BuildCompanyWebSearchUserPrompt(supplier, query),
                    cancellationToken);

                supplier.SourceChecks.Add(new SourceCheck
                {
                    SupplierId = supplier.Id,
                    SourceName = $"AI web search: {query.Label}",
                    Url = ExtractPrimarySourceUrl(research),
                    Status = IsUsefulAiResearch(research) ? SourceCheckStatus.Reachable : SourceCheckStatus.Failed,
                    Notes = BuildAiResearchNotes(query, research)
                });

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (LocalModelException exception)
            {
                supplier.SourceChecks.Add(new SourceCheck
                {
                    SupplierId = supplier.Id,
                    SourceName = $"AI web search: {query.Label}",
                    Url = string.Empty,
                    Status = SourceCheckStatus.Failed,
                    Notes = $"{query.Goal} Query: {query.Query}. {exception.Message}"
                });

                await db.SaveChangesAsync(cancellationToken);
            }
        }
    }

    private static void AddDiscoveredCertifications(
        Supplier supplier,
        IReadOnlyList<DiscoveredCertification> discoveredCertifications)
    {
        foreach (var discovered in discoveredCertifications)
        {
            var alreadyExists = supplier.Certifications.Any(certification =>
                certification.Standard.Equals(discovered.Standard, StringComparison.OrdinalIgnoreCase) &&
                certification.CertificateNumber.Equals(discovered.CertificateNumber, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                continue;
            }

            supplier.Certifications.Add(new Certification
            {
                SupplierId = supplier.Id,
                Standard = discovered.Standard,
                CertificateNumber = discovered.CertificateNumber,
                Issuer = discovered.Issuer,
                ValidUntil = discovered.ValidUntil,
                IsVerified = false,
                VerificationNotes = discovered.Notes
            });
        }
    }

    private static void AddWebsiteResearchSourceChecks(
        Supplier supplier,
        WebsiteResearchResult research)
    {
        foreach (var page in research.Pages)
        {
            var alreadyExists = supplier.SourceChecks.Any(sourceCheck =>
                sourceCheck.Url.Equals(page.Url, StringComparison.OrdinalIgnoreCase) &&
                sourceCheck.SourceName.StartsWith("Website research", StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                continue;
            }

            supplier.SourceChecks.Add(new SourceCheck
            {
                SupplierId = supplier.Id,
                SourceName = $"Website research: {page.PageType}",
                Url = page.Url,
                Status = SourceCheckStatus.Reachable,
                Notes = BuildResearchNotes(page)
            });
        }
    }

    private static void AddResearchCertificationClaims(
        Supplier supplier,
        WebsiteResearchResult research)
    {
        var standards = research.EvidenceTerms
            .Where(IsCertificationTerm)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var standard in standards)
        {
            var alreadyExists = supplier.Certifications.Any(certification =>
                certification.Standard.Equals(standard, StringComparison.OrdinalIgnoreCase));

            if (alreadyExists)
            {
                continue;
            }

            supplier.Certifications.Add(new Certification
            {
                SupplierId = supplier.Id,
                Standard = standard,
                CertificateNumber = $"WEBSITE-CLAIM-{standard.Replace(" ", "-", StringComparison.OrdinalIgnoreCase)}",
                Issuer = "Supplier website claim",
                ValidUntil = null,
                IsVerified = false,
                VerificationNotes = "Certification term appeared in researched supplier website content. This is not registry-verified yet."
            });
        }
    }

    private static string BuildResearchNotes(WebsiteResearchPage page)
    {
        var terms = page.MatchedTerms.Count == 0
            ? "No certification or compliance terms found."
            : $"Matched terms: {string.Join(", ", page.MatchedTerms)}.";

        var notes = $"Title: {page.Title}. Description: {page.Description}. {terms} Text: {page.TextSnippet}";
        return notes.Length <= 2200
            ? notes
            : notes[..2197] + "...";
    }

    private static bool IsCertificationTerm(string term)
    {
        return term.Equals("ISO 9001", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("ISO 14001", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("ISO 45001", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("ISO 27001", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("ISO 50001", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("ISO 13485", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("IATF 16949", StringComparison.OrdinalIgnoreCase) ||
            term.Equals("AS9100", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCompanyWebSearchSystemPrompt()
    {
        return """
            You are a supplier research assistant with web search.
            Use web search for the focused supplier research task.
            Do not rely on Wikipedia or Wikidata.
            Prefer source types that fit the supplier: manufacturer directories, marketplaces, official pages, local company profiles, customs/trade databases, certification pages, import/export directories, country-specific business registries, and industry portals.
            If several companies have similar names, compare them and say which one best matches the requested country and industry.
            Return concise evidence only. Do not mention browser navigation, menus, or search mechanics.
            Include source names and URLs in plain text when available.
            Do not invent certifications; mark them as claims unless the source is a certification registry or certificate document.
            Do not fill gaps from memory. If a source does not prove a fact, say the fact is not confirmed.
            Use this exact structure:
            Company identity:
            Products and services:
            Certification and quality evidence:
            Location or registry evidence:
            Source URLs:
            """;
    }

    private static string BuildCompanyWebSearchUserPrompt(
        Supplier supplier,
        SupplierResearchQuery query)
    {
        var website = string.IsNullOrWhiteSpace(supplier.WebsiteUrl)
            ? "not provided"
            : supplier.WebsiteUrl;
        var registryNumber = string.IsNullOrWhiteSpace(supplier.RegistryNumber)
            ? "not provided"
            : supplier.RegistryNumber;
        var vatNumber = string.IsNullOrWhiteSpace(supplier.VatNumber)
            ? "not provided"
            : supplier.VatNumber;
        var certificationHints = string.IsNullOrWhiteSpace(supplier.CertificationHints)
            ? "not provided"
            : supplier.CertificationHints;

        return $"""
            Supplier search task:
            Name: {supplier.Name}
            Country: {supplier.CountryCode}
            Industry: {supplier.Industry}
            Website: {website}
            Registry number: {registryNumber}
            VAT number: {vatNumber}
            Certification hints: {certificationHints}

            Focused research goal:
            {query.Goal}

            Suggested search query:
            {query.Query}

            Find:
            - likely company identity and what it sells or manufactures
            - likely website or public profile pages
            - product/service categories
            - location/country evidence
            - certification or quality-system claims, only if sources mention them
            - source URLs used

            Keep output under 350 words.
            """;
    }

    private static bool IsUsefulAiResearch(string research)
    {
        var normalized = research.Trim();
        return normalized.Length >= 120 &&
            !normalized.Contains("could not find", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("no relevant", StringComparison.OrdinalIgnoreCase) &&
            !normalized.Contains("not enough information", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildAiResearchNotes(
        SupplierResearchQuery query,
        string research)
    {
        return TrimForSourceNotes(
            $"Research goal: {query.Goal} Query: {query.Query}. Text: {research}");
    }

    private static string ExtractPrimarySourceUrl(string research)
    {
        return UrlRegex
            .Matches(research)
            .Select(match => match.Value.TrimEnd('.', ',', ')', ']', '"', '\''))
            .Where(url => !url.Contains("openrouter.ai", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(url => Uri.TryCreate(url, UriKind.Absolute, out _)) ??
            "https://openrouter.ai/search";
    }

    private static string TrimForSourceNotes(string value)
    {
        var normalized = value.ReplaceLineEndings(" ").Trim();
        return normalized.Length <= 2200
            ? normalized
            : normalized[..2197] + "...";
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

}
