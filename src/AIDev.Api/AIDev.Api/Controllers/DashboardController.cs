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
                        CreatedAt = c.CreatedAt
                    }).ToList()
                }).ToList()
        };

        return Ok(dashboard);
    }
}
