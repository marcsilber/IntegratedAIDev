using AIDev.Api.Data;
using AIDev.Api.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AIDev.Api.Services;

/// <summary>
/// Deterministic background service that orchestrates post-merge activities,
/// monitors pipeline health, detects stalls, tracks deployments, and
/// identifies conflicts between concurrent implementation sessions.
/// </summary>
public class PipelineOrchestratorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<PipelineOrchestratorService> _logger;
    private readonly IConfiguration _configuration;

    private int PollIntervalSeconds => int.Parse(_configuration["PipelineOrchestrator:PollIntervalSeconds"] ?? "60");
    private bool IsEnabled => bool.Parse(_configuration["PipelineOrchestrator:Enabled"] ?? "true");
    private int NeedsClarificationStaleDays => int.Parse(_configuration["PipelineOrchestrator:NeedsClarificationStaleDays"] ?? "7");
    private int ArchitectReviewStaleDays => int.Parse(_configuration["PipelineOrchestrator:ArchitectReviewStaleDays"] ?? "3");
    private int ApprovedStaleDays => int.Parse(_configuration["PipelineOrchestrator:ApprovedStaleDays"] ?? "1");
    private int FailedStaleHours => int.Parse(_configuration["PipelineOrchestrator:FailedStaleHours"] ?? "24");

    public PipelineOrchestratorService(
        IServiceScopeFactory scopeFactory,
        IGitHubService gitHubService,
        ILogger<PipelineOrchestratorService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _gitHubService = gitHubService;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("PipelineOrchestratorService started. Enabled={Enabled}, PollInterval={Interval}s",
            IsEnabled, PollIntervalSeconds);

        // Give the application time to start up
        await Task.Delay(TimeSpan.FromSeconds(25), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsEnabled)
            {
                try
                {
                    await RunOrchestratorCycleAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in PipelineOrchestratorService polling cycle");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("PipelineOrchestratorService stopped");
    }

    private async Task RunOrchestratorCycleAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        await TrackDeploymentsAsync(db, ct);
        await DetectStallsAsync(db, ct);
        await DetectConflictsAsync(db, ct);
    }

    // ── Deployment Tracking ───────────────────────────────────────────────

    /// <summary>
    /// Monitors deployment status for recently merged PRs by checking
    /// GitHub Actions workflow runs on the main branch.
    /// </summary>
    private async Task TrackDeploymentsAsync(AppDbContext db, CancellationToken ct)
    {
        var pendingDeployments = await db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.DeploymentStatus == DeploymentStatus.Pending
                     || r.DeploymentStatus == DeploymentStatus.InProgress)
            .ToListAsync(ct);

        foreach (var request in pendingDeployments)
        {
            try
            {
                await TrackSingleDeploymentAsync(request, db, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error tracking deployment for request #{Id}", request.Id);
            }
        }
    }

    private async Task TrackSingleDeploymentAsync(DevRequest request, AppDbContext db, CancellationToken ct)
    {
        var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
        var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";
        var baseBranch = _configuration["CopilotImplementation:BaseBranch"] ?? "main";

        // The merge time to look for workflow runs after
        var mergeTime = request.CopilotCompletedAt ?? DateTime.UtcNow.AddMinutes(-30);

        var run = await _gitHubService.GetLatestWorkflowRunAsync(owner, repo, baseBranch, mergeTime);

        if (run == null)
        {
            // No workflow run yet — check if we've been waiting too long
            var elapsed = DateTime.UtcNow - mergeTime;
            if (elapsed > TimeSpan.FromMinutes(30))
            {
                _logger.LogWarning("No deployment workflow detected for request #{Id} after {Minutes}m",
                    request.Id, elapsed.TotalMinutes);
            }
            return;
        }

        var (runId, status, conclusion) = run.Value;

        if (request.DeploymentRunId != runId)
        {
            request.DeploymentRunId = runId;
            request.UpdatedAt = DateTime.UtcNow;
        }

        if (status == "completed")
        {
            if (conclusion == "success")
            {
                request.DeploymentStatus = DeploymentStatus.Succeeded;
                request.DeployedAt = DateTime.UtcNow;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                // Post deployment success comment on GitHub issue
                if (request.GitHubIssueNumber.HasValue)
                {
                    await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                        $"🚀 **Deployed to UAT!** The changes from PR #{request.CopilotPrNumber} have been " +
                        $"successfully deployed.\n\n" +
                        $"Deployment completed at {DateTime.UtcNow:yyyy-MM-dd HH:mm} UTC.");

                    await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                        "deployed:uat", "0ea5e9");
                }

                _logger.LogInformation("Deployment succeeded for request #{Id} (workflow run {RunId})",
                    request.Id, runId);
            }
            else
            {
                request.DeploymentStatus = DeploymentStatus.Failed;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                if (request.GitHubIssueNumber.HasValue)
                {
                    await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                        $"⚠️ **Deployment failed!** The GitHub Actions workflow for PR #{request.CopilotPrNumber} " +
                        $"did not complete successfully (conclusion: {conclusion}).\n\n" +
                        "Please check the workflow run and redeploy manually if needed.");

                    await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                        "deploy:failed", "ef4444");
                }

                _logger.LogWarning("Deployment failed for request #{Id} (workflow run {RunId}, conclusion: {Conclusion})",
                    request.Id, runId, conclusion);
            }
        }
        else if (status == "in_progress" || status == "queued" || status == "waiting")
        {
            if (request.DeploymentStatus != DeploymentStatus.InProgress)
            {
                request.DeploymentStatus = DeploymentStatus.InProgress;
                request.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(ct);

                _logger.LogDebug("Deployment in progress for request #{Id} (workflow run {RunId})",
                    request.Id, runId);
            }
        }
    }

    // ── Stall Detection ───────────────────────────────────────────────────

    /// <summary>
    /// Detects requests stuck in intermediate states and posts notifications.
    /// </summary>
    private async Task DetectStallsAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        // 1. NeedsClarification > N days without response
        await DetectNeedsClarificationStallsAsync(db, now, ct);

        // 2. ArchitectReview > N days without human action
        await DetectArchitectReviewStallsAsync(db, now, ct);

        // 3. Approved > N days without implementation trigger
        await DetectApprovedStallsAsync(db, now, ct);

        // 4. Failed implementations not re-triggered
        await DetectFailedStallsAsync(db, now, ct);
    }

    private async Task DetectNeedsClarificationStallsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var threshold = now.AddDays(-NeedsClarificationStaleDays);

        var staleRequests = await db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.Status == RequestStatus.NeedsClarification
                     && r.UpdatedAt < threshold
                     && (r.StallNotifiedAt == null || r.StallNotifiedAt < threshold))
            .ToListAsync(ct);

        foreach (var request in staleRequests)
        {
            var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
            var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

            if (request.GitHubIssueNumber.HasValue)
            {
                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                    $"⏰ **Stall Alert:** This request has been waiting for clarification for " +
                    $"{(now - request.UpdatedAt).Days} days. Please provide the requested information " +
                    $"or consider closing this request.\n\n" +
                    $"_This is an automated pipeline health notification._");

                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                    "pipeline:stalled", "f59e0b");
            }

            request.StallNotifiedAt = now;
            request.UpdatedAt = now;
            await db.SaveChangesAsync(ct);

            _logger.LogWarning("Stall detected: Request #{Id} stuck in NeedsClarification for {Days} days",
                request.Id, (now - request.UpdatedAt).Days);
        }
    }

    private async Task DetectArchitectReviewStallsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var threshold = now.AddDays(-ArchitectReviewStaleDays);

        var staleRequests = await db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.Status == RequestStatus.ArchitectReview
                     && r.UpdatedAt < threshold
                     && (r.StallNotifiedAt == null || r.StallNotifiedAt < threshold))
            .ToListAsync(ct);

        foreach (var request in staleRequests)
        {
            var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
            var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

            if (request.GitHubIssueNumber.HasValue)
            {
                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                    $"⏰ **Stall Alert:** The architect's solution proposal has been awaiting human review for " +
                    $"{(now - request.UpdatedAt).Days} days. Please approve, reject, or provide feedback.\n\n" +
                    $"_This is an automated pipeline health notification._");

                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                    "pipeline:stalled", "f59e0b");
            }

            request.StallNotifiedAt = now;
            await db.SaveChangesAsync(ct);

            _logger.LogWarning("Stall detected: Request #{Id} stuck in ArchitectReview for {Days} days",
                request.Id, (now - request.UpdatedAt).Days);
        }
    }

    private async Task DetectApprovedStallsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var threshold = now.AddDays(-ApprovedStaleDays);

        var staleRequests = await db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.Status == RequestStatus.Approved
                     && r.CopilotSessionId == null
                     && r.UpdatedAt < threshold
                     && (r.StallNotifiedAt == null || r.StallNotifiedAt < threshold))
            .ToListAsync(ct);

        foreach (var request in staleRequests)
        {
            var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
            var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

            if (request.GitHubIssueNumber.HasValue)
            {
                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                    $"⏰ **Stall Alert:** This request was approved {(now - request.UpdatedAt).Days} day(s) ago " +
                    $"but implementation has not been triggered. Check if auto-trigger is enabled or " +
                    $"trigger manually from the dashboard.\n\n" +
                    $"_This is an automated pipeline health notification._");

                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                    "pipeline:stalled", "f59e0b");
            }

            request.StallNotifiedAt = now;
            await db.SaveChangesAsync(ct);

            _logger.LogWarning("Stall detected: Request #{Id} approved but not triggered for {Days} day(s)",
                request.Id, (now - request.UpdatedAt).Days);
        }
    }

    private async Task DetectFailedStallsAsync(AppDbContext db, DateTime now, CancellationToken ct)
    {
        var threshold = now.AddHours(-FailedStaleHours);

        var staleRequests = await db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.CopilotStatus == CopilotImplementationStatus.Failed
                     && r.CopilotCompletedAt < threshold
                     && (r.StallNotifiedAt == null || r.StallNotifiedAt < threshold))
            .ToListAsync(ct);

        foreach (var request in staleRequests)
        {
            var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
            var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

            if (request.GitHubIssueNumber.HasValue)
            {
                await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                    $"⏰ **Stall Alert:** The Copilot implementation failed {FailedStaleHours}+ hours ago " +
                    $"and has not been re-triggered. Please re-trigger from the dashboard or investigate.\n\n" +
                    $"_This is an automated pipeline health notification._");

                await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                    "pipeline:stalled", "f59e0b");
            }

            request.StallNotifiedAt = now;
            await db.SaveChangesAsync(ct);

            _logger.LogWarning("Stall detected: Request #{Id} failed implementation not re-triggered for {Hours}h",
                request.Id, (now - (request.CopilotCompletedAt ?? now)).TotalHours);
        }
    }

    // ── Conflict Detection ────────────────────────────────────────────────

    /// <summary>
    /// Compares SolutionJson across concurrent InProgress requests to detect
    /// overlapping file modifications that could cause merge conflicts.
    /// </summary>
    private async Task DetectConflictsAsync(AppDbContext db, CancellationToken ct)
    {
        var activeRequests = await db.DevRequests
            .Include(r => r.Project)
            .Include(r => r.ArchitectReviews)
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.CopilotStatus != CopilotImplementationStatus.PrMerged
                     && r.CopilotStatus != CopilotImplementationStatus.Failed)
            .ToListAsync(ct);

        if (activeRequests.Count < 2) return;

        // Extract file lists from each request's latest approved architect review
        var requestFiles = new Dictionary<int, HashSet<string>>();
        foreach (var request in activeRequests)
        {
            var latestReview = request.ArchitectReviews
                .Where(r => r.Decision == ArchitectDecision.Approved)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();

            if (latestReview?.SolutionJson == null) continue;

            var files = ExtractFilesFromSolutionJson(latestReview.SolutionJson);
            if (files.Count > 0)
            {
                requestFiles[request.Id] = files;
            }
        }

        // Compare all pairs for overlapping files
        var requestIds = requestFiles.Keys.ToList();
        for (int i = 0; i < requestIds.Count; i++)
        {
            for (int j = i + 1; j < requestIds.Count; j++)
            {
                var idA = requestIds[i];
                var idB = requestIds[j];
                var overlap = requestFiles[idA].Intersect(requestFiles[idB]).ToList();

                if (overlap.Count > 0)
                {
                    var reqA = activeRequests.First(r => r.Id == idA);
                    var reqB = activeRequests.First(r => r.Id == idB);

                    _logger.LogWarning(
                        "Conflict detected: Requests #{IdA} and #{IdB} share {Count} file(s): {Files}",
                        idA, idB, overlap.Count, string.Join(", ", overlap.Take(5)));

                    // Notify on both issues (only once per stall cycle)
                    await NotifyConflictAsync(reqA, reqB, overlap);
                    await NotifyConflictAsync(reqB, reqA, overlap);
                }
            }
        }
    }

    private async Task NotifyConflictAsync(DevRequest request, DevRequest conflicting, List<string> overlappingFiles)
    {
        var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
        var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        if (!request.GitHubIssueNumber.HasValue) return;

        // Only notify once per day
        if (request.StallNotifiedAt.HasValue &&
            (DateTime.UtcNow - request.StallNotifiedAt.Value).TotalHours < 24)
            return;

        var fileList = string.Join("\n", overlappingFiles.Take(10).Select(f => $"- `{f}`"));
        await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
            $"⚠️ **Potential Conflict:** This request modifies files that overlap with " +
            $"Request #{conflicting.Id} (Issue #{conflicting.GitHubIssueNumber}).\n\n" +
            $"**Overlapping files ({overlappingFiles.Count}):**\n{fileList}\n\n" +
            $"Merge conflicts may occur. Consider sequencing these implementations.\n\n" +
            $"_This is an automated pipeline health notification._");

        await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
            "pipeline:conflict", "e11d48");
    }

    /// <summary>
    /// Extracts file paths from the architect's SolutionJson structure.
    /// </summary>
    private static HashSet<string> ExtractFilesFromSolutionJson(string solutionJson)
    {
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            using var doc = JsonDocument.Parse(solutionJson);
            var root = doc.RootElement;

            // Extract impacted files
            if (root.TryGetProperty("impactedFiles", out var impacted) && impacted.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in impacted.EnumerateArray())
                {
                    if (item.TryGetProperty("path", out var path))
                        files.Add(path.GetString() ?? "");
                }
            }

            // Extract new files
            if (root.TryGetProperty("newFiles", out var newFiles) && newFiles.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in newFiles.EnumerateArray())
                {
                    if (item.TryGetProperty("path", out var path))
                        files.Add(path.GetString() ?? "");
                }
            }
        }
        catch (JsonException ex)
        {
            // Non-critical — if we can't parse, skip conflict detection for this request
            System.Diagnostics.Debug.WriteLine($"Failed to parse SolutionJson: {ex.Message}");
        }

        files.Remove(""); // Remove empty entries
        return files;
    }

    // ── Public methods for health queries ─────────────────────────────────

    /// <summary>
    /// Gets the current pipeline health summary. Called by OrchestratorController.
    /// </summary>
    public static async Task<PipelineHealthSummary> GetHealthSummaryAsync(AppDbContext db)
    {
        var now = DateTime.UtcNow;
        var requests = await db.DevRequests.ToListAsync();

        var stalledNeedsClarification = requests.Count(r =>
            r.Status == RequestStatus.NeedsClarification && (now - r.UpdatedAt).TotalDays > 7);
        var stalledArchitectReview = requests.Count(r =>
            r.Status == RequestStatus.ArchitectReview && (now - r.UpdatedAt).TotalDays > 3);
        var stalledApproved = requests.Count(r =>
            r.Status == RequestStatus.Approved && r.CopilotSessionId == null && (now - r.UpdatedAt).TotalDays > 1);
        var stalledFailed = requests.Count(r =>
            r.Status == RequestStatus.InProgress && r.CopilotStatus == CopilotImplementationStatus.Failed
            && r.CopilotCompletedAt.HasValue && (now - r.CopilotCompletedAt.Value).TotalHours > 24);

        var deploymentsPending = requests.Count(r => r.DeploymentStatus == DeploymentStatus.Pending);
        var deploymentsInProgress = requests.Count(r => r.DeploymentStatus == DeploymentStatus.InProgress);
        var deploymentsSucceeded = requests.Count(r => r.DeploymentStatus == DeploymentStatus.Succeeded);
        var deploymentsFailed = requests.Count(r => r.DeploymentStatus == DeploymentStatus.Failed);

        var branchesDeleted = requests.Count(r => r.BranchDeleted);
        var branchesOutstanding = requests.Count(r =>
            !string.IsNullOrEmpty(r.CopilotBranchName) && !r.BranchDeleted
            && r.CopilotStatus == CopilotImplementationStatus.PrMerged);

        return new PipelineHealthSummary
        {
            TotalStalled = stalledNeedsClarification + stalledArchitectReview + stalledApproved + stalledFailed,
            StalledNeedsClarification = stalledNeedsClarification,
            StalledArchitectReview = stalledArchitectReview,
            StalledApproved = stalledApproved,
            StalledFailed = stalledFailed,
            DeploymentsPending = deploymentsPending,
            DeploymentsInProgress = deploymentsInProgress,
            DeploymentsSucceeded = deploymentsSucceeded,
            DeploymentsFailed = deploymentsFailed,
            BranchesDeleted = branchesDeleted,
            BranchesOutstanding = branchesOutstanding
        };
    }
}

/// <summary>
/// Summary of pipeline health metrics.
/// </summary>
public class PipelineHealthSummary
{
    public int TotalStalled { get; set; }
    public int StalledNeedsClarification { get; set; }
    public int StalledArchitectReview { get; set; }
    public int StalledApproved { get; set; }
    public int StalledFailed { get; set; }
    public int DeploymentsPending { get; set; }
    public int DeploymentsInProgress { get; set; }
    public int DeploymentsSucceeded { get; set; }
    public int DeploymentsFailed { get; set; }
    public int BranchesDeleted { get; set; }
    public int BranchesOutstanding { get; set; }
}
