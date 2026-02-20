using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models;

public class Project
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string GitHubOwner { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string GitHubRepo { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>
    /// Full name as returned by GitHub, e.g. "marcsilber/IntegratedAIDev"
    /// </summary>
    [MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    public bool IsActive { get; set; } = false;

    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<DevRequest> Requests { get; set; } = new();
}
