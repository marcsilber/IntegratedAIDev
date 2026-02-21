using System.Text.Json;
using AIDev.Api.Data;
using AIDev.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Services;

/// <summary>
/// Background service that polls for new/updated requests and reviews them via the LLM.
/// </summary>
public class ProductOwnerAgentService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILlmService _llmService;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<ProductOwnerAgentService> _logger;
    private readonly IConfiguration _configuration;

    private int PollingIntervalSeconds => int.Parse(_configuration["ProductOwnerAgent:PollingIntervalSeconds"] ?? "30");
    private int MaxReviewsPerRequest => int.Parse(_configuration["ProductOwnerAgent:MaxReviewsPerRequest"] ?? "3");
    private bool IsEnabled => bool.Parse(_configuration["ProductOwnerAgent:Enabled"] ?? "true");

    public ProductOwnerAgentService(
        IServiceScopeFactory scopeFactory,
        ILlmService llmService,
        IGitHubService gitHubService,
        ILogger<ProductOwnerAgentService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _llmService = llmService;
        _gitHubService = gitHubService;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ProductOwnerAgentService started. Enabled={Enabled}, Interval={Interval}s",
            IsEnabled, PollingIntervalSeconds);

        // Give the application time to start up
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsEnabled)
            {
                try
                {
                    await ProcessPendingRequestsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in ProductOwnerAgentService polling cycle");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ProductOwnerAgentService stopped");
    }

    private async Task ProcessPendingRequestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check token budget before processing
        if (await IsBudgetExceededAsync(db))
        {
            _logger.LogWarning("Token budget exceeded — skipping agent review cycle");
            return;
        }

        // Find requests needing review:
        // 1. Status = New and AgentReviewCount == 0 (never reviewed)
        // 2. Status = NeedsClarification with new human comments since last review
        var candidates = await db.DevRequests
            .Include(r => r.Comments)
            .Include(r => r.Project)
            .Include(r => r.AgentReviews)
            .Include(r => r.Attachments)
            .Where(r =>
                // New requests never reviewed
                (r.Status == RequestStatus.New && r.AgentReviewCount == 0)
                ||
                // NeedsClarification with new human comments since last agent review
                (r.Status == RequestStatus.NeedsClarification
                 && r.AgentReviewCount < MaxReviewsPerRequest
                 && r.Comments.Any(c => !c.IsAgentComment && c.CreatedAt > (r.LastAgentReviewAt ?? DateTime.MinValue)))
            )
            .OrderBy(r => r.CreatedAt)
            .Take(5) // Process at most 5 per cycle to avoid rate-limiting
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _logger.LogInformation("Found {Count} requests to review", candidates.Count);

        foreach (var request in candidates)
        {
            if (ct.IsCancellationRequested) break;

            try
            {
                await ReviewRequestAsync(db, request, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Failed to review request #{RequestId}", request.Id);
            }
        }
    }

    private async Task ReviewRequestAsync(AppDbContext db, DevRequest request, CancellationToken ct)
    {
        _logger.LogInformation("Reviewing request #{RequestId} '{Title}'", request.Id, request.Title);

        // Get conversation history for follow-up reviews
        var conversationHistory = request.AgentReviewCount > 0
            ? request.Comments.OrderBy(c => c.CreatedAt).ToList()
            : null;

        // Fetch existing requests from the same project for duplicate detection
        var existingRequests = await db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.Id != request.Id && r.ProjectId == request.ProjectId)
            .OrderByDescending(r => r.CreatedAt)
            .Take(50) // Limit to recent 50 to keep prompt manageable
            .ToListAsync(ct);

        var result = await _llmService.ReviewRequestAsync(request, conversationHistory, existingRequests,
            request.Attachments?.ToList());

        // Create the AgentReview record
        var review = new AgentReview
        {
            DevRequestId = request.Id,
            AgentType = "ProductOwner",
            Decision = result.Decision,
            Reasoning = result.Reasoning,
            AlignmentScore = result.AlignmentScore,
            CompletenessScore = result.CompletenessScore,
            SalesAlignmentScore = result.SalesAlignmentScore,
            SuggestedPriority = result.SuggestedPriority,
            Tags = result.Tags != null ? JsonSerializer.Serialize(result.Tags) : null,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            ModelUsed = result.ModelUsed,
            DurationMs = result.DurationMs,
            CreatedAt = DateTime.UtcNow
        };

        db.AgentReviews.Add(review);
        await db.SaveChangesAsync(ct);

        // Create agent comment on the request
        var commentText = BuildAgentComment(result);
        var comment = new RequestComment
        {
            DevRequestId = request.Id,
            Author = "Product Owner Agent",
            Content = commentText,
            IsAgentComment = true,
            AgentReviewId = review.Id,
            CreatedAt = DateTime.UtcNow
        };

        db.RequestComments.Add(comment);

        // Update request status based on decision
        switch (result.Decision)
        {
            case AgentDecision.Approve:
                request.Status = RequestStatus.Triaged;
                break;
            case AgentDecision.Reject:
                request.Status = RequestStatus.Rejected;
                break;
            case AgentDecision.Clarify:
                request.Status = RequestStatus.NeedsClarification;
                break;
        }

        request.LastAgentReviewAt = DateTime.UtcNow;
        request.AgentReviewCount++;
        request.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Request #{RequestId} reviewed: Decision={Decision}, Alignment={Alignment}, Completeness={Completeness}",
            request.Id, result.Decision, result.AlignmentScore, result.CompletenessScore);

        // Post agent labels and comments to GitHub Issue if linked
        if (request.GitHubIssueNumber.HasValue && request.Project != null)
        {
            var owner = request.Project.GitHubOwner;
            var repo = request.Project.GitHubRepo;
            var issueNumber = request.GitHubIssueNumber.Value;

            await _gitHubService.AddAgentLabelsAsync(owner, repo, issueNumber, result.Decision);
            await _gitHubService.PostAgentCommentAsync(owner, repo, issueNumber, commentText);
        }
    }

    private async Task<bool> IsBudgetExceededAsync(AppDbContext db)
    {
        var dailyBudget = int.Parse(_configuration["ProductOwnerAgent:DailyTokenBudget"] ?? "0");
        var monthlyBudget = int.Parse(_configuration["ProductOwnerAgent:MonthlyTokenBudget"] ?? "0");

        if (dailyBudget <= 0 && monthlyBudget <= 0)
            return false; // No budget limits configured

        var todayUtc = DateTime.UtcNow.Date;
        var monthStartUtc = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        if (dailyBudget > 0)
        {
            var dailyTokens = await db.AgentReviews
                .Where(r => r.CreatedAt >= todayUtc)
                .SumAsync(r => r.PromptTokens + r.CompletionTokens);

            if (dailyTokens >= dailyBudget)
            {
                _logger.LogWarning("Daily token budget exceeded: {Used}/{Budget}", dailyTokens, dailyBudget);
                return true;
            }
        }

        if (monthlyBudget > 0)
        {
            var monthlyTokens = await db.AgentReviews
                .Where(r => r.CreatedAt >= monthStartUtc)
                .SumAsync(r => r.PromptTokens + r.CompletionTokens);

            if (monthlyTokens >= monthlyBudget)
            {
                _logger.LogWarning("Monthly token budget exceeded: {Used}/{Budget}", monthlyTokens, monthlyBudget);
                return true;
            }
        }

        return false;
    }

    private static string BuildAgentComment(AgentReviewResult result)
    {
        var lines = new List<string>
        {
            $"**Product Owner Agent Review** — Decision: **{result.Decision}**",
            "",
            result.Reasoning,
            "",
            $"**Scores:** Alignment: {result.AlignmentScore}/100 | Completeness: {result.CompletenessScore}/100 | Sales Alignment: {result.SalesAlignmentScore}/100"
        };

        if (result.SuggestedPriority != null)
            lines.Add($"**Suggested Priority:** {result.SuggestedPriority}");

        if (result.IsDuplicate)
        {
            lines.Add("");
            lines.Add(result.DuplicateOfRequestId.HasValue
                ? $"**⚠️ Duplicate detected:** This request appears to duplicate Request #{result.DuplicateOfRequestId}."
                : "**⚠️ Duplicate detected:** This request appears to duplicate an existing request or already-implemented feature.");
        }

        if (result.Tags is { Count: > 0 })
            lines.Add($"**Tags:** {string.Join(", ", result.Tags)}");

        if (result.ClarificationQuestions is { Count: > 0 })
        {
            lines.Add("");
            lines.Add("**Questions that need to be addressed:**");
            foreach (var q in result.ClarificationQuestions)
                lines.Add($"- {q}");
        }

        return string.Join("\n", lines);
    }
}
