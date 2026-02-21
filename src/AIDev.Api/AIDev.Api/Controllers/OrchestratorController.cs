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

    public OrchestratorController(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<OrchestratorController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
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
