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

    public OrchestratorController(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<OrchestratorController> logger,
        ICodebaseService codebaseService,
        ILlmClientFactory llmClientFactory)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
        _codebaseService = codebaseService;
        _llmClientFactory = llmClientFactory;
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
        var totalRequests = await _db.DevRequests.CountAsync();
        var totalArchReviews = await _db.ArchitectReviews.CountAsync();

        return Ok(new
        {
            status = "ok",
            timestamp = DateTime.UtcNow,
            architectAgentEnabled = bool.Parse(_configuration["ArchitectAgent:Enabled"] ?? "true"),
            requests = new { total = totalRequests, triaged = triagedCount, architectReview = architectReviewCount, approved = approvedCount },
            architectReviews = totalArchReviews
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
            BranchName = r.CopilotBranchName
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
            FailedStaleHours = int.Parse(_configuration["PipelineOrchestrator:FailedStaleHours"] ?? "24")
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

        return GetConfig();
    }
}
