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
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }
    /// <summary>
    /// Get dashboard statistics.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<DashboardDto>> GetDashboard()
    {
        var requests = await _db.DevRequests
            .Include(r => r.Comments)
            .Include(r => r.AgentReviews)
            .ToListAsync();

        var dashboard = new DashboardDto
        {
            TotalRequests = requests.Count,
            ByStatus = Enum.GetValues<RequestStatus>()
                .ToDictionary(s => s.ToString(), s => requests.Count(r => r.Status == s)),
            ByType = Enum.GetValues<RequestType>()
                .ToDictionary(t => t.ToString(), t => requests.Count(r => r.RequestType == t)),
            ByPriority = Enum.GetValues<Priority>()
                .ToDictionary(p => p.ToString(), p => requests.Count(r => r.Priority == p)),
            RecentRequests = requests
                .OrderByDescending(r => r.CreatedAt)
                .Take(10)
                .Select(r => new RequestResponseDto
                {
                    Id = r.Id,
                    Title = r.Title,
                    Description = r.Description,
                    RequestType = r.RequestType,
                    Priority = r.Priority,
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
                        IsAgentComment = c.IsAgentComment,
                        AgentReviewId = c.AgentReviewId,
                        CreatedAt = c.CreatedAt
                    }).ToList()
                }).ToList()
        };

        return Ok(dashboard);
    }

    /// <summary>
    /// Get aggregate Code Review Agent statistics.
    /// </summary>
    [HttpGet("code-review-stats")]
    public async Task<ActionResult<CodeReviewStatsDto>> GetCodeReviewStats()
    {
        var reviews = await _db.CodeReviews.ToListAsync();
        var count = reviews.Count;

        return Ok(new CodeReviewStatsDto
        {
            TotalReviews = count,
            Approved = reviews.Count(r => r.Decision == CodeReviewDecision.Approved),
            ChangesRequested = reviews.Count(r => r.Decision == CodeReviewDecision.ChangesRequested),
            Failed = reviews.Count(r => r.Decision == CodeReviewDecision.Failed),
            AverageQualityScore = count > 0
                ? Math.Round(reviews.Average(r => r.QualityScore), 1) : 0,
            DesignComplianceRate = count > 0
                ? Math.Round((double)reviews.Count(r => r.DesignCompliance) / count * 100, 1) : 0,
            SecurityPassRate = count > 0
                ? Math.Round((double)reviews.Count(r => r.SecurityPass) / count * 100, 1) : 0,
            CodingStandardsPassRate = count > 0
                ? Math.Round((double)reviews.Count(r => r.CodingStandardsPass) / count * 100, 1) : 0,
            TotalFilesReviewed = reviews.Sum(r => r.FilesChanged),
            TotalLinesReviewed = reviews.Sum(r => r.LinesAdded + r.LinesRemoved),
            TotalTokensUsed = reviews.Sum(r => r.PromptTokens + r.CompletionTokens),
            AverageDurationMs = count > 0
                ? Math.Round(reviews.Average(r => r.DurationMs), 0) : 0
        });
    }

    /// <summary>
    /// Get aggregate PR Monitor statistics (PR lifecycle tracking).
    /// </summary>
    [HttpGet("pr-monitor-stats")]
    public async Task<ActionResult<PrMonitorStatsDto>> GetPrMonitorStats()
    {
        var requests = await _db.DevRequests
            .Where(r => r.CopilotPrNumber != null)
            .ToListAsync();

        return Ok(new PrMonitorStatsDto
        {
            TotalPrsTracked = requests.Count,
            PrsAwaitingReview = requests.Count(r =>
                r.CopilotStatus == CopilotImplementationStatus.PrOpened),
            PrsApprovedPendingMerge = requests.Count(r =>
                r.CopilotStatus == CopilotImplementationStatus.ReviewApproved),
            PrsMerged = requests.Count(r =>
                r.CopilotStatus == CopilotImplementationStatus.PrMerged),
            PrsFailed = requests.Count(r =>
                r.CopilotStatus == CopilotImplementationStatus.Failed),
            BranchesDeleted = requests.Count(r => r.BranchDeleted),
            BranchesPending = requests.Count(r =>
                r.CopilotBranchName != null && !r.BranchDeleted),
            DeploySucceeded = requests.Count(r =>
                r.DeploymentStatus == DeploymentStatus.Succeeded),
            DeployFailed = requests.Count(r =>
                r.DeploymentStatus == DeploymentStatus.Failed),
            DeployRetrying = requests.Count(r =>
                r.DeploymentRetryCount > 0 && r.DeploymentStatus == DeploymentStatus.Failed)
        });
    }
}
