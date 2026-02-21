using AIDev.Api.Data;
using AIDev.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIDev.Api.Services;

/// <summary>
/// Background service that automatically reviews PRs opened by Copilot.
/// Checks the diff against the approved architect solution, security criteria,
/// and coding standards. Auto-merges approved PRs.
/// </summary>
public class CodeReviewAgentService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly ICodeReviewLlmService _codeReviewLlmService;
    private readonly ILogger<CodeReviewAgentService> _logger;
    private readonly IConfiguration _configuration;

    private int PollIntervalSeconds => int.Parse(_configuration["CodeReviewAgent:PollingIntervalSeconds"] ?? "90");
    private bool IsEnabled => bool.Parse(_configuration["CodeReviewAgent:Enabled"] ?? "true");
    private bool AutoMerge => bool.Parse(_configuration["CodeReviewAgent:AutoMerge"] ?? "true");
    private int MinQualityScore => int.Parse(_configuration["CodeReviewAgent:MinQualityScore"] ?? "6");

    public CodeReviewAgentService(
        IServiceScopeFactory scopeFactory,
        IGitHubService gitHubService,
        ICodeReviewLlmService codeReviewLlmService,
        ILogger<CodeReviewAgentService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _gitHubService = gitHubService;
        _codeReviewLlmService = codeReviewLlmService;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("CodeReviewAgentService started. Enabled={Enabled}, PollInterval={Interval}s, AutoMerge={AutoMerge}",
            IsEnabled, PollIntervalSeconds, AutoMerge);

        // Give the application time to start up
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsEnabled)
            {
                try
                {
                    await ReviewPendingPrsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in CodeReviewAgentService polling cycle");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("CodeReviewAgentService stopped");
    }

    private async Task ReviewPendingPrsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Find requests with PRs that need review
        var prReadyRequests = await db.DevRequests
            .Include(r => r.Project)
            .Include(r => r.ArchitectReviews)
            .Include(r => r.CodeReviews)
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.CopilotStatus == CopilotImplementationStatus.PrOpened
                     && r.CopilotPrNumber != null)
            .ToListAsync(ct);

        if (prReadyRequests.Count == 0)
        {
            _logger.LogDebug("CodeReview: No PRs awaiting review");
            return;
        }

        _logger.LogInformation("CodeReview: Found {Count} PR(s) awaiting review", prReadyRequests.Count);

        foreach (var request in prReadyRequests)
        {
            // Skip if already reviewed (avoid re-reviewing)
            if (request.CodeReviews.Any(cr => cr.PrNumber == request.CopilotPrNumber))
            {
                _logger.LogDebug("CodeReview: PR #{PrNumber} for request #{Id} already reviewed, skipping",
                    request.CopilotPrNumber, request.Id);
                continue;
            }

            try
            {
                await ReviewSinglePrAsync(request, db, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reviewing PR #{PrNumber} for request #{Id}",
                    request.CopilotPrNumber, request.Id);
            }
        }
    }

    private async Task ReviewSinglePrAsync(DevRequest request, AppDbContext db, CancellationToken ct)
    {
        var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
        var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";
        var prNumber = request.CopilotPrNumber!.Value;

        _logger.LogInformation("CodeReview: Starting review of PR #{PrNumber} for request #{RequestId} '{Title}'",
            prNumber, request.Id, request.Title);

        // 1. Get the approved architect solution
        var architectReview = request.ArchitectReviews
            .Where(ar => ar.Decision == ArchitectDecision.Approved)
            .OrderByDescending(ar => ar.ApprovedAt ?? ar.CreatedAt)
            .FirstOrDefault();

        if (architectReview == null)
        {
            _logger.LogWarning("CodeReview: No approved architect review found for request #{Id}. Using latest review.", request.Id);
            architectReview = request.ArchitectReviews
                .OrderByDescending(ar => ar.CreatedAt)
                .FirstOrDefault();

            if (architectReview == null)
            {
                _logger.LogError("CodeReview: No architect review at all for request #{Id}. Skipping.", request.Id);
                return;
            }
        }

        // 2. Get PR diff
        var diff = await _gitHubService.GetPrDiffAsync(owner, repo, prNumber);
        if (string.IsNullOrWhiteSpace(diff))
        {
            _logger.LogWarning("CodeReview: Could not retrieve diff for PR #{PrNumber}. Skipping.", prNumber);
            return;
        }

        // 3. Get PR stats
        var pr = await _gitHubService.GetPullRequestAsync(owner, repo, prNumber);
        var filesChanged = pr?.ChangedFiles ?? 0;
        var linesAdded = pr?.Additions ?? 0;
        var linesRemoved = pr?.Deletions ?? 0;

        // 4. Call LLM to review
        var result = await _codeReviewLlmService.ReviewPrAsync(
            request, architectReview, diff, filesChanged, linesAdded, linesRemoved);

        _logger.LogInformation(
            "CodeReview: PR #{PrNumber} result — Decision={Decision}, Quality={Score}, Design={Design}, Security={Security}, Standards={Standards}",
            prNumber, result.Decision, result.QualityScore, result.DesignCompliance, result.SecurityPass, result.CodingStandardsPass);

        // 5. Save review to database
        var codeReview = new CodeReview
        {
            DevRequestId = request.Id,
            PrNumber = prNumber,
            Decision = result.Decision,
            Summary = result.Summary,
            DesignCompliance = result.DesignCompliance,
            DesignComplianceNotes = result.DesignComplianceNotes,
            SecurityPass = result.SecurityPass,
            SecurityNotes = result.SecurityNotes,
            CodingStandardsPass = result.CodingStandardsPass,
            CodingStandardsNotes = result.CodingStandardsNotes,
            QualityScore = result.QualityScore,
            FilesChanged = filesChanged,
            LinesAdded = linesAdded,
            LinesRemoved = linesRemoved,
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
            ModelUsed = result.ModelUsed,
            DurationMs = result.DurationMs
        };

        db.CodeReviews.Add(codeReview);
        await db.SaveChangesAsync(ct);

        // 6. Act on the review decision
        if (result.Decision == CodeReviewDecision.Approved && result.QualityScore >= MinQualityScore)
        {
            await HandleApprovedPrAsync(request, prNumber, result, owner, repo, db, ct);
        }
        else
        {
            await HandleChangesRequestedAsync(request, prNumber, result, owner, repo, db, ct);
        }
    }

    private async Task HandleApprovedPrAsync(
        DevRequest request, int prNumber, CodeReviewResult result,
        string owner, string repo, AppDbContext db, CancellationToken ct)
    {
        var reviewBody = FormatApprovalComment(result);

        // If it's a draft PR, mark it ready for review first
        var pr = await _gitHubService.GetPullRequestAsync(owner, repo, prNumber);
        if (pr?.Draft == true)
        {
            _logger.LogInformation("CodeReview: PR #{PrNumber} is a draft — marking ready for review", prNumber);
            await _gitHubService.MarkPrReadyForReviewAsync(owner, repo, prNumber);
            // Small delay for GitHub to process the state change
            await Task.Delay(TimeSpan.FromSeconds(3), ct);
        }

        // Submit approval review
        await _gitHubService.ApprovePullRequestAsync(owner, repo, prNumber, reviewBody);

        // Update labels
        await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value, "copilot:pr-ready");
        await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
            "review:approved", "10b981");

        if (AutoMerge)
        {
            // Set status to ReviewApproved before merging
            request.CopilotStatus = CopilotImplementationStatus.ReviewApproved;
            request.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // Merge the PR
            var commitMessage = $"{request.Title} (#{prNumber})\n\nAuto-merged by Code Review Agent.\nQuality score: {result.QualityScore}/10";
            var merged = await _gitHubService.MergePullRequestAsync(owner, repo, prNumber, commitMessage);

            if (merged)
            {
                request.CopilotStatus = CopilotImplementationStatus.PrMerged;
                request.CopilotCompletedAt = DateTime.UtcNow;
                request.Status = RequestStatus.Done;
                request.DeploymentStatus = DeploymentStatus.Pending;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                // Clean up branch
                if (!string.IsNullOrEmpty(request.CopilotBranchName))
                {
                    var deleted = await _gitHubService.DeleteBranchAsync(owner, repo, request.CopilotBranchName);
                    if (deleted)
                    {
                        request.BranchDeleted = true;
                        await db.SaveChangesAsync(ct);
                    }
                }

                // Update labels
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value, "review:approved");
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value, "agent:approved");
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value, "agent:architect-review");
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value, "agent:approved-solution");
                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "copilot:complete", "10b981");

                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    $"🎉 **Implementation complete!** PR #{prNumber} has been reviewed and auto-merged.\n\n" +
                    $"**Quality Score:** {result.QualityScore}/10 | " +
                    $"**Design Compliance:** ✅ | **Security:** ✅ | **Standards:** ✅\n\n" +
                    $"Total time from trigger to merge: {(request.CopilotCompletedAt - request.CopilotTriggeredAt)?.TotalMinutes:F0} minutes");

                _logger.LogInformation("CodeReview: PR #{PrNumber} auto-merged for request #{RequestId} — Done",
                    prNumber, request.Id);
            }
            else
            {
                _logger.LogWarning("CodeReview: Failed to merge PR #{PrNumber}. It was approved but merge failed.", prNumber);

                // Post a comment explaining the situation
                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    $"✅ **Code Review Agent approved PR #{prNumber}** (quality {result.QualityScore}/10) but auto-merge failed.\n\n" +
                    "Please merge manually.");
            }
        }
        else
        {
            // Auto-merge disabled — just approve and comment
            request.CopilotStatus = CopilotImplementationStatus.ReviewApproved;
            request.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                $"✅ **Code Review Agent approved PR #{prNumber}** (quality {result.QualityScore}/10).\n\n" +
                "Auto-merge is disabled. Please merge manually when ready.");

            _logger.LogInformation("CodeReview: PR #{PrNumber} approved (auto-merge disabled) for request #{RequestId}",
                prNumber, request.Id);
        }
    }

    private async Task HandleChangesRequestedAsync(
        DevRequest request, int prNumber, CodeReviewResult result,
        string owner, string repo, AppDbContext db, CancellationToken ct)
    {
        var reviewBody = FormatChangesRequestedComment(result);

        // Submit changes-requested review on the PR
        await _gitHubService.RequestChangesOnPullRequestAsync(owner, repo, prNumber, reviewBody);

        // Update labels
        await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value, "copilot:pr-ready");
        await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
            "review:changes-requested", "ef4444");

        // Post comment on the issue
        await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
            $"🔍 **Code Review Agent requested changes on PR #{prNumber}**\n\n" +
            $"**Quality Score:** {result.QualityScore}/10\n" +
            $"**Design Compliance:** {(result.DesignCompliance ? "✅" : "❌")} | " +
            $"**Security:** {(result.SecurityPass ? "✅" : "❌")} | " +
            $"**Standards:** {(result.CodingStandardsPass ? "✅" : "❌")}\n\n" +
            $"**Summary:** {result.Summary}\n\n" +
            "Please review the PR feedback and make corrections.");

        _logger.LogInformation("CodeReview: Changes requested on PR #{PrNumber} for request #{RequestId} (score {Score}/10)",
            prNumber, request.Id, result.QualityScore);
    }

    private static string FormatApprovalComment(CodeReviewResult result)
    {
        return $"""
            ## ✅ Code Review Agent — Approved

            **Quality Score:** {result.QualityScore}/10

            ### Design Compliance ✅
            {result.DesignComplianceNotes}

            ### Security ✅
            {result.SecurityNotes}

            ### Coding Standards ✅
            {result.CodingStandardsNotes}

            ---
            *Reviewed by AIDev Code Review Agent ({result.ModelUsed})*
            """;
    }

    private static string FormatChangesRequestedComment(CodeReviewResult result)
    {
        return $"""
            ## 🔍 Code Review Agent — Changes Requested

            **Quality Score:** {result.QualityScore}/10

            {result.Summary}

            ### Design Compliance {(result.DesignCompliance ? "✅" : "❌")}
            {result.DesignComplianceNotes}

            ### Security {(result.SecurityPass ? "✅" : "❌")}
            {result.SecurityNotes}

            ### Coding Standards {(result.CodingStandardsPass ? "✅" : "❌")}
            {result.CodingStandardsNotes}

            ---
            *Reviewed by AIDev Code Review Agent ({result.ModelUsed})*
            """;
    }
}
