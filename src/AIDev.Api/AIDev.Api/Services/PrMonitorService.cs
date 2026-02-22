using AIDev.Api.Data;
using AIDev.Api.Models;
using Microsoft.EntityFrameworkCore;
using Octokit;

namespace AIDev.Api.Services;

/// <summary>
/// Background service that monitors the status of Copilot-initiated PRs
/// and updates DevRequest status accordingly.
/// </summary>
public class PrMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<PrMonitorService> _logger;
    private readonly IConfiguration _configuration;

    private int PollIntervalSeconds => int.Parse(_configuration["CopilotImplementation:PrPollIntervalSeconds"] ?? "120");
    private bool IsEnabled => bool.Parse(_configuration["CopilotImplementation:Enabled"] ?? "true");

    public PrMonitorService(
        IServiceScopeFactory scopeFactory,
        IGitHubService gitHubService,
        ILogger<PrMonitorService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _gitHubService = gitHubService;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PrMonitorService started. Enabled={Enabled}, PollInterval={Interval}s",
            IsEnabled, PollIntervalSeconds);

        // Give the application time to start up
        await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsEnabled)
            {
                try
                {
                    await MonitorInProgressRequestsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in PrMonitorService polling cycle");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("PrMonitorService stopped");
    }

    private async Task MonitorInProgressRequestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Find requests actively being implemented by Copilot
        // Exclude PrOpened and ReviewApproved — those are handled by CodeReviewAgentService
        var inProgressRequests = await db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.CopilotSessionId != null
                     && r.CopilotStatus != CopilotImplementationStatus.PrOpened
                     && r.CopilotStatus != CopilotImplementationStatus.ReviewApproved
                     && r.CopilotStatus != CopilotImplementationStatus.PrMerged
                     && r.CopilotStatus != CopilotImplementationStatus.Failed)
            .ToListAsync(ct);

        if (inProgressRequests.Count == 0)
        {
            _logger.LogDebug("PrMonitor: No in-progress Copilot sessions found");
            return;
        }

        _logger.LogInformation("PrMonitor: Monitoring {Count} in-progress Copilot session(s)", inProgressRequests.Count);
        foreach (var req in inProgressRequests)
        {
            _logger.LogInformation("PrMonitor: Request #{Id} — IssueNumber={Issue}, CopilotStatus={Status}, CopilotPrNumber={Pr}, SessionId={Sid}",
                req.Id, req.GitHubIssueNumber, req.CopilotStatus, req.CopilotPrNumber, req.CopilotSessionId);
        }

        foreach (var request in inProgressRequests)
        {
            try
            {
                await CheckCopilotProgressAsync(request, db, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking Copilot progress for request #{Id}", request.Id);
            }
        }
    }

    private async Task CheckCopilotProgressAsync(DevRequest request, AppDbContext db, CancellationToken ct)
    {
        var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
        var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        // Step 1: If no PR found yet, search for one
        if (request.CopilotPrNumber == null)
        {
            var pr = await _gitHubService.FindPrByIssueAndAuthorAsync(
                owner, repo, request.GitHubIssueNumber!.Value);

            if (pr != null)
            {
                request.CopilotPrNumber = pr.Number;
                request.CopilotPrUrl = pr.HtmlUrl;
                request.CopilotStatus = CopilotImplementationStatus.PrOpened;
                request.CopilotBranchName = pr.Head?.Ref;
                request.UpdatedAt = DateTime.UtcNow;

                // If the PR targets a prep branch (attachments/request-*), retarget to main
                var prBase = pr.Base?.Ref;
                if (!string.IsNullOrEmpty(prBase) && prBase.StartsWith("attachments/", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "PrMonitor: PR #{PrNumber} targets prep branch '{Base}' — retargeting to main",
                        pr.Number, prBase);

                    var retargeted = await _gitHubService.UpdatePrBaseAsync(owner, repo, pr.Number, "main");
                    if (retargeted)
                    {
                        _logger.LogInformation("PrMonitor: Successfully retargeted PR #{PrNumber} to main", pr.Number);

                        // Delete the prep branch since it's no longer needed as the PR base
                        await _gitHubService.DeleteBranchAsync(owner, repo, prBase);
                    }
                    else
                    {
                        _logger.LogWarning("PrMonitor: Failed to retarget PR #{PrNumber} — it still targets '{Base}'", pr.Number, prBase);
                    }
                }

                await db.SaveChangesAsync(ct);

                // Update GitHub labels
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "copilot:implementing");
                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "copilot:pr-ready", "10b981");

                // Post comment on Issue
                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    $"✅ **Copilot has opened PR #{pr.Number}.** Ready for human review.\n\n" +
                    $"🔗 [{pr.Title}]({pr.HtmlUrl})");

                _logger.LogInformation("Copilot opened PR #{PrNumber} for request #{RequestId}",
                    pr.Number, request.Id);
            }
            else
            {
                // No PR found — check if Copilot's workflow run has completed (cancelled/failed)
                var elapsed = DateTime.UtcNow - (request.CopilotTriggeredAt ?? DateTime.UtcNow);

                // After a brief startup window, actively check the Copilot run status
                if (elapsed > TimeSpan.FromMinutes(3))
                {
                    var triggerTime = request.CopilotTriggeredAt ?? DateTime.UtcNow;
                    var runStatus = await _gitHubService.GetCopilotRunStatusAsync(
                        owner, repo,
                        triggerTime.AddSeconds(-30),   // small window before trigger
                        triggerTime.AddMinutes(5));    // Copilot run should start within minutes

                    if (runStatus != null)
                    {
                        var (conclusion, runId, headBranch) = runStatus.Value;

                        if (conclusion is "cancelled" or "failure")
                        {
                            _logger.LogWarning(
                                "Copilot run #{RunId} for request #{Id} has conclusion '{Conclusion}' (branch: {Branch}) — marking Failed",
                                runId, request.Id, conclusion, headBranch);

                            request.CopilotStatus = CopilotImplementationStatus.Failed;
                            request.CopilotCompletedAt = DateTime.UtcNow;
                            request.CopilotBranchName = headBranch;
                            request.UpdatedAt = DateTime.UtcNow;
                            await db.SaveChangesAsync(ct);

                            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                                "copilot:implementing");
                            await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                                "copilot:failed", "ef4444");

                            var reasonText = conclusion == "cancelled"
                                ? "Copilot cancelled its own workflow run without creating a PR."
                                : "Copilot's workflow run failed.";

                            await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                                $"❌ **Copilot session {conclusion}.** {reasonText}\n\n" +
                                $"**Run ID:** {runId} | **Branch:** `{headBranch}`\n\n" +
                                "A human can re-trigger implementation from the AIDev dashboard.");

                            return;
                        }
                    }
                }

                // Fallback: check for hard timeout
                if (elapsed > TimeSpan.FromMinutes(30))
                {
                    _logger.LogWarning("Copilot session for request #{Id} timed out after {Minutes:F0}m",
                        request.Id, elapsed.TotalMinutes);

                    request.CopilotStatus = CopilotImplementationStatus.Failed;
                    request.CopilotCompletedAt = DateTime.UtcNow;
                    request.UpdatedAt = DateTime.UtcNow;
                    await db.SaveChangesAsync(ct);

                    await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                        "copilot:implementing");
                    await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                        "copilot:failed", "ef4444");

                    await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                        "❌ **Copilot session timed out.** No PR was created within 30 minutes.\n\n" +
                        "A human can re-trigger implementation from the AIDev dashboard.");
                }
                else
                {
                    // Mark as Working once some time has passed
                    if (request.CopilotStatus == CopilotImplementationStatus.Pending
                        && elapsed > TimeSpan.FromMinutes(2))
                    {
                        request.CopilotStatus = CopilotImplementationStatus.Working;
                        request.UpdatedAt = DateTime.UtcNow;
                        await db.SaveChangesAsync(ct);
                    }
                }
            }
        }
        else
        {
            // Step 2: PR exists — check its status
            var pr = await _gitHubService.GetPullRequestAsync(owner, repo, request.CopilotPrNumber.Value);

            if (pr == null)
            {
                _logger.LogWarning("Could not retrieve PR #{PrNumber} for request #{Id}",
                    request.CopilotPrNumber, request.Id);
                return;
            }

            if (pr.Merged)
            {
                request.CopilotStatus = CopilotImplementationStatus.PrMerged;
                request.CopilotCompletedAt = DateTime.UtcNow;
                request.Status = RequestStatus.Done;
                request.DeploymentStatus = Models.DeploymentStatus.Pending;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                // Delete the feature branch after merge
                if (!string.IsNullOrEmpty(request.CopilotBranchName))
                {
                    var deleted = await _gitHubService.DeleteBranchAsync(owner, repo, request.CopilotBranchName);
                    if (deleted)
                    {
                        request.BranchDeleted = true;
                        await db.SaveChangesAsync(ct);
                        _logger.LogInformation("Deleted feature branch '{Branch}' after PR merge for request #{Id}",
                            request.CopilotBranchName, request.Id);
                    }
                }

                // Update GitHub labels — clean up all pipeline labels
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "copilot:pr-ready");
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "agent:approved");
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "agent:architect-review");
                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "agent:approved-solution");
                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "copilot:complete", "10b981");

                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    $"🎉 **Implementation complete!** PR #{pr.Number} has been merged.\n\n" +
                    $"Total time from trigger to merge: {(request.CopilotCompletedAt - request.CopilotTriggeredAt)?.TotalMinutes:F0} minutes");

                _logger.LogInformation("PR #{PrNumber} merged for request #{RequestId} — marking Done",
                    pr.Number, request.Id);
            }
            else if (pr.State == ItemState.Closed)
            {
                // PR was closed without merge — failure
                request.CopilotStatus = CopilotImplementationStatus.Failed;
                request.CopilotCompletedAt = DateTime.UtcNow;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "copilot:pr-ready");
                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    "copilot:failed", "ef4444");

                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
                    $"❌ **PR #{pr.Number} was closed without merge.** Implementation marked as failed.\n\n" +
                    "A human can re-trigger implementation from the AIDev dashboard.");

                _logger.LogWarning("PR #{PrNumber} closed without merge for request #{RequestId}",
                    pr.Number, request.Id);
            }
        }
    }
}
