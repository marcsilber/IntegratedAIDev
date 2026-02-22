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
    private int MaxReviewsPerPr => int.Parse(_configuration["CodeReviewAgent:MaxReviewsPerPr"] ?? "3");
    private bool IsStagedMode => string.Equals(_configuration["PipelineOrchestrator:DeploymentMode"], "Staged", StringComparison.OrdinalIgnoreCase);

    /// <summary>Tracks (prNumber:headSha) pairs already reviewed to avoid re-reviewing unchanged PRs.</summary>
    private readonly HashSet<string> _reviewedShas = new();

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
            // Check review history for this PR
            var latestReview = request.CodeReviews
                .Where(cr => cr.PrNumber == request.CopilotPrNumber)
                .OrderByDescending(cr => cr.CreatedAt)
                .FirstOrDefault();

            if (latestReview != null)
            {
                // Already approved — skip
                if (latestReview.Decision == CodeReviewDecision.Approved)
                {
                    _logger.LogDebug("CodeReview: PR #{PrNumber} for request #{Id} already approved, skipping",
                        request.CopilotPrNumber, request.Id);
                    continue;
                }

                // Changes requested — allow re-review if new commits and under max review count
                if (latestReview.Decision == CodeReviewDecision.ChangesRequested)
                {
                    var reviewCount = request.CodeReviews.Count(cr => cr.PrNumber == request.CopilotPrNumber);
                    if (reviewCount >= MaxReviewsPerPr)
                    {
                        _logger.LogDebug(
                            "CodeReview: PR #{PrNumber} for request #{Id} reached max review count ({Count}/{Max}), skipping",
                            request.CopilotPrNumber, request.Id, reviewCount, MaxReviewsPerPr);
                        continue;
                    }

                    // Fetch PR to check head SHA — only re-review if Copilot pushed new commits
                    var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
                    var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";
                    var prForCheck = await _gitHubService.GetPullRequestAsync(owner, repo, request.CopilotPrNumber!.Value);
                    var headSha = prForCheck?.Head?.Sha ?? "";
                    var shaKey = $"{request.CopilotPrNumber}:{headSha}";

                    if (string.IsNullOrEmpty(headSha) || _reviewedShas.Contains(shaKey))
                    {
                        _logger.LogDebug(
                            "CodeReview: PR #{PrNumber} for request #{Id} has no new commits since last review, skipping",
                            request.CopilotPrNumber, request.Id);
                        continue;
                    }

                    _logger.LogInformation(
                        "CodeReview: PR #{PrNumber} was previously ChangesRequested but has new commits (SHA: {Sha}). Re-reviewing (review {Count}/{Max}).",
                        request.CopilotPrNumber, headSha[..7], reviewCount + 1, MaxReviewsPerPr);
                }
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
        var headSha = pr?.Head?.Sha ?? "";

        // Track this SHA so we don't re-review the same commit
        if (!string.IsNullOrEmpty(headSha))
            _reviewedShas.Add($"{prNumber}:{headSha}");

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
            await HandleApprovedPrAsync(request, prNumber, result, architectReview, diff, filesChanged, linesAdded, linesRemoved, owner, repo, db, ct);
        }
        else
        {
            await HandleChangesRequestedAsync(request, prNumber, result, owner, repo, db, ct);
        }
    }

    private async Task HandleApprovedPrAsync(
        DevRequest request, int prNumber, CodeReviewResult result,
        ArchitectReview architectReview, string diff, int filesChanged, int linesAdded, int linesRemoved,
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
            // Check if the Copilot workflow run completed successfully.
            // If it was cancelled or failed, do NOT auto-merge — the implementation may be incomplete.
            if (request.CopilotTriggeredAt.HasValue)
            {
                var triggerTime = request.CopilotTriggeredAt.Value;
                var runStatus = await _gitHubService.GetCopilotRunStatusAsync(
                    owner, repo,
                    triggerTime.AddSeconds(-30),
                    triggerTime.AddMinutes(5));

                if (runStatus != null && runStatus.Value.conclusion is "cancelled" or "failure")
                {
                    var (conclusion, runId, headBranch) = runStatus.Value;
                    _logger.LogWarning(
                        "CodeReview: Copilot run #{RunId} for request #{RequestId} has conclusion '{Conclusion}'. Blocking auto-merge.",
                        runId, request.Id, conclusion);

                    request.CopilotStatus = CopilotImplementationStatus.Failed;
                    request.CopilotCompletedAt = DateTime.UtcNow;
                    request.Status = RequestStatus.Approved; // Reset to Approved so it can be re-triggered
                    request.CopilotSessionId = null;         // Clear session so re-trigger is possible
                    request.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                        "review:approved");
                    await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                        "copilot:failed", "ef4444");

                    await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                        $"❌ **Auto-merge blocked.** The Copilot workflow run was **{conclusion}** (Run #{runId}), " +
                        "indicating the implementation may be incomplete.\n\n" +
                        $"Code review passed (quality {result.QualityScore}/10) but the PR will NOT be merged automatically.\n\n" +
                        "**Next steps:** A human can review and manually merge the PR, or re-trigger Copilot from the dashboard.");

                    return;
                }
            }

            // In Staged mode, approve but do NOT merge — wait for human to trigger deploy
            if (IsStagedMode)
            {
                request.CopilotStatus = CopilotImplementationStatus.ReviewApproved;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "deploy:staged", "6366f1");

                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    $"✅ **Code review passed** (quality {result.QualityScore}/10). " +
                    $"PR #{prNumber} is approved and ready to merge.\n\n" +
                    "🔒 **Staged deployment mode** is active — this PR will be merged when a human triggers deployment from the Admin panel.");

                _logger.LogInformation(
                    "CodeReview: PR #{PrNumber} approved in Staged mode for request #{RequestId}. Awaiting manual deploy trigger.",
                    prNumber, request.Id);
                return;
            }

            // Set status to ReviewApproved before merging
            request.CopilotStatus = CopilotImplementationStatus.ReviewApproved;
            request.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            // --- SAFE MERGE: Ensure branch is up-to-date with main before merging ---
            // This prevents a later PR from overwriting an earlier PR's changes.
            if (!string.IsNullOrEmpty(request.CopilotBranchName))
            {
                var behindBy = await _gitHubService.GetBehindByCountAsync(owner, repo, "main", request.CopilotBranchName);
                if (behindBy > 0)
                {
                    _logger.LogInformation(
                        "CodeReview: PR #{PrNumber} branch '{Branch}' is {BehindBy} commit(s) behind main. Updating branch before merge.",
                        prNumber, request.CopilotBranchName, behindBy);

                    var updated = await _gitHubService.UpdatePrBranchAsync(owner, repo, prNumber);
                    if (!updated)
                    {
                        // Branch update failed — likely merge conflicts. Request changes instead of forcing merge.
                        _logger.LogWarning(
                            "CodeReview: PR #{PrNumber} branch update failed (likely merge conflicts). Requesting manual resolution.",
                            prNumber);

                        request.CopilotStatus = CopilotImplementationStatus.PrOpened;
                        request.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);

                        await _gitHubService.RequestChangesOnPullRequestAsync(owner, repo, prNumber,
                            "## ⚠️ Merge Conflict Detected\n\n" +
                            "This PR's branch is behind `main` and could not be automatically updated. " +
                            "Another PR was likely merged first, causing conflicts.\n\n" +
                            "**Please rebase or merge `main` into this branch and resolve conflicts before this PR can be merged.**\n\n" +
                            $"The branch is **{behindBy} commit(s) behind** `main`.\n\n" +
                            "---\n*Detected by AIDev Code Review Agent*");

                        await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                            "merge-conflict", "d93f0b");

                        await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                            $"⚠️ **PR #{prNumber} has merge conflicts** with `main`. Code review passed (quality {result.QualityScore}/10) " +
                            "but the branch needs to be updated before merging. The Copilot agent has been asked to resolve the conflicts.");

                        return;
                    }

                    // Branch updated — wait for GitHub to process the merge commit
                    _logger.LogInformation("CodeReview: PR #{PrNumber} branch updated successfully. Waiting for GitHub to settle.", prNumber);
                    await Task.Delay(TimeSpan.FromSeconds(10), ct);

                    // Re-fetch the diff and re-review after update to ensure no regressions
                    var updatedDiff = await _gitHubService.GetPrDiffAsync(owner, repo, prNumber);
                    if (!string.IsNullOrWhiteSpace(updatedDiff) && updatedDiff != diff)
                    {
                        _logger.LogInformation("CodeReview: PR #{PrNumber} diff changed after branch update. Re-reviewing.", prNumber);
                        var updatedPr = await _gitHubService.GetPullRequestAsync(owner, repo, prNumber);
                        var reReviewResult = await _codeReviewLlmService.ReviewPrAsync(
                            request, architectReview!, updatedDiff,
                            updatedPr?.ChangedFiles ?? filesChanged,
                            updatedPr?.Additions ?? linesAdded,
                            updatedPr?.Deletions ?? linesRemoved);

                        if (reReviewResult.Decision != CodeReviewDecision.Approved || reReviewResult.QualityScore < MinQualityScore)
                        {
                            _logger.LogWarning(
                                "CodeReview: PR #{PrNumber} failed re-review after branch update (Decision={Decision}, Score={Score}). Requesting changes.",
                                prNumber, reReviewResult.Decision, reReviewResult.QualityScore);

                            // Save the re-review
                            var reReview = new CodeReview
                            {
                                DevRequestId = request.Id,
                                PrNumber = prNumber,
                                Decision = reReviewResult.Decision,
                                Summary = "[Post-rebase re-review] " + reReviewResult.Summary,
                                DesignCompliance = reReviewResult.DesignCompliance,
                                DesignComplianceNotes = reReviewResult.DesignComplianceNotes,
                                SecurityPass = reReviewResult.SecurityPass,
                                SecurityNotes = reReviewResult.SecurityNotes,
                                CodingStandardsPass = reReviewResult.CodingStandardsPass,
                                CodingStandardsNotes = reReviewResult.CodingStandardsNotes,
                                QualityScore = reReviewResult.QualityScore,
                                FilesChanged = updatedPr?.ChangedFiles ?? filesChanged,
                                LinesAdded = updatedPr?.Additions ?? linesAdded,
                                LinesRemoved = updatedPr?.Deletions ?? linesRemoved,
                                PromptTokens = reReviewResult.PromptTokens,
                                CompletionTokens = reReviewResult.CompletionTokens,
                                ModelUsed = reReviewResult.ModelUsed,
                                DurationMs = reReviewResult.DurationMs
                            };
                            db.CodeReviews.Add(reReview);

                            request.CopilotStatus = CopilotImplementationStatus.PrOpened;
                            request.UpdatedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(ct);

                            await HandleChangesRequestedAsync(request, prNumber, reReviewResult, owner, repo, db, ct);
                            return;
                        }

                        _logger.LogInformation("CodeReview: PR #{PrNumber} re-review passed after branch update. Proceeding with merge.", prNumber);
                    }
                }
                else if (behindBy == 0)
                {
                    _logger.LogInformation("CodeReview: PR #{PrNumber} branch is up-to-date with main. Safe to merge.", prNumber);
                }
            }

            // Clean up _temp-attachments/ from the PR branch before merging
            // so temporary staging files don't end up in main
            if (!string.IsNullOrEmpty(request.CopilotBranchName))
            {
                var removed = await _gitHubService.RemoveFilesFromBranchAsync(
                    owner, repo, request.CopilotBranchName,
                    "_temp-attachments/",
                    "Clean up temp attachment files before merge");
                if (removed)
                {
                    _logger.LogInformation("CodeReview: Cleaned up _temp-attachments/ from PR #{PrNumber} branch", prNumber);
                    // Wait for GitHub to process
                    await Task.Delay(TimeSpan.FromSeconds(3), ct);
                }
            }

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

                // Clean up the prep/attachments branch if one was created
                var prepBranch = $"attachments/request-{request.Id}";
                await _gitHubService.DeleteBranchAsync(owner, repo, prepBranch);

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
