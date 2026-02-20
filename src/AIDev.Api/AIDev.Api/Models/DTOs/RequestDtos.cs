using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models.DTOs;

public class CreateRequestDto
{
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
}

public class UpdateRequestDto
{
    [MaxLength(200)]
    public string? Title { get; set; }

    public string? Description { get; set; }

    public RequestType? RequestType { get; set; }

    public Priority? Priority { get; set; }

    public string? StepsToReproduce { get; set; }

    public string? ExpectedBehavior { get; set; }

    public string? ActualBehavior { get; set; }

    public RequestStatus? Status { get; set; }
}

public class RequestResponseDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public RequestType RequestType { get; set; }
    public Priority Priority { get; set; }
    public string? StepsToReproduce { get; set; }
    public string? ExpectedBehavior { get; set; }
    public string? ActualBehavior { get; set; }
    public RequestStatus Status { get; set; }
    public string SubmittedBy { get; set; } = string.Empty;
    public string SubmittedByEmail { get; set; } = string.Empty;
    public int? GitHubIssueNumber { get; set; }
    public string? GitHubIssueUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<CommentResponseDto> Comments { get; set; } = new();
}

public class CommentResponseDto
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateCommentDto
{
    [Required]
    public string Content { get; set; } = string.Empty;
}

public class DashboardDto
{
    public int TotalRequests { get; set; }
    public Dictionary<string, int> ByStatus { get; set; } = new();
    public Dictionary<string, int> ByType { get; set; } = new();
    public Dictionary<string, int> ByPriority { get; set; } = new();
    public List<RequestResponseDto> RecentRequests { get; set; } = new();
}
