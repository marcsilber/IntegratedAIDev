using AIDev.Api.Data;
using AIDev.Api.Models;
using AIDev.Api.Models.DTOs;
using AIDev.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Controllers;

/// <summary>
/// Pipeline orchestrator endpoints for health monitoring, stall detection,
/// deployment tracking, and configuration.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class OrchestratorController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OrchestratorController> _logger;
    private readonly ICodebaseService _codebaseService;
    private readonly ILlmClientFactory _llmClientFactory;
    private readonly IArchitectLlmService _architectLlmService;
    private readonly IGitHubService _gitHubService;

    public OrchestratorController(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<OrchestratorController> logger,
        ICodebaseService codebaseService,
        ILlmClientFactory llmClientFactory,
        IArchitectLlmService architectLlmService,
        IGitHubService gitHubService)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
        _codebaseService = codebaseService;
        _llmClientFactory = llmClientFactory;
        _architectLlmService = architectLlmService;
        _gitHubService = gitHubService;
    }

    // ── Health ────────────────────────────────────────────────────────────

    /// <summary>
    /// Quick anonymous diagnostic endpoint to check deployed API status.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("ping")]
    public async Task<ActionResult> Ping()
    {
        var triagedCount = await _db.DevRequests.CountAsync(r => r.Status == RequestStatus.Triaged);
        var architectReviewCount = await _db.DevRequests.CountAsync(r => r.Status == RequestStatus.ArchitectReview);
        var approvedCount = await _db.DevRequests.CountAsync(r => r.Status == RequestStatus.Approved);
        var inProgressCount = await _db.DevRequests.CountAsync(r => r.Status == RequestStatus.InProgress);
        var doneCount = await _db.DevRequests.CountAsync(r => r.Status == RequestStatus.Done);
        var totalRequests = await _db.DevRequests.CountAsync();
        var totalArchReviews = await _db.ArchitectReviews.CountAsync();

        // Copilot session details for in-progress requests
        var copilotSessions = await _db.DevRequests
            .Where(r => r.Status == RequestStatus.InProgress && r.CopilotSessionId != null)
            .Select(r => new { r.Id, r.GitHubIssueNumber, copilotStatus = r.CopilotStatus.ToString(), r.CopilotPrNumber, r.CopilotSessionId })
            .ToListAsync();

        return Ok(new
        {
            status = "ok",
            timestamp = DateTime.UtcNow,
            architectAgentEnabled = bool.Parse(_configuration["ArchitectAgent:Enabled"] ?? "true"),
            requests = new { total = totalRequests, triaged = triagedCount, architectReview = architectReviewCount, approved = approvedCount, inProgress = inProgressCount, done = doneCount },
            architectReviews = totalArchReviews,
            copilotSessions
        });
    }

    /// <summary>
    /// Deep diagnostic endpoint — tests each step of the Architect Agent pipeline
    /// and reports exactly what fails.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("diagnose")]
    public async Task<ActionResult> Diagnose()
    {
        var diagnostics = new List<object>();

        // 1. Find candidates exactly as ArchitectAgentService does
        var candidates = await _db.DevRequests
            .Include(r => r.Comments)
            .Include(r => r.Project)
            .Include(r => r.AgentReviews)
            .Include(r => r.ArchitectReviews)
            .Where(r =>
                (r.Status == RequestStatus.Triaged && r.ArchitectReviewCount == 0)
                ||
                (r.Status == RequestStatus.ArchitectReview
                 && r.ArchitectReviewCount < 3
                 && r.Comments.Any(c => !c.IsAgentComment
                     && c.CreatedAt > (r.LastArchitectReviewAt ?? DateTime.MinValue)))
            )
            .OrderBy(r => r.CreatedAt)
            .Take(5)
            .ToListAsync();

        foreach (var req in candidates)
        {
            var diag = new Dictionary<string, object?>
            {
                ["requestId"] = req.Id,
                ["title"] = req.Title,
                ["status"] = req.Status.ToString(),
                ["architectReviewCount"] = req.ArchitectReviewCount,
                ["agentReviewCount"] = req.AgentReviews.Count,
                ["projectId"] = req.ProjectId,
                ["projectName"] = req.Project?.DisplayName,
                ["gitHubOwner"] = req.Project?.GitHubOwner,
                ["gitHubRepo"] = req.Project?.GitHubRepo,
            };

            // Check PO review
            var poReview = req.AgentReviews.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
            diag["hasPOReview"] = poReview != null;
            diag["poDecision"] = poReview?.Decision;

            if (req.Project == null)
            {
                diag["error"] = "No project linked";
                diagnostics.Add(diag);
                continue;
            }

            if (poReview == null)
            {
                diag["error"] = "No PO AgentReview found";
                diagnostics.Add(diag);
                continue;
            }

            // Test repo map
            try
            {
                var repoMap = await _codebaseService.GetRepositoryMapAsync(
                    req.Project.GitHubOwner, req.Project.GitHubRepo);
                diag["repoMapLength"] = repoMap?.Length ?? 0;
                diag["repoMapPreview"] = repoMap?.Length > 200 ? repoMap[..200] + "..." : repoMap;
            }
            catch (Exception ex)
            {
                diag["repoMapError"] = $"{ex.GetType().Name}: {ex.Message}";
            }

            diagnostics.Add(diag);
        }

        // Also check config
        var ghToken = _configuration["GitHub:PersonalAccessToken"];
        var endpoint = _configuration["GitHubModels:Endpoint"];
        var model = _configuration["GitHubModels:ModelName"];

        // Test LLM call
        string? llmTestResult = null;
        string? llmTestError = null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var chatClient = _llmClientFactory.CreateChatClient();
            var messages = new List<OpenAI.Chat.ChatMessage>
            {
                new OpenAI.Chat.SystemChatMessage("Reply with exactly: OK"),
                new OpenAI.Chat.UserChatMessage("Test")
            };
            var opts = new OpenAI.Chat.ChatCompletionOptions { MaxOutputTokenCount = 10 };
            var completion = await chatClient.CompleteChatAsync(messages, opts, cts.Token);
            llmTestResult = completion.Value.Content[0].Text;
        }
        catch (Exception ex)
        {
            llmTestError = $"{ex.GetType().Name}: {ex.Message}";
        }

        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            candidateCount = candidates.Count,
            candidates = diagnostics,
            config = new
            {
                hasGitHubToken = !string.IsNullOrEmpty(ghToken),
                gitHubTokenLength = ghToken?.Length ?? 0,
                llmEndpoint = endpoint ?? "(default)",
                llmModel = model ?? "(default)",
                architectEnabled = _configuration["ArchitectAgent:Enabled"]
            },
            llmTest = new
            {
                result = llmTestResult,
                error = llmTestError
            }
        });
    }

    /// <summary>
    /// Test architect review process for a specific request. Returns detailed error info.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("test-architect/{requestId}")]
    public async Task<ActionResult> TestArchitect(int requestId)
    {
        var steps = new List<object>();

        try
        {
            // 1. Load request
            var request = await _db.DevRequests
                .Include(r => r.Comments)
                .Include(r => r.Project)
                .Include(r => r.AgentReviews)
                .Include(r => r.ArchitectReviews)
                .Include(r => r.Attachments)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null)
                return NotFound(new { error = $"Request #{requestId} not found" });

            steps.Add(new { step = "load_request", ok = true, title = request.Title, status = request.Status.ToString() });

            // 2. Get PO review
            var poReview = request.AgentReviews.OrderByDescending(r => r.CreatedAt).FirstOrDefault();
            if (poReview == null)
                return Ok(new { steps, error = "No PO review found" });

            steps.Add(new { step = "po_review", ok = true, decision = poReview.Decision, alignmentScore = poReview.AlignmentScore, completenessScore = poReview.CompletenessScore });

            // 3. Get repo map
            var owner = request.Project!.GitHubOwner;
            var repo = request.Project.GitHubRepo;
            var repoMap = await _codebaseService.GetRepositoryMapAsync(owner, repo);
            steps.Add(new { step = "repo_map", ok = true, length = repoMap?.Length ?? 0 });

            // 4. File reader delegate
            async Task<Dictionary<string, string>> FileReader(IEnumerable<string> files)
                => await _codebaseService.GetFileContentsAsync(owner, repo, files);

            // 5. Call architect LLM
            var result = await _architectLlmService.AnalyseRequestAsync(
                request, poReview, repoMap, FileReader, null, request.Attachments?.ToList());
            steps.Add(new
            {
                step = "architect_llm",
                ok = true,
                summary = result.SolutionSummary,
                complexity = result.EstimatedComplexity,
                filesRead = result.FilesRead.Count,
                step1Tokens = result.Step1PromptTokens + result.Step1CompletionTokens,
                step2Tokens = result.Step2PromptTokens + result.Step2CompletionTokens,
                durationMs = result.TotalDurationMs
            });

            return Ok(new { success = true, steps });
        }
        catch (Exception ex)
        {
            steps.Add(new { step = "error", ok = false, error = $"{ex.GetType().Name}: {ex.Message}", stackTrace = ex.StackTrace?.Split('\n').Take(5) });
            return Ok(new { success = false, steps });
        }
    }

    /// <summary>
    /// Get pipeline health summary with stall counts and deployment status.
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<PipelineHealthDto>> GetHealth()
    {
        var summary = await PipelineOrchestratorService.GetHealthSummaryAsync(_db);

        return Ok(new PipelineHealthDto
        {
            TotalStalled = summary.TotalStalled,
            StalledNeedsClarification = summary.StalledNeedsClarification,
            StalledArchitectReview = summary.StalledArchitectReview,
            StalledApproved = summary.StalledApproved,
            StalledFailed = summary.StalledFailed,
            DeploymentsPending = summary.DeploymentsPending,
            DeploymentsInProgress = summary.DeploymentsInProgress,
            DeploymentsSucceeded = summary.DeploymentsSucceeded,
            DeploymentsFailed = summary.DeploymentsFailed,
            DeploymentsRetrying = summary.DeploymentsRetrying,
            StagedForDeploy = summary.StagedForDeploy,
            BranchesDeleted = summary.BranchesDeleted,
            BranchesOutstanding = summary.BranchesOutstanding
        });
    }

    // ── Stalled Requests ──────────────────────────────────────────────────

    /// <summary>
    /// List requests that are stalled in intermediate pipeline states.
    /// </summary>
    [HttpGet("stalled")]
    public async Task<ActionResult<List<StalledRequestDto>>> GetStalledRequests()
    {
        var now = DateTime.UtcNow;
        var stalled = new List<StalledRequestDto>();

        // NeedsClarification > 7 days
        var needsClarification = await _db.DevRequests
            .Where(r => r.Status == RequestStatus.NeedsClarification)
            .ToListAsync();

        foreach (var r in needsClarification.Where(r => (now - r.UpdatedAt).TotalDays > 7))
        {
            stalled.Add(new StalledRequestDto
            {
                RequestId = r.Id,
                Title = r.Title,
                Status = "NeedsClarification",
                StallReason = $"Waiting for clarification for {(now - r.UpdatedAt).Days} days",
                Severity = (now - r.UpdatedAt).TotalDays > 14 ? "Critical" : "Warning",
                GitHubIssueNumber = r.GitHubIssueNumber,
                DaysStalled = (int)(now - r.UpdatedAt).TotalDays,
                StallNotifiedAt = r.StallNotifiedAt
            });
        }

        // ArchitectReview > 3 days
        var architectReview = await _db.DevRequests
            .Where(r => r.Status == RequestStatus.ArchitectReview)
            .ToListAsync();

        foreach (var r in architectReview.Where(r => (now - r.UpdatedAt).TotalDays > 3))
        {
            stalled.Add(new StalledRequestDto
            {
                RequestId = r.Id,
                Title = r.Title,
                Status = "ArchitectReview",
                StallReason = $"Awaiting human review for {(now - r.UpdatedAt).Days} days",
                Severity = (now - r.UpdatedAt).TotalDays > 7 ? "Critical" : "Warning",
                GitHubIssueNumber = r.GitHubIssueNumber,
                DaysStalled = (int)(now - r.UpdatedAt).TotalDays,
                StallNotifiedAt = r.StallNotifiedAt
            });
        }

        // Approved > 1 day without trigger
        var approved = await _db.DevRequests
            .Where(r => r.Status == RequestStatus.Approved && r.CopilotSessionId == null)
            .ToListAsync();

        foreach (var r in approved.Where(r => (now - r.UpdatedAt).TotalDays > 1))
        {
            stalled.Add(new StalledRequestDto
            {
                RequestId = r.Id,
                Title = r.Title,
                Status = "Approved",
                StallReason = $"Approved but not triggered for {(now - r.UpdatedAt).Days} day(s)",
                Severity = (now - r.UpdatedAt).TotalDays > 3 ? "Critical" : "Warning",
                GitHubIssueNumber = r.GitHubIssueNumber,
                DaysStalled = (int)(now - r.UpdatedAt).TotalDays,
                StallNotifiedAt = r.StallNotifiedAt
            });
        }

        // Failed > 24h
        var failed = await _db.DevRequests
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.CopilotStatus == CopilotImplementationStatus.Failed)
            .ToListAsync();

        foreach (var r in failed.Where(r => r.CopilotCompletedAt.HasValue && (now - r.CopilotCompletedAt.Value).TotalHours > 24))
        {
            stalled.Add(new StalledRequestDto
            {
                RequestId = r.Id,
                Title = r.Title,
                Status = "Failed",
                StallReason = $"Failed {(int)(now - r.CopilotCompletedAt!.Value).TotalHours}h ago, not re-triggered",
                Severity = (now - r.CopilotCompletedAt.Value).TotalDays > 3 ? "Critical" : "Warning",
                GitHubIssueNumber = r.GitHubIssueNumber,
                DaysStalled = (int)(now - r.CopilotCompletedAt.Value).TotalDays,
                StallNotifiedAt = r.StallNotifiedAt
            });
        }

        return Ok(stalled.OrderByDescending(s => s.DaysStalled).ToList());
    }

    // ── Deployment Tracking ───────────────────────────────────────────────

    /// <summary>
    /// List deployment tracking status for completed requests.
    /// </summary>
    [HttpGet("deployments")]
    public async Task<ActionResult<List<DeploymentTrackingDto>>> GetDeployments(
        [FromQuery] DeploymentStatus? status = null)
    {
        var query = _db.DevRequests
            .Where(r => r.CopilotStatus == CopilotImplementationStatus.PrMerged
                     || r.DeploymentStatus != DeploymentStatus.None);

        if (status.HasValue)
            query = query.Where(r => r.DeploymentStatus == status.Value);

        var requests = await query
            .OrderByDescending(r => r.CopilotCompletedAt)
            .ToListAsync();

        return Ok(requests.Select(r => new DeploymentTrackingDto
        {
            RequestId = r.Id,
            Title = r.Title,
            PrNumber = r.CopilotPrNumber,
            DeploymentStatus = r.DeploymentStatus.ToString(),
            DeploymentRunId = r.DeploymentRunId,
            MergedAt = r.CopilotCompletedAt,
            DeployedAt = r.DeployedAt,
            BranchDeleted = r.BranchDeleted,
            BranchName = r.CopilotBranchName,
            RetryCount = r.DeploymentRetryCount
        }).ToList());
    }

    // ── Config ────────────────────────────────────────────────────────────

    /// <summary>
    /// Get pipeline orchestrator configuration.
    /// </summary>
    [HttpGet("config")]
    public ActionResult<PipelineConfigDto> GetConfig()
    {
        return Ok(new PipelineConfigDto
        {
            Enabled = bool.Parse(_configuration["PipelineOrchestrator:Enabled"] ?? "true"),
            PollIntervalSeconds = int.Parse(_configuration["PipelineOrchestrator:PollIntervalSeconds"] ?? "60"),
            NeedsClarificationStaleDays = int.Parse(_configuration["PipelineOrchestrator:NeedsClarificationStaleDays"] ?? "7"),
            ArchitectReviewStaleDays = int.Parse(_configuration["PipelineOrchestrator:ArchitectReviewStaleDays"] ?? "3"),
            ApprovedStaleDays = int.Parse(_configuration["PipelineOrchestrator:ApprovedStaleDays"] ?? "1"),
            FailedStaleHours = int.Parse(_configuration["PipelineOrchestrator:FailedStaleHours"] ?? "24"),
            DeploymentMode = _configuration["PipelineOrchestrator:DeploymentMode"] ?? "Auto",
            MaxDeployRetries = int.Parse(_configuration["PipelineOrchestrator:MaxDeployRetries"] ?? "3")
        });
    }

    /// <summary>
    /// Update pipeline orchestrator configuration.
    /// </summary>
    [HttpPut("config")]
    public ActionResult<PipelineConfigDto> UpdateConfig([FromBody] PipelineConfigUpdateDto update)
    {
        if (update.Enabled.HasValue)
            _configuration["PipelineOrchestrator:Enabled"] = update.Enabled.Value.ToString();
        if (update.PollIntervalSeconds.HasValue)
            _configuration["PipelineOrchestrator:PollIntervalSeconds"] = update.PollIntervalSeconds.Value.ToString();
        if (update.NeedsClarificationStaleDays.HasValue)
            _configuration["PipelineOrchestrator:NeedsClarificationStaleDays"] = update.NeedsClarificationStaleDays.Value.ToString();
        if (update.ArchitectReviewStaleDays.HasValue)
            _configuration["PipelineOrchestrator:ArchitectReviewStaleDays"] = update.ArchitectReviewStaleDays.Value.ToString();
        if (update.ApprovedStaleDays.HasValue)
            _configuration["PipelineOrchestrator:ApprovedStaleDays"] = update.ApprovedStaleDays.Value.ToString();
        if (update.FailedStaleHours.HasValue)
            _configuration["PipelineOrchestrator:FailedStaleHours"] = update.FailedStaleHours.Value.ToString();
        if (update.DeploymentMode != null)
            _configuration["PipelineOrchestrator:DeploymentMode"] = update.DeploymentMode;
        if (update.MaxDeployRetries.HasValue)
            _configuration["PipelineOrchestrator:MaxDeployRetries"] = update.MaxDeployRetries.Value.ToString();

        return GetConfig();
    }

    // ── Deploy Actions ────────────────────────────────────────────────────

    /// <summary>
    /// Get staged (approved but unmerged) PRs awaiting deployment.
    /// </summary>
    [HttpGet("staged")]
    public async Task<ActionResult<List<StagedDeploymentDto>>> GetStagedDeployments()
    {
        var staged = await _db.DevRequests
            .Include(r => r.Project)
            .Include(r => r.CodeReviews)
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.CopilotStatus == CopilotImplementationStatus.ReviewApproved
                     && r.CopilotPrNumber != null)
            .OrderBy(r => r.CopilotTriggeredAt)
            .ToListAsync();

        return Ok(staged.Select(r => new StagedDeploymentDto
        {
            RequestId = r.Id,
            Title = r.Title,
            PrNumber = r.CopilotPrNumber!.Value,
            PrUrl = r.CopilotPrUrl ?? "",
            BranchName = r.CopilotBranchName ?? "",
            QualityScore = r.CodeReviews.OrderByDescending(cr => cr.CreatedAt).FirstOrDefault()?.QualityScore ?? 0,
            ApprovedAt = r.CodeReviews.OrderByDescending(cr => cr.CreatedAt).FirstOrDefault()?.CreatedAt,
            GitHubIssueNumber = r.GitHubIssueNumber
        }).ToList());
    }

    /// <summary>
    /// Deploy all staged PRs: merge them and let the push-triggered workflows deploy.
    /// </summary>
    [HttpPost("deploy")]
    public async Task<ActionResult<DeployTriggerResponseDto>> TriggerDeploy()
    {
        var owner = _configuration["GitHub:Owner"] ?? "";
        var repo = _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        var staged = await _db.DevRequests
            .Include(r => r.Project)
            .Where(r => r.Status == RequestStatus.InProgress
                     && r.CopilotStatus == CopilotImplementationStatus.ReviewApproved
                     && r.CopilotPrNumber != null)
            .OrderBy(r => r.CopilotTriggeredAt)
            .ToListAsync();

        var merged = new List<int>();
        var failed = new List<int>();

        foreach (var request in staged)
        {
            var reqOwner = request.Project?.GitHubOwner ?? owner;
            var reqRepo = request.Project?.GitHubRepo ?? repo;
            var prNumber = request.CopilotPrNumber!.Value;

            try
            {
                // Ensure branch is up-to-date before merging
                if (!string.IsNullOrEmpty(request.CopilotBranchName))
                {
                    var behindBy = await _gitHubService.GetBehindByCountAsync(reqOwner, reqRepo, "main", request.CopilotBranchName);
                    if (behindBy > 0)
                    {
                        var updated = await _gitHubService.UpdatePrBranchAsync(reqOwner, reqRepo, prNumber);
                        if (!updated)
                        {
                            _logger.LogWarning("Deploy: PR #{PrNumber} has merge conflicts, skipping", prNumber);
                            failed.Add(prNumber);
                            continue;
                        }
                        await Task.Delay(TimeSpan.FromSeconds(5));
                    }
                }

                // Clean up temp attachments before merge
                if (!string.IsNullOrEmpty(request.CopilotBranchName))
                {
                    await _gitHubService.RemoveFilesFromBranchAsync(
                        reqOwner, reqRepo, request.CopilotBranchName,
                        "_temp-attachments/", "Clean up temp attachment files before merge");
                    await Task.Delay(TimeSpan.FromSeconds(2));
                }

                // Merge PR
                var commitMsg = $"{request.Title} (#{prNumber})\n\nMerged via Deploy action.\n";
                var mergeOk = await _gitHubService.MergePullRequestAsync(reqOwner, reqRepo, prNumber, commitMsg);

                if (mergeOk)
                {
                    merged.Add(prNumber);
                    request.CopilotStatus = CopilotImplementationStatus.PrMerged;
                    request.CopilotCompletedAt = DateTime.UtcNow;
                    request.Status = RequestStatus.Done;
                    request.DeploymentStatus = Models.DeploymentStatus.Pending;
                    request.DeploymentRetryCount = 0;
                    request.UpdatedAt = DateTime.UtcNow;

                    // Delete feature branch
                    if (!string.IsNullOrEmpty(request.CopilotBranchName))
                    {
                        var deleted = await _gitHubService.DeleteBranchAsync(reqOwner, reqRepo, request.CopilotBranchName);
                        if (deleted) request.BranchDeleted = true;
                    }

                    // Clean up labels
                    if (request.GitHubIssueNumber.HasValue)
                    {
                        await _gitHubService.RemoveLabelAsync(reqOwner, reqRepo, request.GitHubIssueNumber.Value, "review:approved");
                        await _gitHubService.RemoveLabelAsync(reqOwner, reqRepo, request.GitHubIssueNumber.Value, "deploy:staged");
                        await _gitHubService.AddLabelAsync(reqOwner, reqRepo, request.GitHubIssueNumber.Value, "copilot:complete", "10b981");

                        await _gitHubService.PostAgentCommentAsync(reqOwner, reqRepo, request.GitHubIssueNumber.Value,
                            $"🚀 **Deployed!** PR #{prNumber} merged and deployment triggered.\n\n" +
                            "Triggered by manual deploy action from AIDev Admin panel.");
                    }
                }
                else
                {
                    failed.Add(prNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Deploy: Error merging PR #{PrNumber}", prNumber);
                failed.Add(prNumber);
            }
        }

        await _db.SaveChangesAsync();

        return Ok(new DeployTriggerResponseDto
        {
            MergedPrs = merged,
            FailedPrs = failed,
            Message = merged.Count > 0
                ? $"Merged {merged.Count} PR(s). Deployment workflows will trigger automatically."
                : "No PRs were merged."
        });
    }

    /// <summary>
    /// Manually trigger workflow_dispatch for both API and Web deploy workflows.
    /// Useful for redeploying without merging anything new.
    /// </summary>
    [HttpPost("deploy/trigger-workflows")]
    public async Task<ActionResult> TriggerWorkflows()
    {
        var owner = _configuration["GitHub:Owner"] ?? "";
        var repo = _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        var apiOk = await _gitHubService.TriggerWorkflowDispatchAsync(owner, repo, "deploy-api.yml");
        var webOk = await _gitHubService.TriggerWorkflowDispatchAsync(owner, repo, "deploy-web.yml");

        return Ok(new
        {
            apiTriggered = apiOk,
            webTriggered = webOk,
            message = apiOk && webOk ? "Both deployment workflows triggered." :
                      apiOk ? "Only API workflow triggered." :
                      webOk ? "Only Web workflow triggered." :
                      "Failed to trigger workflows."
        });
    }

    /// <summary>
    /// Retry a specific failed deployment by rerunning the workflow or dispatching a new one.
    /// </summary>
    [HttpPost("deploy/retry/{requestId}")]
    public async Task<ActionResult> RetryDeployment(int requestId)
    {
        var request = await _db.DevRequests
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null) return NotFound(new { error = $"Request #{requestId} not found" });
        if (request.DeploymentStatus != Models.DeploymentStatus.Failed)
            return BadRequest(new { error = "Request deployment is not in Failed status" });

        var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
        var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        bool success = false;

        // Try rerunning the failed workflow run first
        if (request.DeploymentRunId.HasValue)
        {
            success = await _gitHubService.RerunFailedJobsAsync(owner, repo, request.DeploymentRunId.Value);
        }

        // If rerun didn't work, trigger fresh dispatches
        if (!success)
        {
            var apiOk = await _gitHubService.TriggerWorkflowDispatchAsync(owner, repo, "deploy-api.yml");
            var webOk = await _gitHubService.TriggerWorkflowDispatchAsync(owner, repo, "deploy-web.yml");
            success = apiOk || webOk;
        }

        if (success)
        {
            request.DeploymentStatus = Models.DeploymentStatus.Pending;
            request.DeploymentRetryCount++;
            request.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return Ok(new { success, requestId, message = success ? "Deployment retry triggered." : "Failed to trigger retry." });
    }

    /// <summary>
    /// Get recent workflow run status for both deploy workflows.
    /// </summary>
    [HttpGet("deploy/status")]
    public async Task<ActionResult> GetDeployStatus()
    {
        var owner = _configuration["GitHub:Owner"] ?? "";
        var repo = _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        var apiRuns = await _gitHubService.GetWorkflowRunsByNameAsync(owner, repo, "deploy-api.yml", 3);
        var webRuns = await _gitHubService.GetWorkflowRunsByNameAsync(owner, repo, "deploy-web.yml", 3);

        return Ok(new
        {
            deploymentMode = _configuration["PipelineOrchestrator:DeploymentMode"] ?? "Auto",
            api = apiRuns.Select(r => new { r.runId, r.status, r.conclusion, r.createdAt }),
            web = webRuns.Select(r => new { r.runId, r.status, r.conclusion, r.createdAt })
        });
    }

    /// <summary>
    /// Diagnostic: test PR detection for a specific request.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("test-pr-monitor/{requestId}")]
    public async Task<ActionResult> TestPrMonitor(int requestId)
    {
        var steps = new List<object>();
        try
        {
            var request = await _db.DevRequests
                .Include(r => r.Project)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null)
                return NotFound(new { error = $"Request #{requestId} not found" });

            steps.Add(new
            {
                step = "load_request",
                ok = true,
                title = request.Title,
                status = request.Status.ToString(),
                copilotStatus = request.CopilotStatus.ToString(),
                copilotSessionId = request.CopilotSessionId,
                copilotPrNumber = request.CopilotPrNumber,
                gitHubIssueNumber = request.GitHubIssueNumber,
                copilotTriggeredAt = request.CopilotTriggeredAt
            });

            var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
            var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

            steps.Add(new { step = "github_config", ok = true, owner, repo });

            if (request.GitHubIssueNumber == null)
            {
                steps.Add(new { step = "error", ok = false, error = "No GitHubIssueNumber on request" });
                return Ok(new { success = false, steps });
            }

            // Try to find PR
            var pr = await _gitHubService.FindPrByIssueAndAuthorAsync(owner, repo, request.GitHubIssueNumber.Value);
            if (pr != null)
            {
                steps.Add(new
                {
                    step = "find_pr",
                    ok = true,
                    prNumber = pr.Number,
                    prTitle = pr.Title,
                    prAuthor = pr.User?.Login,
                    prState = pr.State.ToString(),
                    prMerged = pr.Merged,
                    prBranch = pr.Head?.Ref,
                    prUrl = pr.HtmlUrl
                });
            }
            else
            {
                steps.Add(new { step = "find_pr", ok = false, error = "No matching PR found" });
            }

            return Ok(new { success = pr != null, steps });
        }
        catch (Exception ex)
        {
            steps.Add(new { step = "error", ok = false, error = $"{ex.GetType().Name}: {ex.Message}" });
            return Ok(new { success = false, steps });
        }
    }
}
