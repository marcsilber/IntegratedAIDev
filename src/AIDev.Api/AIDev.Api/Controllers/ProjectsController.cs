using AIDev.Api.Data;
using AIDev.Api.Models.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProjectsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Get all active projects (for request form dropdown).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<ProjectResponseDto>>> GetAll()
    {
        var projects = await _db.Projects
            .Where(p => p.IsActive)
            .OrderBy(p => p.DisplayName)
            .Select(p => new ProjectResponseDto
            {
                Id = p.Id,
                GitHubOwner = p.GitHubOwner,
                GitHubRepo = p.GitHubRepo,
                DisplayName = p.DisplayName,
                Description = p.Description,
                FullName = p.FullName,
                IsActive = p.IsActive,
                LastSyncedAt = p.LastSyncedAt,
                RequestCount = p.Requests.Count
            })
            .ToListAsync();

        return Ok(projects);
    }
}
