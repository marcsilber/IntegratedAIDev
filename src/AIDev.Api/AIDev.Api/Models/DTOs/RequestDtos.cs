using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models.DTOs;

public class CreateRequestDto
{
    [Required]
    public int ProjectId { get; set; }

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
    public int ProjectId { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public int? GitHubIssueNumber { get; set; }
    public string? GitHubIssueUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<CommentResponseDto> Comments { get; set; } = new();
    public List<AttachmentResponseDto> Attachments { get; set; } = new();
    public AgentReviewResponseDto? LatestAgentReview { get; set; }
    public int AgentReviewCount { get; set; }
}

public class AttachmentResponseDto
{
    public int Id { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public string UploadedBy { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string DownloadUrl { get; set; } = string.Empty;
}

public class CommentResponseDto
{
    public int Id { get; set; }
    public string Author { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public bool IsAgentComment { get; set; }
    public int? AgentReviewId { get; set; }
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

// ── Project DTOs ──────────────────────────────────────────────────────────

public class ProjectResponseDto
{
    public int Id { get; set; }
    public string GitHubOwner { get; set; } = string.Empty;
    public string GitHubRepo { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string FullName { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public DateTime LastSyncedAt { get; set; }
    public int RequestCount { get; set; }
}

public class UpdateProjectDto
{
    [MaxLength(200)]
    public string? DisplayName { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    public bool? IsActive { get; set; }
}

// ── Agent DTOs ────────────────────────────────────────────────────────────

public class AgentReviewResponseDto
{
    public int Id { get; set; }
    public int DevRequestId { get; set; }
    public string RequestTitle { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public AgentDecision Decision { get; set; }
    public string Reasoning { get; set; } = string.Empty;
    public int AlignmentScore { get; set; }
    public int CompletenessScore { get; set; }
    public int SalesAlignmentScore { get; set; }
    public string? SuggestedPriority { get; set; }
    public string? Tags { get; set; }
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class AgentOverrideDto
{
    [Required]
    public RequestStatus NewStatus { get; set; }
    public string? Reason { get; set; }
}

public class AgentStatsDto
{
    public int TotalReviews { get; set; }
    public Dictionary<string, int> ByDecision { get; set; } = new();
    public double AverageAlignmentScore { get; set; }
    public double AverageCompletenessScore { get; set; }
    public double AverageSalesAlignmentScore { get; set; }
    public int TotalTokensUsed { get; set; }
    public int AverageDurationMs { get; set; }
}

public class AgentConfigDto
{
    public bool Enabled { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public int MaxReviewsPerRequest { get; set; }
    public float Temperature { get; set; }
    public string ModelName { get; set; } = string.Empty;
}
