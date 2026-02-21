using System.Text.Json;
using AIDev.Api.Data;
using AIDev.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Services;

/// <summary>
/// Background service that polls for triaged requests and produces architect solution proposals.
/// Follows the same polling pattern as <see cref="ProductOwnerAgentService"/>.
/// </summary>
public class ArchitectAgentService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IArchitectLlmService _architectLlmService;
    private readonly ICodebaseService _codebaseService;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<ArchitectAgentService> _logger;
    private readonly IConfiguration _configuration;

    private int PollingIntervalSeconds => int.Parse(_configuration["ArchitectAgent:PollingIntervalSeconds"] ?? "60");
    private int MaxReviewsPerRequest => int.Parse(_configuration["ArchitectAgent:MaxReviewsPerRequest"] ?? "3");
    private bool IsEnabled => bool.Parse(_configuration["ArchitectAgent:Enabled"] ?? "true");
    private int BatchSize => int.Parse(_configuration["ArchitectAgent:BatchSize"] ?? "3");

    public ArchitectAgentService(
        IServiceScopeFactory scopeFactory,
        IArchitectLlmService architectLlmService,
        ICodebaseService codebaseService,
        IGitHubService gitHubService,
        ILogger<ArchitectAgentService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _architectLlmService = architectLlmService;
        _codebaseService = codebaseService;
        _gitHubService = gitHubService;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ArchitectAgentService started. Enabled={Enabled}, Interval={Interval}s",
            IsEnabled, PollingIntervalSeconds);

        // Give the application time to start up
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsEnabled)
            {
                try
                {
                    _logger.LogInformation("ArchitectAgentService: starting poll cycle");
                    await ProcessPendingRequestsAsync(stoppingToken);
                    _logger.LogInformation("ArchitectAgentService: poll cycle complete");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in ArchitectAgentService polling cycle");
                }
            }
            else
            {
                _logger.LogInformation("ArchitectAgentService: disabled, skipping cycle");
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ArchitectAgentService stopped");
    }

    private async Task ProcessPendingRequestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check architect-specific token budget
        if (await IsBudgetExceededAsync(db))
        {
            _logger.LogWarning("Architect token budget exceeded — skipping cycle");
            return;
        }

        // Find requests needing architect review:
        // 1. Status = Triaged and ArchitectReviewCount == 0 (never architecture-reviewed)
        // 2. Status = ArchitectReview with new human comments since last review
        var candidates = await db.DevRequests
            .Include(r => r.Comments)
            .Include(r => r.Project)
            .Include(r => r.AgentReviews)
            .Include(r => r.ArchitectReviews)
            .Include(r => r.Attachments)
            .Where(r =>
                // Triaged requests never architect-reviewed
                (r.Status == RequestStatus.Triaged && r.ArchitectReviewCount == 0)
                ||
                // ArchitectReview with new human comments since last architect review
                (r.Status == RequestStatus.ArchitectReview
                 && r.ArchitectReviewCount < MaxReviewsPerRequest
                 && r.Comments.Any(c => !c.IsAgentComment
                     && c.CreatedAt > (r.LastArchitectReviewAt ?? DateTime.MinValue)))
            )
            .OrderBy(r => r.CreatedAt)
            .Take(BatchSize)
            .ToListAsync(ct);

        if (candidates.Count == 0)
        {
            _logger.LogInformation("ArchitectAgentService: no candidates found");
            return;
        }

        _logger.LogInformation("Found {Count} requests for architect review", candidates.Count);

        foreach (var request in candidates)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await AnalyseRequestAsync(db, request, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to architect-review request #{RequestId}", request.Id);
            }
        }
    }

    private async Task AnalyseRequestAsync(AppDbContext db, DevRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Architect analysing request #{RequestId} '{Title}'", request.Id, request.Title);

        // 1. Get the latest PO review for context
        var poReview = request.AgentReviews
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();

        if (poReview == null)
        {
            _logger.LogWarning("No Product Owner review found for request #{RequestId} — skipping", request.Id);
            return;
        }

        // 2. Get repo info from the project
        if (request.Project == null)
        {
            _logger.LogWarning("Request #{RequestId} has no project — skipping", request.Id);
            return;
        }

        var owner = request.Project.GitHubOwner;
        var repo = request.Project.GitHubRepo;

        // 3. Get repository map
        var repoMap = await _codebaseService.GetRepositoryMapAsync(owner, repo);

        // 4. Build file reader delegate
        async Task<Dictionary<string, string>> FileReader(IEnumerable<string> files)
            => await _codebaseService.GetFileContentsAsync(owner, repo, files);

        // 5. Get conversation history for revision context
        var conversationHistory = request.ArchitectReviewCount > 0
            ? request.Comments
                .Where(c => c.ArchitectReviewId != null || (!c.IsAgentComment && c.CreatedAt > (request.LastArchitectReviewAt ?? DateTime.MinValue)))
                .OrderBy(c => c.CreatedAt)
                .ToList()
            : null;

        // 6. Call the architect LLM service
        var result = await _architectLlmService.AnalyseRequestAsync(
            request, poReview, repoMap, FileReader, conversationHistory,
            request.Attachments?.ToList());

        // 7. Create ArchitectReview record
        var solutionJson = JsonSerializer.Serialize(new
        {
            result.ImpactedFiles,
            result.NewFiles,
            result.DataMigration,
            result.BreakingChanges,
            result.DependencyChanges,
            result.Risks,
            result.ImplementationOrder,
            result.TestingNotes,
            result.ArchitecturalNotes,
            result.ClarificationQuestions
        }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        var architectReview = new ArchitectReview
        {
            DevRequestId = request.Id,
            AgentType = "Architect",
            SolutionSummary = result.SolutionSummary,
            Approach = result.Approach,
            SolutionJson = solutionJson,
            EstimatedComplexity = result.EstimatedComplexity,
            EstimatedEffort = result.EstimatedEffort,
            FilesAnalysed = result.FilesRead.Count,
            FilesReadJson = JsonSerializer.Serialize(result.FilesRead),
            Decision = result.ClarificationQuestions is { Count: > 0 }
                ? ArchitectDecision.Pending
                : ArchitectDecision.Pending,
            Step1PromptTokens = result.Step1PromptTokens,
            Step1CompletionTokens = result.Step1CompletionTokens,
            Step2PromptTokens = result.Step2PromptTokens,
            Step2CompletionTokens = result.Step2CompletionTokens,
            ModelUsed = result.ModelUsed,
            TotalDurationMs = result.TotalDurationMs,
            CreatedAt = DateTime.UtcNow
        };

        db.ArchitectReviews.Add(architectReview);
        await db.SaveChangesAsync(ct);

        // 8. Create formatted agent comment
        var commentText = BuildAgentComment(result, architectReview);
        var comment = new RequestComment
        {
            DevRequestId = request.Id,
            Author = "Architect Agent",
            Content = commentText,
            IsAgentComment = true,
            ArchitectReviewId = architectReview.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.RequestComments.Add(comment);

        // 9. Update request status and tracking fields
        request.Status = RequestStatus.ArchitectReview;
        request.LastArchitectReviewAt = DateTime.UtcNow;
        request.ArchitectReviewCount++;
        request.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Request #{RequestId} architect review complete: Complexity={Complexity}, Files={Files}, Duration={Duration}ms",
            request.Id, result.EstimatedComplexity, result.FilesRead.Count, result.TotalDurationMs);

        // 10. Post to GitHub Issue if linked
        if (request.GitHubIssueNumber.HasValue && request.Project != null)
        {
            var issueNumber = request.GitHubIssueNumber.Value;

            await _gitHubService.AddLabelAsync(owner, repo, issueNumber,
                "agent:architect-review", "0e8a16");
            await _gitHubService.PostAgentCommentAsync(owner, repo, issueNumber, commentText);
        }
    }

    private async Task<bool> IsBudgetExceededAsync(AppDbContext db)
    {
        var dailyBudget = int.Parse(_configuration["ArchitectAgent:DailyTokenBudget"] ?? "0");
        var monthlyBudget = int.Parse(_configuration["ArchitectAgent:MonthlyTokenBudget"] ?? "0");

        if (dailyBudget <= 0 && monthlyBudget <= 0)
            return false;

        var todayUtc = DateTime.UtcNow.Date;
        var monthStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        if (dailyBudget > 0)
        {
            var dailyTokens = await db.ArchitectReviews
                .Where(r => r.CreatedAt >= todayUtc)
                .SumAsync(r => r.Step1PromptTokens + r.Step1CompletionTokens
                    + r.Step2PromptTokens + r.Step2CompletionTokens);

            if (dailyTokens >= dailyBudget)
            {
                _logger.LogWarning("Architect daily token budget exceeded: {Used}/{Budget}", dailyTokens, dailyBudget);
                return true;
            }
        }

        if (monthlyBudget > 0)
        {
            var monthlyTokens = await db.ArchitectReviews
                .Where(r => r.CreatedAt >= monthStartUtc)
                .SumAsync(r => r.Step1PromptTokens + r.Step1CompletionTokens
                    + r.Step2PromptTokens + r.Step2CompletionTokens);

            if (monthlyTokens >= monthlyBudget)
            {
                _logger.LogWarning("Architect monthly token budget exceeded: {Used}/{Budget}", monthlyTokens, monthlyBudget);
                return true;
            }
        }

        return false;
    }

    private static string BuildAgentComment(ArchitectSolutionResult result, ArchitectReview review)
    {
        var lines = new List<string>
        {
            "**Architect Agent Solution Proposal**",
            "",
            $"**Summary:** {result.SolutionSummary}",
            "",
            $"**Approach:** {result.Approach}",
            "",
            $"**Complexity:** {result.EstimatedComplexity} | **Effort:** {result.EstimatedEffort}",
            ""
        };

        if (result.ImpactedFiles.Count > 0)
        {
            lines.Add("**Impacted Files:**");
            foreach (var f in result.ImpactedFiles)
                lines.Add($"- `{f.Path}` ({f.Action}, ~{f.EstimatedLinesChanged} lines) — {f.Description}");
            lines.Add("");
        }

        if (result.NewFiles.Count > 0)
        {
            lines.Add("**New Files:**");
            foreach (var f in result.NewFiles)
                lines.Add($"- `{f.Path}` (~{f.EstimatedLines} lines) — {f.Description}");
            lines.Add("");
        }

        if (result.DataMigration.Required)
        {
            lines.Add($"**Data Migration Required:** {result.DataMigration.Description}");
            foreach (var step in result.DataMigration.Steps)
                lines.Add($"  - {step}");
            lines.Add("");
        }

        if (result.BreakingChanges.Count > 0)
        {
            lines.Add("**⚠️ Breaking Changes:**");
            foreach (var bc in result.BreakingChanges)
                lines.Add($"- {bc}");
            lines.Add("");
        }

        if (result.Risks.Count > 0)
        {
            lines.Add("**Risks:**");
            foreach (var r in result.Risks)
                lines.Add($"- [{r.Severity}] {r.Description} — Mitigation: {r.Mitigation}");
            lines.Add("");
        }

        if (result.ImplementationOrder.Count > 0)
        {
            lines.Add("**Implementation Order:**");
            foreach (var step in result.ImplementationOrder)
                lines.Add($"  {step}");
            lines.Add("");
        }

        if (result.ClarificationQuestions is { Count: > 0 })
        {
            lines.Add("**❓ Clarification Questions:**");
            foreach (var q in result.ClarificationQuestions)
                lines.Add($"- {q}");
            lines.Add("");
        }

        lines.Add($"---");
        lines.Add($"*Files analysed: {review.FilesAnalysed} | Tokens: {review.Step1PromptTokens + review.Step1CompletionTokens + review.Step2PromptTokens + review.Step2CompletionTokens} | Model: {review.ModelUsed} | Duration: {review.TotalDurationMs}ms | Review #{review.Id}*");

        return string.Join("\n", lines);
    }
}
