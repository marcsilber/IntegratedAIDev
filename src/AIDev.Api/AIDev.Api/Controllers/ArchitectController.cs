using System.Text.Json;
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
public class ArchitectController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGitHubService _gitHubService;
    private readonly ICodebaseService _codebaseService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ArchitectController> _logger;
    private readonly IArchitectReferenceService _referenceService;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ArchitectController(
        AppDbContext db,
        IGitHubService gitHubService,
        ICodebaseService codebaseService,
        IConfiguration configuration,
        ILogger<ArchitectController> logger,
        IArchitectReferenceService referenceService)
    {
        _db = db;
        _gitHubService = gitHubService;
        _codebaseService = codebaseService;
        _configuration = configuration;
        _logger = logger;
        _referenceService = referenceService;
    }

    /// <summary>
    /// Get architect reference data including database schema, architecture overview, and design decisions.
    /// </summary>
    [HttpGet("reference")]
    public ActionResult<ArchitectReferenceDto> GetReference()
    {
        var data = _referenceService.GetReferenceData(_db);
        return Ok(data);
    }

    /// <summary>
    /// List architect reviews, optionally filtered by requestId or decision.
    /// </summary>
    [HttpGet("reviews")]
    public async Task<ActionResult<List<ArchitectReviewResponseDto>>> GetReviews(
        [FromQuery] int? requestId = null,
        [FromQuery] ArchitectDecision? decision = null)
    {
        var query = _db.ArchitectReviews
            .Include(r => r.DevRequest)
            .AsQueryable();

        if (requestId.HasValue)
            query = query.Where(r => r.DevRequestId == requestId.Value);

        if (decision.HasValue)
            query = query.Where(r => r.Decision == decision.Value);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .ToListAsync();

        return Ok(reviews.Select(MapToDto).ToList());
    }

    /// <summary>
    /// Get a single architect review with full solution JSON.
    /// </summary>
    [HttpGet("reviews/{id}")]
    public async Task<ActionResult<ArchitectReviewResponseDto>> GetReview(int id)
    {
        var review = await _db.ArchitectReviews
            .Include(r => r.DevRequest)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review == null)
            return NotFound();

        return Ok(MapToDto(review));
    }

    /// <summary>
    /// Approve a solution proposal. Sets request status to Approved.
    /// </summary>
    [HttpPost("reviews/{id}/approve")]
    public async Task<ActionResult<ArchitectReviewResponseDto>> ApproveReview(
        int id, [FromBody] ArchitectApprovalDto dto)
    {
        var review = await _db.ArchitectReviews
            .Include(r => r.DevRequest!)
                .ThenInclude(d => d.Project)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review == null)
            return NotFound();

        var author = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Admin";

        review.Decision = ArchitectDecision.Approved;
        review.ApprovedBy = author;
        review.ApprovedAt = DateTime.UtcNow;

        var request = review.DevRequest!;
        request.Status = RequestStatus.Approved;
        request.UpdatedAt = DateTime.UtcNow;

        var commentContent = $"**Solution Approved** by {author}" +
            (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $"\n\nReason: {dto.Reason}");

        _db.RequestComments.Add(new RequestComment
        {
            DevRequestId = request.Id,
            Author = author,
            Content = commentContent,
            IsAgentComment = false,
            ArchitectReviewId = review.Id,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Architect review #{ReviewId} approved by {User}", id, author);

        // Update GitHub Issue label
        if (request.GitHubIssueNumber.HasValue && request.Project != null)
        {
            await _gitHubService.AddLabelAsync(
                request.Project.GitHubOwner, request.Project.GitHubRepo,
                request.GitHubIssueNumber.Value, "agent:approved-solution", "0e8a16");
            await _gitHubService.PostAgentCommentAsync(
                request.Project.GitHubOwner, request.Project.GitHubRepo,
                request.GitHubIssueNumber.Value, commentContent);
        }

        return Ok(MapToDto(review));
    }

    /// <summary>
    /// Reject a solution proposal. Returns request to Triaged for re-analysis.
    /// </summary>
    [HttpPost("reviews/{id}/reject")]
    public async Task<ActionResult<ArchitectReviewResponseDto>> RejectReview(
        int id, [FromBody] ArchitectApprovalDto dto)
    {
        var review = await _db.ArchitectReviews
            .Include(r => r.DevRequest!)
                .ThenInclude(d => d.Project)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review == null)
            return NotFound();

        var author = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Admin";

        review.Decision = ArchitectDecision.Rejected;
        review.HumanFeedback = dto.Reason;

        var request = review.DevRequest!;
        request.Status = RequestStatus.Triaged; // Back to queue for re-analysis
        request.UpdatedAt = DateTime.UtcNow;

        var commentContent = $"**Solution Rejected** by {author}" +
            (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $"\n\nReason: {dto.Reason}");

        _db.RequestComments.Add(new RequestComment
        {
            DevRequestId = request.Id,
            Author = author,
            Content = commentContent,
            IsAgentComment = false,
            ArchitectReviewId = review.Id,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Architect review #{ReviewId} rejected by {User}: {Reason}", id, author, dto.Reason);

        // Update GitHub
        if (request.GitHubIssueNumber.HasValue && request.Project != null)
        {
            await _gitHubService.PostAgentCommentAsync(
                request.Project.GitHubOwner, request.Project.GitHubRepo,
                request.GitHubIssueNumber.Value, commentContent);
        }

        return Ok(MapToDto(review));
    }

    /// <summary>
    /// Post feedback on a proposal for the agent to revise.
    /// </summary>
    [HttpPost("reviews/{id}/feedback")]
    public async Task<ActionResult> PostFeedback(int id, [FromBody] ArchitectFeedbackDto dto)
    {
        var review = await _db.ArchitectReviews
            .Include(r => r.DevRequest!)
                .ThenInclude(d => d.Project)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review == null)
            return NotFound();

        var author = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Admin";

        // Mark current proposal as revised (agent will create a new one)
        review.Decision = ArchitectDecision.Revised;
        review.HumanFeedback = dto.Feedback;

        var request = review.DevRequest!;

        _db.RequestComments.Add(new RequestComment
        {
            DevRequestId = request.Id,
            Author = author,
            Content = $"**Feedback on Architect Proposal #{review.Id}:**\n\n{dto.Feedback}",
            IsAgentComment = false,
            ArchitectReviewId = review.Id,
            CreatedAt = DateTime.UtcNow
        });

        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Feedback posted on architect review #{ReviewId} by {User}", id, author);

        // Post to GitHub
        if (request.GitHubIssueNumber.HasValue && request.Project != null)
        {
            await _gitHubService.PostAgentCommentAsync(
                request.Project.GitHubOwner, request.Project.GitHubRepo,
                request.GitHubIssueNumber.Value,
                $"**Feedback on solution proposal:**\n\n{dto.Feedback}");
        }

        return Ok(new { message = "Feedback posted. The architect agent will revise the proposal on its next cycle." });
    }

    /// <summary>
    /// Trigger a fresh architecture analysis for a request.
    /// </summary>
    [HttpPost("reviews/re-analyse/{requestId}")]
    public async Task<ActionResult> TriggerReAnalysis(int requestId)
    {
        var request = await _db.DevRequests.FindAsync(requestId);
        if (request == null) return NotFound();

        request.Status = RequestStatus.Triaged;
        request.ArchitectReviewCount = 0;
        request.LastArchitectReviewAt = null;
        request.UpdatedAt = DateTime.UtcNow;

        var author = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Admin";
        _db.RequestComments.Add(new RequestComment
        {
            DevRequestId = request.Id,
            Author = author,
            Content = $"**Re-analysis triggered** — Request queued for fresh architecture review by {author}.",
            IsAgentComment = false,
            CreatedAt = DateTime.UtcNow
        });

        await _db.SaveChangesAsync();

        _logger.LogInformation("Re-analysis triggered for request #{RequestId} by {User}", requestId, author);
        return Ok(new { message = $"Request #{requestId} queued for re-analysis" });
    }

    /// <summary>
    /// Get current architect agent configuration.
    /// </summary>
    [HttpGet("config")]
    public ActionResult<ArchitectConfigDto> GetConfig()
    {
        return Ok(BuildConfigDto());
    }

    /// <summary>
    /// Update architect agent configuration at runtime.
    /// </summary>
    [HttpPut("config")]
    public ActionResult<ArchitectConfigDto> UpdateConfig([FromBody] ArchitectConfigUpdateDto dto)
    {
        if (dto.Enabled.HasValue)
            _configuration["ArchitectAgent:Enabled"] = dto.Enabled.Value.ToString();
        if (dto.PollingIntervalSeconds.HasValue)
            _configuration["ArchitectAgent:PollingIntervalSeconds"] = dto.PollingIntervalSeconds.Value.ToString();
        if (dto.MaxReviewsPerRequest.HasValue)
            _configuration["ArchitectAgent:MaxReviewsPerRequest"] = dto.MaxReviewsPerRequest.Value.ToString();
        if (dto.MaxFilesToRead.HasValue)
            _configuration["ArchitectAgent:MaxFilesToRead"] = dto.MaxFilesToRead.Value.ToString();
        if (dto.Temperature.HasValue)
            _configuration["ArchitectAgent:Temperature"] = dto.Temperature.Value.ToString();
        if (dto.DailyTokenBudget.HasValue)
            _configuration["ArchitectAgent:DailyTokenBudget"] = dto.DailyTokenBudget.Value.ToString();
        if (dto.MonthlyTokenBudget.HasValue)
            _configuration["ArchitectAgent:MonthlyTokenBudget"] = dto.MonthlyTokenBudget.Value.ToString();

        var author = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Admin";
        _logger.LogInformation("Architect config updated by {User}: {@Config}", author, dto);

        return Ok(BuildConfigDto());
    }

    /// <summary>
    /// Get architect agent token budget status.
    /// </summary>
    [HttpGet("budget")]
    public async Task<ActionResult<TokenBudgetDto>> GetBudget()
    {
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var reviews = await _db.ArchitectReviews.ToListAsync();

        var dailyTokens = reviews
            .Where(r => r.CreatedAt.Date == today)
            .Sum(r => r.Step1PromptTokens + r.Step1CompletionTokens + r.Step2PromptTokens + r.Step2CompletionTokens);

        var monthlyTokens = reviews
            .Where(r => r.CreatedAt >= monthStart)
            .Sum(r => r.Step1PromptTokens + r.Step1CompletionTokens + r.Step2PromptTokens + r.Step2CompletionTokens);

        var dailyBudget = int.TryParse(_configuration["ArchitectAgent:DailyTokenBudget"], out var db2) ? db2 : 0;
        var monthlyBudget = int.TryParse(_configuration["ArchitectAgent:MonthlyTokenBudget"], out var mb2) ? mb2 : 0;

        return Ok(new TokenBudgetDto
        {
            DailyTokensUsed = dailyTokens,
            DailyTokenBudget = dailyBudget,
            DailyBudgetExceeded = dailyBudget > 0 && dailyTokens >= dailyBudget,
            MonthlyTokensUsed = monthlyTokens,
            MonthlyTokenBudget = monthlyBudget,
            MonthlyBudgetExceeded = monthlyBudget > 0 && monthlyTokens >= monthlyBudget,
            DailyReviewCount = reviews.Count(r => r.CreatedAt.Date == today),
            MonthlyReviewCount = reviews.Count(r => r.CreatedAt >= monthStart)
        });
    }

    /// <summary>
    /// Get aggregate architect agent statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<ArchitectStatsDto>> GetStats()
    {
        var reviews = await _db.ArchitectReviews.ToListAsync();

        return Ok(new ArchitectStatsDto
        {
            TotalAnalyses = reviews.Count,
            PendingReview = reviews.Count(r => r.Decision == ArchitectDecision.Pending),
            Approved = reviews.Count(r => r.Decision == ArchitectDecision.Approved),
            Rejected = reviews.Count(r => r.Decision == ArchitectDecision.Rejected),
            Revised = reviews.Count(r => r.Decision == ArchitectDecision.Revised),
            AverageFilesAnalysed = reviews.Count > 0
                ? Math.Round(reviews.Average(r => r.FilesAnalysed), 1)
                : 0,
            TotalTokensUsed = reviews.Sum(r =>
                r.Step1PromptTokens + r.Step1CompletionTokens +
                r.Step2PromptTokens + r.Step2CompletionTokens),
            AverageDurationMs = reviews.Count > 0
                ? Math.Round(reviews.Average(r => r.TotalDurationMs), 0)
                : 0
        });
    }

    // ────────────────────────────────────────────────────────────
    // Helpers
    // ────────────────────────────────────────────────────────────

    private ArchitectConfigDto BuildConfigDto() => new()
    {
        Enabled = bool.Parse(_configuration["ArchitectAgent:Enabled"] ?? "true"),
        PollingIntervalSeconds = int.Parse(_configuration["ArchitectAgent:PollingIntervalSeconds"] ?? "60"),
        MaxReviewsPerRequest = int.Parse(_configuration["ArchitectAgent:MaxReviewsPerRequest"] ?? "3"),
        MaxFilesToRead = int.Parse(_configuration["ArchitectAgent:MaxFilesToRead"] ?? "20"),
        Temperature = float.Parse(_configuration["ArchitectAgent:Temperature"] ?? "0.2"),
        ModelName = _configuration["GitHubModels:ModelName"] ?? "gpt-4o",
        DailyTokenBudget = int.TryParse(_configuration["ArchitectAgent:DailyTokenBudget"], out var db2) ? db2 : 0,
        MonthlyTokenBudget = int.TryParse(_configuration["ArchitectAgent:MonthlyTokenBudget"], out var mb2) ? mb2 : 0
    };

    private static ArchitectReviewResponseDto MapToDto(ArchitectReview r)
    {
        var dto = new ArchitectReviewResponseDto
        {
            Id = r.Id,
            DevRequestId = r.DevRequestId,
            RequestTitle = r.DevRequest?.Title ?? "",
            SolutionSummary = r.SolutionSummary,
            Approach = r.Approach,
            EstimatedComplexity = r.EstimatedComplexity,
            EstimatedEffort = r.EstimatedEffort,
            Decision = r.Decision,
            HumanFeedback = r.HumanFeedback,
            ApprovedBy = r.ApprovedBy,
            ApprovedAt = r.ApprovedAt,
            FilesAnalysed = r.FilesAnalysed,
            TotalTokensUsed = r.Step1PromptTokens + r.Step1CompletionTokens
                + r.Step2PromptTokens + r.Step2CompletionTokens,
            ModelUsed = r.ModelUsed,
            TotalDurationMs = r.TotalDurationMs,
            CreatedAt = r.CreatedAt
        };

        // Parse solution JSON to populate structured fields
        if (!string.IsNullOrEmpty(r.SolutionJson))
        {
            try
            {
                var solution = JsonSerializer.Deserialize<SolutionJsonDto>(r.SolutionJson, JsonOptions);
                if (solution != null)
                {
                    dto.ImpactedFiles = solution.ImpactedFiles ?? new();
                    dto.NewFiles = solution.NewFiles ?? new();
                    dto.DataMigration = solution.DataMigration ?? new();
                    dto.BreakingChanges = solution.BreakingChanges ?? new();
                    dto.DependencyChanges = solution.DependencyChanges ?? new();
                    dto.Risks = solution.Risks ?? new();
                    dto.ImplementationOrder = solution.ImplementationOrder ?? new();
                    dto.TestingNotes = solution.TestingNotes ?? "";
                    dto.ArchitecturalNotes = solution.ArchitecturalNotes ?? "";
                }
            }
            catch (JsonException ex)
            {
                // Log but don't fail — the summary fields are still available
                System.Diagnostics.Debug.WriteLine($"Failed to parse SolutionJson: {ex.Message}");
            }
        }

        return dto;
    }

    /// <summary>Internal DTO for deserializing the SolutionJson blob.</summary>
    private class SolutionJsonDto
    {
        public List<ImpactedFileDto>? ImpactedFiles { get; set; }
        public List<NewFileDto>? NewFiles { get; set; }
        public DataMigrationDto? DataMigration { get; set; }
        public List<string>? BreakingChanges { get; set; }
        public List<DependencyChangeDto>? DependencyChanges { get; set; }
        public List<RiskDto>? Risks { get; set; }
        public List<string>? ImplementationOrder { get; set; }
        public string? TestingNotes { get; set; }
        public string? ArchitecturalNotes { get; set; }
    }
}
