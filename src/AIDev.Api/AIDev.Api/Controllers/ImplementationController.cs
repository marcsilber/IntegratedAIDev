using AIDev.Api.Data;
using AIDev.Api.Models;
using AIDev.Api.Models.DTOs;
using AIDev.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ImplementationController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGitHubService _gitHubService;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ImplementationController> _logger;

    public ImplementationController(
        AppDbContext db,
        IGitHubService gitHubService,
        IConfiguration configuration,
        IServiceProvider serviceProvider,
        ILogger<ImplementationController> logger)
    {
        _db = db;
        _gitHubService = gitHubService;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    // ── Trigger endpoints ─────────────────────────────────────────────────

    /// <summary>
    /// Manually trigger Copilot Coding Agent for an approved request.
    /// </summary>
    [HttpPost("trigger/{requestId}")]
    public async Task<ActionResult<ImplementationTriggerResponseDto>> TriggerImplementation(
        int requestId, [FromBody] ImplementationTriggerDto? dto = null)
    {
        var request = await _db.DevRequests
            .Include(r => r.ArchitectReviews)
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound($"Request #{requestId} not found");

        if (request.Status != RequestStatus.Approved)
            return BadRequest($"Request #{requestId} is not in Approved status (current: {request.Status})");

        if (request.GitHubIssueNumber == null)
            return BadRequest($"Request #{requestId} has no GitHub Issue linked");

        if (request.CopilotSessionId != null && request.CopilotStatus != CopilotImplementationStatus.Failed)
            return BadRequest($"Request #{requestId} already has an active Copilot session");

        try
        {
            // Get the trigger service to reuse its BuildCustomInstructions logic
            var triggerService = _serviceProvider.GetServices<IHostedService>()
                .OfType<ImplementationTriggerService>()
                .FirstOrDefault();

            if (triggerService == null)
                return StatusCode(500, "ImplementationTriggerService is not registered");

            await triggerService.TriggerCopilotCodingAgent(request, _db, HttpContext.RequestAborted);

            return Ok(new ImplementationTriggerResponseDto
            {
                RequestId = request.Id,
                IssueNumber = request.GitHubIssueNumber,
                CopilotStatus = request.CopilotStatus ?? CopilotImplementationStatus.Pending,
                TriggeredAt = request.CopilotTriggeredAt ?? DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to trigger Copilot for request #{Id}", requestId);
            return StatusCode(500, $"Failed to trigger Copilot: {ex.Message}");
        }
    }

    /// <summary>
    /// Reject a completed implementation, resetting the request to Approved status
    /// so it can be re-triggered. Does not immediately re-trigger Copilot.
    /// </summary>
    [HttpPost("reject/{requestId}")]
    public async Task<ActionResult> RejectImplementation(int requestId, [FromBody] RejectImplementationDto? dto = null)
    {
        var request = await _db.DevRequests
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound($"Request #{requestId} not found");

        // Allow rejecting requests that are Done, PrMerged, or Failed
        if (request.Status != RequestStatus.Done
            && request.Status != RequestStatus.InProgress
            && request.CopilotStatus != CopilotImplementationStatus.PrMerged
            && request.CopilotStatus != CopilotImplementationStatus.Failed)
        {
            return BadRequest($"Request #{requestId} cannot be rejected (Status: {request.Status}, CopilotStatus: {request.CopilotStatus})");
        }

        var previousPr = request.CopilotPrNumber;
        var reason = dto?.Reason ?? "Implementation rejected by user";

        // Reset Copilot fields — back to Approved so it can be re-triggered
        request.CopilotSessionId = null;
        request.CopilotPrNumber = null;
        request.CopilotPrUrl = null;
        request.CopilotStatus = null;
        request.CopilotTriggeredAt = null;
        request.CopilotCompletedAt = null;
        request.CopilotBranchName = null;
        request.BranchDeleted = false;
        request.DeploymentStatus = DeploymentStatus.None;
        request.DeployedAt = null;
        request.DeploymentRetryCount = 0;
        request.Status = RequestStatus.Approved;
        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Post comment on GitHub Issue
        var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
        var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        if (request.GitHubIssueNumber.HasValue)
        {
            var prRef = previousPr.HasValue ? $" (PR #{previousPr.Value})" : "";
            await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                $"🔙 **Implementation rejected.**{prRef}\n\n" +
                $"**Reason:** {reason}\n\n" +
                "Status has been reset to Approved. The request can be re-triggered from the dashboard.");

            // Update labels
            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "copilot:complete");
            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "copilot:failed");
            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "deployed:uat");
            await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                "agent:approved-solution", "10b981");
        }

        _logger.LogInformation(
            "Implementation rejected for request #{RequestId} — reset to Approved. Reason: {Reason}",
            requestId, reason);

        return Ok(new { message = $"Request #{requestId} reset to Approved", reason });
    }

    /// <summary>
    /// Re-trigger Copilot for a request that previously failed.
    /// </summary>
    [HttpPost("re-trigger/{requestId}")]
    public async Task<ActionResult<ImplementationTriggerResponseDto>> ReTriggerImplementation(int requestId)
    {
        var request = await _db.DevRequests
            .Include(r => r.ArchitectReviews)
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound($"Request #{requestId} not found");

        if (request.CopilotStatus != CopilotImplementationStatus.Failed)
            return BadRequest($"Request #{requestId} is not in Failed status (current: {request.CopilotStatus})");

        // Reset Copilot fields for re-trigger
        request.CopilotSessionId = null;
        request.CopilotPrNumber = null;
        request.CopilotPrUrl = null;
        request.CopilotStatus = null;
        request.CopilotTriggeredAt = null;
        request.CopilotCompletedAt = null;
        request.Status = RequestStatus.Approved; // Back to Approved so trigger service picks it up
        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        try
        {
            var triggerService = _serviceProvider.GetServices<IHostedService>()
                .OfType<ImplementationTriggerService>()
                .FirstOrDefault();

            if (triggerService == null)
                return StatusCode(500, "ImplementationTriggerService is not registered");

            await triggerService.TriggerCopilotCodingAgent(request, _db, HttpContext.RequestAborted);

            return Ok(new ImplementationTriggerResponseDto
            {
                RequestId = request.Id,
                IssueNumber = request.GitHubIssueNumber,
                CopilotStatus = request.CopilotStatus ?? CopilotImplementationStatus.Pending,
                TriggeredAt = request.CopilotTriggeredAt ?? DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to re-trigger Copilot for request #{Id}", requestId);
            return StatusCode(500, $"Failed to re-trigger Copilot: {ex.Message}");
        }
    }

    // ── Status endpoints ──────────────────────────────────────────────────

    /// <summary>
    /// Get implementation status for a specific request.
    /// </summary>
    [HttpGet("status/{requestId}")]
    public async Task<ActionResult<ImplementationStatusDto>> GetStatus(int requestId)
    {
        var request = await _db.DevRequests.FindAsync(requestId);
        if (request == null) return NotFound();

        return Ok(MapToStatusDto(request));
    }

    /// <summary>
    /// List all Copilot sessions (active and recent).
    /// </summary>
    [HttpGet("sessions")]
    public async Task<ActionResult<List<ImplementationStatusDto>>> GetSessions(
        [FromQuery] CopilotImplementationStatus? status = null)
    {
        var query = _db.DevRequests
            .Where(r => r.CopilotSessionId != null);

        if (status.HasValue)
            query = query.Where(r => r.CopilotStatus == status.Value);

        var requests = await query
            .OrderByDescending(r => r.CopilotTriggeredAt)
            .ToListAsync();

        return Ok(requests.Select(MapToStatusDto).ToList());
    }

    // ── Config endpoints ──────────────────────────────────────────────────

    /// <summary>
    /// Get current implementation configuration.
    /// </summary>
    [HttpGet("config")]
    public ActionResult<ImplementationConfigDto> GetConfig()
    {
        return Ok(new ImplementationConfigDto
        {
            Enabled = bool.Parse(_configuration["CopilotImplementation:Enabled"] ?? "true"),
            AutoTriggerOnApproval = bool.Parse(_configuration["CopilotImplementation:AutoTriggerOnApproval"] ?? "true"),
            PollingIntervalSeconds = int.Parse(_configuration["CopilotImplementation:PollingIntervalSeconds"] ?? "60"),
            PrPollIntervalSeconds = int.Parse(_configuration["CopilotImplementation:PrPollIntervalSeconds"] ?? "120"),
            MaxConcurrentSessions = int.Parse(_configuration["CopilotImplementation:MaxConcurrentSessions"] ?? "3"),
            BaseBranch = _configuration["CopilotImplementation:BaseBranch"] ?? "main",
            Model = _configuration["CopilotImplementation:Model"] ?? "",
            CustomAgent = _configuration["CopilotImplementation:CustomAgent"] ?? "",
            MaxRetries = int.Parse(_configuration["CopilotImplementation:MaxRetries"] ?? "2")
        });
    }

    /// <summary>
    /// Update implementation configuration.
    /// </summary>
    [HttpPut("config")]
    public ActionResult<ImplementationConfigDto> UpdateConfig([FromBody] ImplementationConfigUpdateDto update)
    {
        if (update.Enabled.HasValue)
            _configuration["CopilotImplementation:Enabled"] = update.Enabled.Value.ToString();
        if (update.AutoTriggerOnApproval.HasValue)
            _configuration["CopilotImplementation:AutoTriggerOnApproval"] = update.AutoTriggerOnApproval.Value.ToString();
        if (update.PollingIntervalSeconds.HasValue)
            _configuration["CopilotImplementation:PollingIntervalSeconds"] = update.PollingIntervalSeconds.Value.ToString();
        if (update.PrPollIntervalSeconds.HasValue)
            _configuration["CopilotImplementation:PrPollIntervalSeconds"] = update.PrPollIntervalSeconds.Value.ToString();
        if (update.MaxConcurrentSessions.HasValue)
            _configuration["CopilotImplementation:MaxConcurrentSessions"] = update.MaxConcurrentSessions.Value.ToString();
        if (update.BaseBranch != null)
            _configuration["CopilotImplementation:BaseBranch"] = update.BaseBranch;
        if (update.Model != null)
            _configuration["CopilotImplementation:Model"] = update.Model;
        if (update.MaxRetries.HasValue)
            _configuration["CopilotImplementation:MaxRetries"] = update.MaxRetries.Value.ToString();

        return GetConfig();
    }

    // ── Stats endpoint ────────────────────────────────────────────────────

    /// <summary>
    /// Get aggregate implementation statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<ImplementationStatsDto>> GetStats()
    {
        var sessions = await _db.DevRequests
            .Where(r => r.CopilotSessionId != null)
            .ToListAsync();

        var totalTriggered = sessions.Count;
        var merged = sessions.Count(s => s.CopilotStatus == CopilotImplementationStatus.PrMerged);
        var failed = sessions.Count(s => s.CopilotStatus == CopilotImplementationStatus.Failed);
        var completed = merged + failed;

        var completedSessions = sessions
            .Where(s => s.CopilotTriggeredAt != null && s.CopilotCompletedAt != null)
            .ToList();

        var avgMinutes = completedSessions.Count > 0
            ? completedSessions.Average(s => (s.CopilotCompletedAt!.Value - s.CopilotTriggeredAt!.Value).TotalMinutes)
            : 0;

        return Ok(new ImplementationStatsDto
        {
            TotalTriggered = totalTriggered,
            Pending = sessions.Count(s => s.CopilotStatus == CopilotImplementationStatus.Pending),
            Working = sessions.Count(s => s.CopilotStatus == CopilotImplementationStatus.Working),
            PrOpened = sessions.Count(s => s.CopilotStatus == CopilotImplementationStatus.PrOpened),
            PrMerged = merged,
            Failed = failed,
            SuccessRate = completed > 0 ? Math.Round((double)merged / completed * 100, 1) : 0,
            AverageCompletionMinutes = Math.Round(avgMinutes, 1),
            ActiveSessions = sessions.Count(s =>
                s.CopilotStatus == CopilotImplementationStatus.Pending
                || s.CopilotStatus == CopilotImplementationStatus.Working)
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static ImplementationStatusDto MapToStatusDto(DevRequest request)
    {
        double? elapsedMinutes = null;
        if (request.CopilotTriggeredAt.HasValue)
        {
            var endTime = request.CopilotCompletedAt ?? DateTime.UtcNow;
            elapsedMinutes = Math.Round((endTime - request.CopilotTriggeredAt.Value).TotalMinutes, 1);
        }

        return new ImplementationStatusDto
        {
            RequestId = request.Id,
            Title = request.Title,
            IssueNumber = request.GitHubIssueNumber,
            CopilotStatus = request.CopilotStatus,
            CopilotSessionId = request.CopilotSessionId,
            PrNumber = request.CopilotPrNumber,
            PrUrl = request.CopilotPrUrl,
            TriggeredAt = request.CopilotTriggeredAt,
            CompletedAt = request.CopilotCompletedAt,
            ElapsedMinutes = elapsedMinutes
        };
    }
}
