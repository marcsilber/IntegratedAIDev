using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models;

public class DevRequest
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Description { get; set; } = string.Empty;

    public RequestType RequestType { get; set; } = RequestType.Bug;

    public Priority Priority { get; set; } = Priority.Medium;

    public string? StepsToReproduce { get; set; }

    public string? ExpectedBehavior { get; set; }

    public string? ActualBehavior { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.New;

    [Required]
    [MaxLength(200)]
    public string SubmittedBy { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string SubmittedByEmail { get; set; } = string.Empty;

    public int? GitHubIssueNumber { get; set; }

    [MaxLength(500)]
    public string? GitHubIssueUrl { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public List<RequestComment> Comments { get; set; } = new();
}
