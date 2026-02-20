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
public class RequestsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGitHubService _gitHub;
    private readonly ILogger<RequestsController> _logger;

    public RequestsController(AppDbContext db, IGitHubService gitHub, ILogger<RequestsController> logger)
    {
        _db = db;
        _gitHub = gitHub;
        _logger = logger;
    }

    /// <summary>
    /// Get all requests with optional filtering.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<RequestResponseDto>>> GetAll(
        [FromQuery] RequestStatus? status = null,
        [FromQuery] RequestType? type = null,
        [FromQuery] Priority? priority = null,
        [FromQuery] string? search = null)
    {
        var query = _db.DevRequests
            .Include(r => r.Comments)
            .AsQueryable();

        if (status.HasValue)
            query = query.Where(r => r.Status == status.Value);

        if (type.HasValue)
            query = query.Where(r => r.RequestType == type.Value);

        if (priority.HasValue)
            query = query.Where(r => r.Priority == priority.Value);

        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(r => r.Title.Contains(search) || r.Description.Contains(search));

        var requests = await query
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => MapToDto(r))
            .ToListAsync();

        return Ok(requests);
    }

    /// <summary>
    /// Get a single request by ID.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<RequestResponseDto>> GetById(int id)
    {
        var request = await _db.DevRequests
            .Include(r => r.Comments)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
            return NotFound();

        return Ok(MapToDto(request));
    }

    /// <summary>
    /// Create a new request.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<RequestResponseDto>> Create([FromBody] CreateRequestDto dto)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var userName = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Unknown";
        var userEmail = User.FindFirst("preferred_username")?.Value ?? "unknown@example.com";

        var request = new DevRequest
        {
            Title = dto.Title,
            Description = dto.Description,
            RequestType = dto.RequestType,
            Priority = dto.Priority,
            StepsToReproduce = dto.StepsToReproduce,
            ExpectedBehavior = dto.ExpectedBehavior,
            ActualBehavior = dto.ActualBehavior,
            SubmittedBy = userName,
            SubmittedByEmail = userEmail,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.DevRequests.Add(request);
        await _db.SaveChangesAsync();

        // Sync to GitHub Issues (non-blocking failure — request still saved)
        try
        {
            var (issueNumber, issueUrl) = await _gitHub.CreateIssueAsync(request);
            if (issueNumber > 0)
            {
                request.GitHubIssueNumber = issueNumber;
                request.GitHubIssueUrl = issueUrl;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub Issue creation failed for request {RequestId}, continuing without sync.", request.Id);
        }

        return CreatedAtAction(nameof(GetById), new { id = request.Id }, MapToDto(request));
    }

    /// <summary>
    /// Update an existing request.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<RequestResponseDto>> Update(int id, [FromBody] UpdateRequestDto dto)
    {
        var request = await _db.DevRequests
            .Include(r => r.Comments)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (request == null)
            return NotFound();

        if (dto.Title != null) request.Title = dto.Title;
        if (dto.Description != null) request.Description = dto.Description;
        if (dto.RequestType.HasValue) request.RequestType = dto.RequestType.Value;
        if (dto.Priority.HasValue) request.Priority = dto.Priority.Value;
        if (dto.StepsToReproduce != null) request.StepsToReproduce = dto.StepsToReproduce;
        if (dto.ExpectedBehavior != null) request.ExpectedBehavior = dto.ExpectedBehavior;
        if (dto.ActualBehavior != null) request.ActualBehavior = dto.ActualBehavior;
        if (dto.Status.HasValue) request.Status = dto.Status.Value;

        request.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Sync update to GitHub
        try
        {
            await _gitHub.UpdateIssueAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GitHub Issue update failed for request {RequestId}.", request.Id);
        }

        return Ok(MapToDto(request));
    }

    /// <summary>
    /// Delete a request.
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> Delete(int id)
    {
        var request = await _db.DevRequests.FindAsync(id);
        if (request == null)
            return NotFound();

        _db.DevRequests.Remove(request);
        await _db.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Add a comment to a request.
    /// </summary>
    [HttpPost("{id}/comments")]
    public async Task<ActionResult<CommentResponseDto>> AddComment(int id, [FromBody] CreateCommentDto dto)
    {
        var request = await _db.DevRequests.FindAsync(id);
        if (request == null)
            return NotFound();

        var author = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Unknown";

        var comment = new RequestComment
        {
            DevRequestId = id,
            Author = author,
            Content = dto.Content,
            CreatedAt = DateTime.UtcNow
        };

        _db.RequestComments.Add(comment);
        await _db.SaveChangesAsync();

        return Ok(new CommentResponseDto
        {
            Id = comment.Id,
            Author = comment.Author,
            Content = comment.Content,
            CreatedAt = comment.CreatedAt
        });
    }

    private static RequestResponseDto MapToDto(DevRequest r) => new()
    {
        Id = r.Id,
        Title = r.Title,
        Description = r.Description,
        RequestType = r.RequestType,
        Priority = r.Priority,
        StepsToReproduce = r.StepsToReproduce,
        ExpectedBehavior = r.ExpectedBehavior,
        ActualBehavior = r.ActualBehavior,
        Status = r.Status,
        SubmittedBy = r.SubmittedBy,
        SubmittedByEmail = r.SubmittedByEmail,
        GitHubIssueNumber = r.GitHubIssueNumber,
        GitHubIssueUrl = r.GitHubIssueUrl,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        Comments = r.Comments.Select(c => new CommentResponseDto
        {
            Id = c.Id,
            Author = c.Author,
            Content = c.Content,
            CreatedAt = c.CreatedAt
        }).ToList()
    };
}
