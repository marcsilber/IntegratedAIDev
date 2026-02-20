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
            .Include(r => r.Project)
            .Include(r => r.Attachments)
            .Include(r => r.AgentReviews)
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
            .Include(r => r.Project)
            .Include(r => r.Attachments)
            .Include(r => r.AgentReviews)
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

        var project = await _db.Projects.FindAsync(dto.ProjectId);
        if (project == null || !project.IsActive)
            return BadRequest("Invalid or inactive project.");

        var userName = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Unknown";
        var userEmail = User.FindFirst("preferred_username")?.Value
            ?? User.FindFirst("upn")?.Value
            ?? User.FindFirst("email")?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
            ?? User.FindFirst(System.Security.Claims.ClaimTypes.Upn)?.Value
            ?? "unknown@example.com";

        var request = new DevRequest
        {
            Title = dto.Title,
            Description = dto.Description,
            RequestType = dto.RequestType,
            Priority = dto.Priority,
            StepsToReproduce = dto.StepsToReproduce,
            ExpectedBehavior = dto.ExpectedBehavior,
            ActualBehavior = dto.ActualBehavior,
            ProjectId = dto.ProjectId,
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
            var (issueNumber, issueUrl) = await _gitHub.CreateIssueAsync(request, project.GitHubOwner, project.GitHubRepo);
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
            .Include(r => r.Project)
            .Include(r => r.Attachments)
            .Include(r => r.AgentReviews)
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
            if (request.Project != null)
                await _gitHub.UpdateIssueAsync(request, request.Project.GitHubOwner, request.Project.GitHubRepo);
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

    /// <summary>
    /// Upload one or more attachments (images/files) to a request.
    /// </summary>
    [HttpPost("{id}/attachments")]
    [RequestSizeLimit(10 * 1024 * 1024)] // 10 MB per request
    public async Task<ActionResult<List<AttachmentResponseDto>>> UploadAttachments(int id, [FromForm] List<IFormFile> files)
    {
        var request = await _db.DevRequests.FindAsync(id);
        if (request == null)
            return NotFound();

        if (files == null || files.Count == 0)
            return BadRequest("No files provided.");

        var uploadsRoot = Path.Combine(Directory.GetCurrentDirectory(), "uploads");
        Directory.CreateDirectory(uploadsRoot);

        var userName = User.FindFirst("name")?.Value ?? User.Identity?.Name ?? "Unknown";
        var results = new List<AttachmentResponseDto>();

        foreach (var file in files)
        {
            if (file.Length == 0) continue;
            if (file.Length > 5 * 1024 * 1024) // 5 MB per file
                return BadRequest($"File '{file.FileName}' exceeds the 5 MB limit.");

            var storedName = $"{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var storedPath = Path.Combine("uploads", storedName);
            var fullPath = Path.Combine(uploadsRoot, storedName);

            await using var stream = new FileStream(fullPath, FileMode.Create);
            await file.CopyToAsync(stream);

            var attachment = new Attachment
            {
                DevRequestId = id,
                FileName = file.FileName,
                ContentType = file.ContentType,
                FileSizeBytes = file.Length,
                StoredPath = storedPath,
                UploadedBy = userName,
                CreatedAt = DateTime.UtcNow
            };

            _db.Attachments.Add(attachment);
            await _db.SaveChangesAsync();

            results.Add(new AttachmentResponseDto
            {
                Id = attachment.Id,
                FileName = attachment.FileName,
                ContentType = attachment.ContentType,
                FileSizeBytes = attachment.FileSizeBytes,
                UploadedBy = attachment.UploadedBy,
                CreatedAt = attachment.CreatedAt,
                DownloadUrl = $"/api/requests/{id}/attachments/{attachment.Id}"
            });
        }

        return Ok(results);
    }

    /// <summary>
    /// Download an attachment.
    /// </summary>
    [HttpGet("{requestId}/attachments/{attachmentId}")]
    public async Task<ActionResult> DownloadAttachment(int requestId, int attachmentId)
    {
        var attachment = await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.DevRequestId == requestId);

        if (attachment == null)
            return NotFound();

        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), attachment.StoredPath);
        if (!System.IO.File.Exists(fullPath))
            return NotFound("File not found on disk.");

        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read);
        return File(stream, attachment.ContentType, attachment.FileName);
    }

    /// <summary>
    /// Delete an attachment.
    /// </summary>
    [HttpDelete("{requestId}/attachments/{attachmentId}")]
    public async Task<ActionResult> DeleteAttachment(int requestId, int attachmentId)
    {
        var attachment = await _db.Attachments
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.DevRequestId == requestId);

        if (attachment == null)
            return NotFound();

        var fullPath = Path.Combine(Directory.GetCurrentDirectory(), attachment.StoredPath);
        if (System.IO.File.Exists(fullPath))
            System.IO.File.Delete(fullPath);

        _db.Attachments.Remove(attachment);
        await _db.SaveChangesAsync();

        return NoContent();
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
        ProjectId = r.ProjectId,
        ProjectName = r.Project?.DisplayName ?? "Unknown",
        GitHubIssueNumber = r.GitHubIssueNumber,
        GitHubIssueUrl = r.GitHubIssueUrl,
        CreatedAt = r.CreatedAt,
        UpdatedAt = r.UpdatedAt,
        AgentReviewCount = r.AgentReviewCount,
        LatestAgentReview = r.AgentReviews?.OrderByDescending(a => a.CreatedAt).Select(a => new AgentReviewResponseDto
        {
            Id = a.Id,
            DevRequestId = a.DevRequestId,
            RequestTitle = r.Title,
            AgentType = a.AgentType,
            Decision = a.Decision,
            Reasoning = a.Reasoning,
            AlignmentScore = a.AlignmentScore,
            CompletenessScore = a.CompletenessScore,
            SalesAlignmentScore = a.SalesAlignmentScore,
            SuggestedPriority = a.SuggestedPriority,
            Tags = a.Tags,
            PromptTokens = a.PromptTokens,
            CompletionTokens = a.CompletionTokens,
            ModelUsed = a.ModelUsed,
            DurationMs = a.DurationMs,
            CreatedAt = a.CreatedAt
        }).FirstOrDefault(),
        Comments = r.Comments.Select(c => new CommentResponseDto
        {
            Id = c.Id,
            Author = c.Author,
            Content = c.Content,
            IsAgentComment = c.IsAgentComment,
            AgentReviewId = c.AgentReviewId,
            CreatedAt = c.CreatedAt
        }).ToList(),
        Attachments = r.Attachments.Select(a => new AttachmentResponseDto
        {
            Id = a.Id,
            FileName = a.FileName,
            ContentType = a.ContentType,
            FileSizeBytes = a.FileSizeBytes,
            UploadedBy = a.UploadedBy,
            CreatedAt = a.CreatedAt,
            DownloadUrl = $"/api/requests/{r.Id}/attachments/{a.Id}"
        }).ToList()
    };
}
