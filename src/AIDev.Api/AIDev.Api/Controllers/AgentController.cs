using AIDev.Api.Data;
using AIDev.Api.Models;
using AIDev.Api.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentController> _logger;

    public AgentController(AppDbContext db, IConfiguration configuration, ILogger<AgentController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// Get all agent reviews, optionally filtered by request ID or decision.
    /// </summary>
    [HttpGet("reviews")]
    public async Task<ActionResult<List<AgentReviewResponseDto>>> GetReviews(
        [FromQuery] int? requestId = null,
        [FromQuery] AgentDecision? decision = null)
    {
        var query = _db.AgentReviews
            .Include(r => r.DevRequest)
            .AsQueryable();

        if (requestId.HasValue)
            query = query.Where(r => r.DevRequestId == requestId.Value);

        if (decision.HasValue)
            query = query.Where(r => r.Decision == decision.Value);

        var reviews = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => MapToDto(r))
            .ToListAsync();

        return Ok(reviews);
    }

    /// <summary>
    /// Get a single agent review by ID.
    /// </summary>
    [HttpGet("reviews/{id}")]
    public async Task<ActionResult<AgentReviewResponseDto>> GetReview(int id)
    {
        var review = await _db.AgentReviews
            .Include(r => r.DevRequest)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review == null)
            return NotFound();

        return Ok(MapToDto(review));
    }

    /// <summary>
    /// Override an agent decision — sets the request to a new status manually.
    /// </summary>
    [HttpPost("reviews/{id}/override")]
    public async Task<ActionResult<AgentReviewResponseDto>> OverrideReview(int id, [FromBody] AgentOverrideDto dto)
    {
        var review = await _db.AgentReviews
            .Include(r => r.DevRequest)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (review == null)
            return NotFound();

        var request = review.DevRequest!;

        // Update request status
        request.Status = dto.NewStatus;
        request.UpdatedAt = DateTime.UtcNow;

        // Add a human override comment
        var author = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Admin";
        var comment = new RequestComment
        {
            DevRequestId = request.Id,
            Author = author,
            Content = $"**Manual Override** — Agent decision ({review.Decision}) overridden to **{dto.NewStatus}**." +
                     (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $"\nReason: {dto.Reason}"),
            IsAgentComment = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.RequestComments.Add(comment);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Agent review #{ReviewId} overridden by {User}: {OldDecision} → {NewStatus}",
            id, author, review.Decision, dto.NewStatus);

        return Ok(MapToDto(review));
    }

    /// <summary>
    /// Get aggregate agent statistics.
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<AgentStatsDto>> GetStats()
    {
        var reviews = await _db.AgentReviews.ToListAsync();

        var stats = new AgentStatsDto
        {
            TotalReviews = reviews.Count,
            ByDecision = Enum.GetValues<AgentDecision>()
                .ToDictionary(d => d.ToString(), d => reviews.Count(r => r.Decision == d)),
            AverageAlignmentScore = reviews.Count > 0
                ? Math.Round(reviews.Average(r => r.AlignmentScore), 1)
                : 0,
            AverageCompletenessScore = reviews.Count > 0
                ? Math.Round(reviews.Average(r => r.CompletenessScore), 1)
                : 0,
            AverageSalesAlignmentScore = reviews.Count > 0
                ? Math.Round(reviews.Average(r => r.SalesAlignmentScore), 1)
                : 0,
            TotalTokensUsed = reviews.Sum(r => r.PromptTokens + r.CompletionTokens),
            AverageDurationMs = reviews.Count > 0
                ? (int)Math.Round(reviews.Average(r => r.DurationMs))
                : 0
        };

        return Ok(stats);
    }

    /// <summary>
    /// Get the current agent configuration.
    /// </summary>
    [HttpGet("config")]
    public ActionResult<AgentConfigDto> GetConfig()
    {
        return Ok(new AgentConfigDto
        {
            Enabled = bool.Parse(_configuration["ProductOwnerAgent:Enabled"] ?? "true"),
            PollingIntervalSeconds = int.Parse(_configuration["ProductOwnerAgent:PollingIntervalSeconds"] ?? "30"),
            MaxReviewsPerRequest = int.Parse(_configuration["ProductOwnerAgent:MaxReviewsPerRequest"] ?? "3"),
            Temperature = float.Parse(_configuration["ProductOwnerAgent:Temperature"] ?? "0.3"),
            ModelName = _configuration["GitHubModels:ModelName"] ?? "gpt-4o"
        });
    }

    private static AgentReviewResponseDto MapToDto(AgentReview r) => new()
    {
        Id = r.Id,
        DevRequestId = r.DevRequestId,
        RequestTitle = r.DevRequest?.Title ?? "",
        AgentType = r.AgentType,
        Decision = r.Decision,
        Reasoning = r.Reasoning,
        AlignmentScore = r.AlignmentScore,
        CompletenessScore = r.CompletenessScore,
        SalesAlignmentScore = r.SalesAlignmentScore,
        SuggestedPriority = r.SuggestedPriority,
        Tags = r.Tags,
        PromptTokens = r.PromptTokens,
        CompletionTokens = r.CompletionTokens,
        ModelUsed = r.ModelUsed,
        DurationMs = r.DurationMs,
        CreatedAt = r.CreatedAt
    };
}
