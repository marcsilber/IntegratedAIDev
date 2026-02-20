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
public class AdminController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IGitHubService _gitHub;

    public AdminController(AppDbContext db, IGitHubService gitHub)
    {
        _db = db;
        _gitHub = gitHub;
    }

    /// <summary>
    /// Get all projects (including inactive) for admin management.
    /// </summary>
    [HttpGet("projects")]
    public async Task<ActionResult<List<ProjectResponseDto>>> GetAllProjects()
    {
        var projects = await _db.Projects
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

    /// <summary>
    /// Sync repositories from GitHub and upsert into the database.
    /// </summary>
    [HttpPost("projects/sync")]
    public async Task<ActionResult<List<ProjectResponseDto>>> SyncProjects()
    {
        var repos = await _gitHub.GetRepositoriesAsync();

        var results = new List<ProjectResponseDto>();

        foreach (var repo in repos)
        {
            var existing = await _db.Projects
                .FirstOrDefaultAsync(p => p.GitHubOwner == repo.Owner.Login && p.GitHubRepo == repo.Name);

            if (existing != null)
            {
                existing.Description = repo.Description ?? "";
                existing.FullName = repo.FullName;
                existing.LastSyncedAt = DateTime.UtcNow;
            }
            else
            {
                existing = new Project
                {
                    GitHubOwner = repo.Owner.Login,
                    GitHubRepo = repo.Name,
                    DisplayName = repo.Name,
                    Description = repo.Description ?? "",
                    FullName = repo.FullName,
                    IsActive = false, // Admin must explicitly activate
                    LastSyncedAt = DateTime.UtcNow
                };
                _db.Projects.Add(existing);
            }
        }

        await _db.SaveChangesAsync();

        // Return all projects after sync
        var projects = await _db.Projects
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

    /// <summary>
    /// Update a project (toggle active, rename, etc.)
    /// </summary>
    [HttpPut("projects/{id}")]
    public async Task<ActionResult<ProjectResponseDto>> UpdateProject(int id, [FromBody] UpdateProjectDto dto)
    {
        var project = await _db.Projects
            .Include(p => p.Requests)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project == null)
            return NotFound();

        if (dto.DisplayName != null) project.DisplayName = dto.DisplayName;
        if (dto.Description != null) project.Description = dto.Description;
        if (dto.IsActive.HasValue) project.IsActive = dto.IsActive.Value;

        await _db.SaveChangesAsync();

        return Ok(new ProjectResponseDto
        {
            Id = project.Id,
            GitHubOwner = project.GitHubOwner,
            GitHubRepo = project.GitHubRepo,
            DisplayName = project.DisplayName,
            Description = project.Description,
            FullName = project.FullName,
            IsActive = project.IsActive,
            LastSyncedAt = project.LastSyncedAt,
            RequestCount = project.Requests.Count
        });
    }
}
