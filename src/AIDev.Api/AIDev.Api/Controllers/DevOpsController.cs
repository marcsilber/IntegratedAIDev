using AIDev.Api.Auth;
using AIDev.Api.Data;
using AIDev.Api.Models;
using AIDev.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Controllers;

/// <summary>
/// Secure operational endpoints for AI agents and dev tooling.
/// Requires a valid X-Dev-Key header. Does not require Entra ID auth.
/// 
/// Usage from PowerShell / curl:
///   $headers = @{ "X-Dev-Key" = "your-key-here" }
///   Invoke-RestMethod -Uri "https://aidev-api.azurewebsites.net/api/devops/requests" -Headers $headers
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous]
[DevApiKey]
public class DevOpsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<DevOpsController> _logger;
    private readonly IGitHubService _gitHubService;

    public DevOpsController(
        AppDbContext db,
        IConfiguration configuration,
        ILogger<DevOpsController> logger,
        IGitHubService gitHubService)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
        _gitHubService = gitHubService;
    }

    // ── Query Endpoints ──────────────────────────────────────────────────

    /// <summary>
    /// List all requests with optional filtering.
    /// Query params: status, copilotStatus, issueNumber, projectId, limit (default 50)
    /// </summary>
    [HttpGet("requests")]
    public async Task<ActionResult> GetRequests(
        [FromQuery] string? status = null,
        [FromQuery] string? copilotStatus = null,
        [FromQuery] int? issueNumber = null,
        [FromQuery] int? projectId = null,
        [FromQuery] int limit = 50)
    {
        var query = _db.DevRequests
            .Include(r => r.Project)
            .AsQueryable();

        if (!string.IsNullOrEmpty(status) && Enum.TryParse<RequestStatus>(status, true, out var rs))
            query = query.Where(r => r.Status == rs);

        if (!string.IsNullOrEmpty(copilotStatus) && Enum.TryParse<CopilotImplementationStatus>(copilotStatus, true, out var cs))
            query = query.Where(r => r.CopilotStatus == cs);

        if (issueNumber.HasValue)
            query = query.Where(r => r.GitHubIssueNumber == issueNumber.Value);

        if (projectId.HasValue)
            query = query.Where(r => r.ProjectId == projectId.Value);

        var requests = await query
            .OrderByDescending(r => r.UpdatedAt)
            .Take(Math.Min(limit, 200))
            .Select(r => new
            {
                r.Id,
                r.Title,
                r.Status,
                r.CopilotStatus,
                r.GitHubIssueNumber,
                r.CopilotPrNumber,
                r.CopilotPrUrl,
                r.CopilotBranchName,
                r.CopilotTriggeredAt,
                r.CopilotCompletedAt,
                r.DeploymentStatus,
                r.DeploymentRetryCount,
                ProjectName = r.Project != null ? r.Project.DisplayName : null,
                r.ArchitectReviewCount,
                r.CreatedAt,
                r.UpdatedAt
            })
            .ToListAsync();

        return Ok(new { count = requests.Count, requests });
    }

    /// <summary>
    /// Get full detail for a single request, including related reviews and comments.
    /// </summary>
    [HttpGet("request/{requestId}")]
    public async Task<ActionResult> GetRequest(int requestId)
    {
        var request = await _db.DevRequests
            .Include(r => r.Project)
            .Include(r => r.AgentReviews)
            .Include(r => r.ArchitectReviews)
            .Include(r => r.CodeReviews)
            .Include(r => r.Comments)
            .Include(r => r.Attachments)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new { error = $"Request #{requestId} not found" });

        return Ok(new
        {
            request.Id,
            request.Title,
            request.Description,
            request.RequestType,
            request.Priority,
            request.Status,
            request.GitHubIssueNumber,
            request.GitHubIssueUrl,
            request.CopilotStatus,
            request.CopilotPrNumber,
            request.CopilotPrUrl,
            request.CopilotBranchName,
            request.CopilotSessionId,
            request.CopilotTriggeredAt,
            request.CopilotCompletedAt,
            request.DeploymentStatus,
            request.DeploymentRetryCount,
            request.ArchitectReviewCount,
            ProjectName = request.Project?.DisplayName,
            request.CreatedAt,
            request.UpdatedAt,
            agentReviews = request.AgentReviews.Select(r => new
            {
                r.Id, r.Decision, r.Reasoning, r.AlignmentScore, r.CompletenessScore, r.CreatedAt
            }),
            architectReviews = request.ArchitectReviews.Select(r => new
            {
                r.Id, r.Decision, r.SolutionSummary, r.EstimatedComplexity, r.EstimatedEffort,
                r.FilesAnalysed, r.ModelUsed, r.CreatedAt, r.ApprovedAt
            }),
            codeReviews = request.CodeReviews.Select(r => new
            {
                r.Id, r.PrNumber, r.Decision, r.Summary, r.QualityScore,
                r.DesignCompliance, r.SecurityPass, r.CodingStandardsPass, r.CreatedAt
            }),
            recentComments = request.Comments
                .OrderByDescending(c => c.CreatedAt)
                .Take(10)
                .Select(c => new { c.Id, c.Author, c.IsAgentComment, c.Content, c.CreatedAt }),
            attachments = request.Attachments?.Select(a => new
            {
                a.Id, a.FileName, a.ContentType, a.FileSizeBytes
            })
        });
    }

    // ── Action Endpoints ─────────────────────────────────────────────────

    /// <summary>
    /// Reset a request's implementation state back to Approved so it can be re-triggered.
    /// </summary>
    [HttpPost("reset-implementation/{requestId}")]
    public async Task<ActionResult> ResetImplementation(int requestId, [FromQuery] string? reason = null)
    {
        var request = await _db.DevRequests
            .Include(r => r.Project)
            .FirstOrDefaultAsync(r => r.Id == requestId);

        if (request == null)
            return NotFound(new { error = $"Request #{requestId} not found" });

        var previousStatus = request.Status;
        var previousCopilotStatus = request.CopilotStatus;
        var previousPr = request.CopilotPrNumber;

        // Reset Copilot fields
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
            var effectiveReason = reason ?? "Reset via DevOps API";
            var prRef = previousPr.HasValue ? $" (PR #{previousPr.Value})" : "";
            await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber.Value,
                $"🔙 **Implementation reset.**{prRef}\n\n" +
                $"**Reason:** {effectiveReason}\n\n" +
                "Status has been reset to Approved. Will be re-triggered automatically.");

            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "copilot:complete");
            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "copilot:failed");
            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "copilot:pr-ready");
            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "review:changes-requested");
            await _gitHubService.RemoveLabelAsync(owner, repo, request.GitHubIssueNumber.Value, "review:approved");
            await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber.Value,
                "agent:approved-solution", "10b981");
        }

        _logger.LogInformation(
            "DevOps: Reset implementation for request #{RequestId} (was {Status}/{CopilotStatus}, PR #{Pr}). Reason: {Reason}",
            requestId, previousStatus, previousCopilotStatus, previousPr, reason ?? "none");

        return Ok(new
        {
            message = $"Request #{requestId} reset to Approved",
            previousStatus = previousStatus.ToString(),
            previousCopilotStatus = previousCopilotStatus?.ToString(),
            previousPrNumber = previousPr
        });
    }

    /// <summary>
    /// Change a request's status directly (for operational recovery).
    /// </summary>
    [HttpPost("set-status/{requestId}")]
    public async Task<ActionResult> SetStatus(int requestId, [FromQuery] string status, [FromQuery] string? copilotStatus = null)
    {
        var request = await _db.DevRequests.FindAsync(requestId);
        if (request == null)
            return NotFound(new { error = $"Request #{requestId} not found" });

        if (!Enum.TryParse<RequestStatus>(status, true, out var newStatus))
            return BadRequest(new { error = $"Invalid status '{status}'. Valid: {string.Join(", ", Enum.GetNames<RequestStatus>())}" });

        var previousStatus = request.Status;
        request.Status = newStatus;

        if (!string.IsNullOrEmpty(copilotStatus))
        {
            if (Enum.TryParse<CopilotImplementationStatus>(copilotStatus, true, out var newCopilotStatus))
                request.CopilotStatus = newCopilotStatus;
            else
                return BadRequest(new { error = $"Invalid copilotStatus '{copilotStatus}'. Valid: {string.Join(", ", Enum.GetNames<CopilotImplementationStatus>())}" });
        }

        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("DevOps: Changed request #{RequestId} status from {Old} to {New}", requestId, previousStatus, newStatus);

        return Ok(new
        {
            message = $"Request #{requestId} status updated",
            previousStatus = previousStatus.ToString(),
            newStatus = newStatus.ToString(),
            newCopilotStatus = request.CopilotStatus?.ToString()
        });
    }

    // ── System Health ────────────────────────────────────────────────────

    /// <summary>
    /// Re-trigger architect review by resetting LastArchitectReviewAt so the
    /// background service picks up existing human feedback again.
    /// </summary>
    [HttpPost("retrigger-architect/{requestId}")]
    public async Task<ActionResult> RetriggerArchitect(int requestId, [FromQuery] string? reason = null)
    {
        var request = await _db.DevRequests
            .Include(r => r.Comments)
            .FirstOrDefaultAsync(r => r.Id == requestId);
        if (request == null)
            return NotFound(new { error = $"Request #{requestId} not found" });

        if (request.Status != RequestStatus.ArchitectReview)
            return BadRequest(new { error = $"Request #{requestId} is in '{request.Status}', not ArchitectReview" });

        var maxReviews = int.Parse(_configuration["ArchitectAgent:MaxReviewsPerRequest"] ?? "3");
        if (request.ArchitectReviewCount >= maxReviews)
            return BadRequest(new { error = $"Request #{requestId} has reached max reviews ({maxReviews})" });

        // Find the latest human comment
        var lastHumanComment = request.Comments
            .Where(c => !c.IsAgentComment)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault();

        if (lastHumanComment == null)
            return BadRequest(new { error = "No human comments found to re-process" });

        // Reset LastArchitectReviewAt to just before the human comment
        var previousReviewAt = request.LastArchitectReviewAt;
        request.LastArchitectReviewAt = lastHumanComment.CreatedAt.AddSeconds(-1);
        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "DevOps: Re-triggered architect for request #{RequestId}. Reason: {Reason}. " +
            "Reset LastArchitectReviewAt from {Old} to {New}",
            requestId, reason ?? "manual", previousReviewAt, request.LastArchitectReviewAt);

        return Ok(new
        {
            message = $"Architect re-triggered for request #{requestId}",
            reason = reason ?? "manual",
            lastHumanComment = lastHumanComment.Content[..Math.Min(200, lastHumanComment.Content.Length)],
            lastArchitectReviewAt = request.LastArchitectReviewAt,
            architectReviewCount = request.ArchitectReviewCount,
            maxReviews,
            note = "The background agent will pick this up on its next polling cycle"
        });
    }

    /// <summary>
    /// Full system health check with DB stats and service configuration.
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult> Health()
    {
        var totalRequests = await _db.DevRequests.CountAsync();
        var statusCounts = await _db.DevRequests
            .GroupBy(r => r.Status)
            .Select(g => new { Status = g.Key.ToString(), Count = g.Count() })
            .ToListAsync();

        var copilotStatusCounts = await _db.DevRequests
            .Where(r => r.CopilotStatus != null)
            .GroupBy(r => r.CopilotStatus)
            .Select(g => new { Status = g.Key!.Value.ToString(), Count = g.Count() })
            .ToListAsync();

        var recentReviews = await _db.ArchitectReviews
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .Select(r => new { r.Id, r.DevRequestId, r.Decision, r.ModelUsed, r.CreatedAt })
            .ToListAsync();

        var recentCodeReviews = await _db.CodeReviews
            .OrderByDescending(r => r.CreatedAt)
            .Take(3)
            .Select(r => new { r.Id, r.DevRequestId, r.PrNumber, r.Decision, r.QualityScore, r.CreatedAt })
            .ToListAsync();

        return Ok(new
        {
            timestamp = DateTime.UtcNow,
            database = new
            {
                totalRequests,
                byStatus = statusCounts,
                byCopilotStatus = copilotStatusCounts
            },
            recentArchitectReviews = recentReviews,
            recentCodeReviews,
            config = new
            {
                architectEnabled = _configuration["ArchitectAgent:Enabled"],
                copilotEnabled = _configuration["CopilotImplementation:Enabled"],
                copilotModel = _configuration["CopilotImplementation:Model"],
                codeReviewEnabled = _configuration["CodeReviewAgent:Enabled"],
                autoMerge = _configuration["CodeReviewAgent:AutoMerge"],
                deploymentMode = _configuration["PipelineOrchestrator:DeploymentMode"],
                llmModel = _configuration["GitHubModels:ModelName"]
            }
        });
    }

    /// <summary>
    /// Execute a raw SQL SELECT query against the database (read-only).
    /// Only SELECT statements are allowed for safety.
    /// </summary>
    [HttpPost("query")]
    public async Task<ActionResult> ExecuteQuery([FromBody] DevOpsQueryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Sql))
            return BadRequest(new { error = "SQL query is required" });

        var sql = request.Sql.Trim();

        // Only allow SELECT statements
        if (!sql.StartsWith("SELECT", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { error = "Only SELECT queries are allowed" });

        // Block dangerous keywords
        var blocked = new[] { "DROP", "DELETE", "UPDATE", "INSERT", "ALTER", "CREATE", "EXEC", "TRUNCATE", "--", "/*" };
        foreach (var keyword in blocked)
        {
            if (sql.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return BadRequest(new { error = $"Query contains blocked keyword: {keyword}" });
        }

        try
        {
            var connection = _db.Database.GetDbConnection();
            await connection.OpenAsync();

            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.CommandTimeout = 10; // 10 second timeout

            using var reader = await command.ExecuteReaderAsync();
            var results = new List<Dictionary<string, object?>>();
            var columns = Enumerable.Range(0, reader.FieldCount)
                .Select(i => reader.GetName(i))
                .ToList();

            var rowCount = 0;
            while (await reader.ReadAsync() && rowCount < (request.MaxRows ?? 100))
            {
                var row = new Dictionary<string, object?>();
                foreach (var col in columns)
                {
                    var value = reader[col];
                    row[col] = value == DBNull.Value ? null : value;
                }
                results.Add(row);
                rowCount++;
            }

            return Ok(new
            {
                columns,
                rowCount = results.Count,
                truncated = rowCount >= (request.MaxRows ?? 100),
                rows = results
            });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Query failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Diagnostic: check what architect candidates the background service would find.
    /// </summary>
    [HttpGet("architect-candidates")]
    public async Task<ActionResult> GetArchitectCandidates()
    {
        var maxReviews = int.Parse(_configuration["ArchitectAgent:MaxReviewsPerRequest"] ?? "3");
        var batchSize = int.Parse(_configuration["ArchitectAgent:BatchSize"] ?? "3");

        var candidates = await _db.DevRequests
            .Include(r => r.Comments)
            .Where(r =>
                (r.Status == RequestStatus.Triaged && r.ArchitectReviewCount == 0)
                ||
                (r.Status == RequestStatus.ArchitectReview
                 && r.ArchitectReviewCount < maxReviews
                 && r.Comments.Any(c => !c.IsAgentComment
                     && c.CreatedAt > (r.LastArchitectReviewAt ?? DateTime.MinValue)))
            )
            .OrderBy(r => r.CreatedAt)
            .Take(batchSize)
            .Select(r => new
            {
                r.Id,
                r.Title,
                Status = r.Status.ToString(),
                r.ArchitectReviewCount,
                r.LastArchitectReviewAt,
                HumanComments = r.Comments
                    .Where(c => !c.IsAgentComment)
                    .Select(c => new { c.Id, c.CreatedAt, ContentPreview = c.Content.Substring(0, Math.Min(100, c.Content.Length)) })
                    .ToList()
            })
            .ToListAsync();

        return Ok(new
        {
            maxReviews,
            batchSize,
            candidateCount = candidates.Count,
            candidates
        });
    }

    /// <summary>
    /// Diagnostic: run the architect analysis for a specific request and return the result or error.
    /// This calls the same code path as the background service.
    /// </summary>
    [HttpPost("test-architect/{requestId}")]
    public async Task<ActionResult> TestArchitect(
        int requestId,
        [FromServices] IArchitectLlmService architectLlmService,
        [FromServices] ICodebaseService codebaseService)
    {
        try
        {
            var request = await _db.DevRequests
                .Include(r => r.Comments)
                .Include(r => r.Project)
                .Include(r => r.AgentReviews)
                .Include(r => r.ArchitectReviews)
                .Include(r => r.Attachments)
                .FirstOrDefaultAsync(r => r.Id == requestId);

            if (request == null)
                return NotFound(new { error = $"Request #{requestId} not found" });

            var poReview = request.AgentReviews
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();
            if (poReview == null)
                return BadRequest(new { error = "No PO review found" });

            if (request.Project == null)
                return BadRequest(new { error = "No project linked" });

            var owner = request.Project.GitHubOwner;
            var repo = request.Project.GitHubRepo;
            var repoMap = await codebaseService.GetRepositoryMapAsync(owner, repo);

            async Task<Dictionary<string, string>> FileReader(IEnumerable<string> files)
                => await codebaseService.GetFileContentsAsync(owner, repo, files);

            var conversationHistory = request.ArchitectReviewCount > 0
                ? request.Comments
                    .Where(c => c.ArchitectReviewId != null
                        || (!c.IsAgentComment && c.CreatedAt > (request.LastArchitectReviewAt ?? DateTime.MinValue)))
                    .OrderBy(c => c.CreatedAt)
                    .ToList()
                : null;

            var result = await architectLlmService.AnalyseRequestAsync(
                request, poReview, repoMap, FileReader, conversationHistory,
                request.Attachments?.ToList());

            return Ok(new
            {
                message = "Architect test completed successfully",
                solutionSummary = result.SolutionSummary,
                approach = result.Approach?.Substring(0, Math.Min(500, result.Approach?.Length ?? 0)),
                feedbackResponse = result.FeedbackResponse,
                estimatedComplexity = result.EstimatedComplexity,
                filesRead = result.FilesRead.Count,
                step1Tokens = result.Step1PromptTokens + result.Step1CompletionTokens,
                step2Tokens = result.Step2PromptTokens + result.Step2CompletionTokens,
                modelUsed = result.ModelUsed,
                durationMs = result.TotalDurationMs
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DevOps: test-architect failed for request #{RequestId}", requestId);
            return StatusCode(500, new
            {
                error = "Architect test failed",
                exceptionType = ex.GetType().Name,
                message = ex.Message,
                innerException = ex.InnerException?.Message,
                stackTrace = ex.StackTrace?[..Math.Min(1000, ex.StackTrace?.Length ?? 0)]
            });
        }
    }
}

/// <summary>
/// Request body for the DevOps SQL query endpoint.
/// </summary>
public record DevOpsQueryRequest(string Sql, int? MaxRows = 100);
