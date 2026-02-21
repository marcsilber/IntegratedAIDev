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

    public int ProjectId { get; set; }
    public Project? Project { get; set; }

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

    public DateTime? LastAgentReviewAt { get; set; }

    public int AgentReviewCount { get; set; } = 0;

    public List<RequestComment> Comments { get; set; } = new();

    public List<Attachment> Attachments { get; set; } = new();

    public List<AgentReview> AgentReviews { get; set; } = new();

    public DateTime? LastArchitectReviewAt { get; set; }
    public int ArchitectReviewCount { get; set; } = 0;
    public List<ArchitectReview> ArchitectReviews { get; set; } = new();
    public List<CodeReview> CodeReviews { get; set; } = new();

    // ── Phase 4: Copilot Implementation ───────────────────────────────────
    [MaxLength(200)]
    public string? CopilotSessionId { get; set; }
    public int? CopilotPrNumber { get; set; }
    [MaxLength(500)]
    public string? CopilotPrUrl { get; set; }
    public DateTime? CopilotTriggeredAt { get; set; }
    public DateTime? CopilotCompletedAt { get; set; }
    public CopilotImplementationStatus? CopilotStatus { get; set; }

    // ── Phase 5: Pipeline Orchestrator ────────────────────────────────────
    /// <summary>Feature branch name created by Copilot for this request's PR.</summary>
    [MaxLength(300)]
    public string? CopilotBranchName { get; set; }

    /// <summary>Whether the feature branch has been deleted after merge.</summary>
    public bool BranchDeleted { get; set; }

    /// <summary>Tracks deployment status after PR merge.</summary>
    public DeploymentStatus DeploymentStatus { get; set; } = DeploymentStatus.None;

    /// <summary>GitHub Actions workflow run ID for deployment tracking.</summary>
    public long? DeploymentRunId { get; set; }

    /// <summary>When the deployment completed (success or failure).</summary>
    public DateTime? DeployedAt { get; set; }

    /// <summary>When a stall notification was last sent for this request.</summary>
    public DateTime? StallNotifiedAt { get; set; }
}
