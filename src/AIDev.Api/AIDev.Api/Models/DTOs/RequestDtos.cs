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
    public int DailyTokenBudget { get; set; }
    public int MonthlyTokenBudget { get; set; }
}

public class AgentConfigUpdateDto
{
    public bool? Enabled { get; set; }
    public int? PollingIntervalSeconds { get; set; }
    public int? MaxReviewsPerRequest { get; set; }
    public float? Temperature { get; set; }
    public int? DailyTokenBudget { get; set; }
    public int? MonthlyTokenBudget { get; set; }
}

public class TokenBudgetDto
{
    public int DailyTokensUsed { get; set; }
    public int DailyTokenBudget { get; set; }
    public bool DailyBudgetExceeded { get; set; }
    public int MonthlyTokensUsed { get; set; }
    public int MonthlyTokenBudget { get; set; }
    public bool MonthlyBudgetExceeded { get; set; }
    public int DailyReviewCount { get; set; }
    public int MonthlyReviewCount { get; set; }
}

// ── Architect DTOs ────────────────────────────────────────────────────────

public class ArchitectReviewResponseDto
{
    public int Id { get; set; }
    public int DevRequestId { get; set; }
    public string RequestTitle { get; set; } = string.Empty;
    public string SolutionSummary { get; set; } = string.Empty;
    public string Approach { get; set; } = string.Empty;
    public List<ImpactedFileDto> ImpactedFiles { get; set; } = new();
    public List<NewFileDto> NewFiles { get; set; } = new();
    public DataMigrationDto DataMigration { get; set; } = new();
    public List<string> BreakingChanges { get; set; } = new();
    public List<DependencyChangeDto> DependencyChanges { get; set; } = new();
    public List<RiskDto> Risks { get; set; } = new();
    public string EstimatedComplexity { get; set; } = string.Empty;
    public string EstimatedEffort { get; set; } = string.Empty;
    public List<string> ImplementationOrder { get; set; } = new();
    public string TestingNotes { get; set; } = string.Empty;
    public string ArchitecturalNotes { get; set; } = string.Empty;
    public ArchitectDecision Decision { get; set; }
    public string? HumanFeedback { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int FilesAnalysed { get; set; }
    public int TotalTokensUsed { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public int TotalDurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record ImpactedFileDto(string Path, string Action, string Description, int EstimatedLinesChanged);
public record NewFileDto(string Path, string Description, int EstimatedLines);
public record DataMigrationDto(bool Required = false, string? Description = null, List<string>? Steps = null)
{
    public List<string> Steps { get; init; } = Steps ?? new();
}
public record DependencyChangeDto(string Package, string Action, string Version, string Reason);
public record RiskDto(string Description, string Severity, string Mitigation);

public class ArchitectApprovalDto
{
    public string? Reason { get; set; }
}

public class ArchitectFeedbackDto
{
    [Required]
    public string Feedback { get; set; } = string.Empty;
}

public class ArchitectConfigDto
{
    public bool Enabled { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public int MaxReviewsPerRequest { get; set; }
    public int MaxFilesToRead { get; set; }
    public float Temperature { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int DailyTokenBudget { get; set; }
    public int MonthlyTokenBudget { get; set; }
}

public class ArchitectConfigUpdateDto
{
    public bool? Enabled { get; set; }
    public int? PollingIntervalSeconds { get; set; }
    public int? MaxReviewsPerRequest { get; set; }
    public int? MaxFilesToRead { get; set; }
    public float? Temperature { get; set; }
    public int? DailyTokenBudget { get; set; }
    public int? MonthlyTokenBudget { get; set; }
}

public class ArchitectStatsDto
{
    public int TotalAnalyses { get; set; }
    public int PendingReview { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Revised { get; set; }
    public double AverageFilesAnalysed { get; set; }
    public int TotalTokensUsed { get; set; }
    public double AverageDurationMs { get; set; }
}

// ── Implementation / Copilot DTOs ─────────────────────────────────────────

public class ImplementationStatusDto
{
    public int RequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? IssueNumber { get; set; }
    public CopilotImplementationStatus? CopilotStatus { get; set; }
    public string? CopilotSessionId { get; set; }
    public int? PrNumber { get; set; }
    public string? PrUrl { get; set; }
    public DateTime? TriggeredAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? ElapsedMinutes { get; set; }
}

public class ImplementationTriggerDto
{
    public string? AdditionalInstructions { get; set; }
    public string? Model { get; set; }
    public string? BaseBranch { get; set; }
}

public class ImplementationTriggerResponseDto
{
    public int RequestId { get; set; }
    public int? IssueNumber { get; set; }
    public CopilotImplementationStatus CopilotStatus { get; set; }
    public DateTime TriggeredAt { get; set; }
}

/// <summary>
/// Payload for rejecting a completed implementation.
/// </summary>
public record RejectImplementationDto(string? Reason);

public class ImplementationConfigDto
{
    public bool Enabled { get; set; }
    public bool AutoTriggerOnApproval { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public int PrPollIntervalSeconds { get; set; }
    public int MaxConcurrentSessions { get; set; }
    public string BaseBranch { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string CustomAgent { get; set; } = string.Empty;
    public int MaxRetries { get; set; }
}

public class ImplementationConfigUpdateDto
{
    public bool? Enabled { get; set; }
    public bool? AutoTriggerOnApproval { get; set; }
    public int? PollingIntervalSeconds { get; set; }
    public int? PrPollIntervalSeconds { get; set; }
    public int? MaxConcurrentSessions { get; set; }
    public string? BaseBranch { get; set; }
    public string? Model { get; set; }
    public int? MaxRetries { get; set; }
}

public class ImplementationStatsDto
{
    public int TotalTriggered { get; set; }
    public int Pending { get; set; }
    public int Working { get; set; }
    public int PrOpened { get; set; }
    public int PrMerged { get; set; }
    public int Failed { get; set; }
    public double SuccessRate { get; set; }
    public double AverageCompletionMinutes { get; set; }
    public int ActiveSessions { get; set; }
}

// ── Code Review Agent DTOs ────────────────────────────────────────────────

public class CodeReviewStatsDto
{
    public int TotalReviews { get; set; }
    public int Approved { get; set; }
    public int ChangesRequested { get; set; }
    public int Failed { get; set; }
    public double AverageQualityScore { get; set; }
    public double DesignComplianceRate { get; set; }
    public double SecurityPassRate { get; set; }
    public double CodingStandardsPassRate { get; set; }
    public int TotalFilesReviewed { get; set; }
    public int TotalLinesReviewed { get; set; }
    public int TotalTokensUsed { get; set; }
    public double AverageDurationMs { get; set; }
}

// ── PR Monitor / Pipeline Stats ───────────────────────────────────────────

public class PrMonitorStatsDto
{
    public int TotalPrsTracked { get; set; }
    public int PrsAwaitingReview { get; set; }
    public int PrsApprovedPendingMerge { get; set; }
    public int PrsMerged { get; set; }
    public int PrsFailed { get; set; }
    public int BranchesDeleted { get; set; }
    public int BranchesPending { get; set; }
    public int DeploySucceeded { get; set; }
    public int DeployFailed { get; set; }
    public int DeployRetrying { get; set; }
}

// ── Pipeline Orchestrator DTOs ────────────────────────────────────────────

public class PipelineHealthDto
{
    public int TotalStalled { get; set; }
    public int StalledNeedsClarification { get; set; }
    public int StalledArchitectReview { get; set; }
    public int StalledApproved { get; set; }
    public int StalledFailed { get; set; }
    public int DeploymentsPending { get; set; }
    public int DeploymentsInProgress { get; set; }
    public int DeploymentsSucceeded { get; set; }
    public int DeploymentsFailed { get; set; }
    public int DeploymentsRetrying { get; set; }
    public int StagedForDeploy { get; set; }
    public int BranchesDeleted { get; set; }
    public int BranchesOutstanding { get; set; }
}

public class PipelineConfigDto
{
    public bool Enabled { get; set; }
    public int PollIntervalSeconds { get; set; }
    public int NeedsClarificationStaleDays { get; set; }
    public int ArchitectReviewStaleDays { get; set; }
    public int ApprovedStaleDays { get; set; }
    public int FailedStaleHours { get; set; }
    public string DeploymentMode { get; set; } = "Auto";
    public int MaxDeployRetries { get; set; }
}

public class PipelineConfigUpdateDto
{
    public bool? Enabled { get; set; }
    public int? PollIntervalSeconds { get; set; }
    public int? NeedsClarificationStaleDays { get; set; }
    public int? ArchitectReviewStaleDays { get; set; }
    public int? ApprovedStaleDays { get; set; }
    public int? FailedStaleHours { get; set; }
    public string? DeploymentMode { get; set; }
    public int? MaxDeployRetries { get; set; }
}

public class StalledRequestDto
{
    public int RequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string StallReason { get; set; } = string.Empty;
    public string Severity { get; set; } = string.Empty;
    public int? GitHubIssueNumber { get; set; }
    public int DaysStalled { get; set; }
    public DateTime? StallNotifiedAt { get; set; }
}

public class DeploymentTrackingDto
{
    public int RequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? PrNumber { get; set; }
    public string DeploymentStatus { get; set; } = string.Empty;
    public long? DeploymentRunId { get; set; }
    public DateTime? MergedAt { get; set; }
    public DateTime? DeployedAt { get; set; }
    public bool BranchDeleted { get; set; }
    public string? BranchName { get; set; }
    public int RetryCount { get; set; }
}

public class StagedDeploymentDto
{
    public int RequestId { get; set; }
    public string Title { get; set; } = string.Empty;
    public int PrNumber { get; set; }
    public string PrUrl { get; set; } = string.Empty;
    public string BranchName { get; set; } = string.Empty;
    public int QualityScore { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int? GitHubIssueNumber { get; set; }
}

public class DeployTriggerResponseDto
{
    public List<int> MergedPrs { get; set; } = new();
    public List<int> FailedPrs { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

// ── System Prompt DTOs ────────────────────────────────────────────────────

/// <summary>
/// System prompt as returned to the admin panel.
/// </summary>
public record SystemPromptDto
{
    public int Id { get; init; }
    public string Key { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string PromptText { get; init; } = string.Empty;
    public string? UpdatedBy { get; init; }
    public DateTime UpdatedAt { get; init; }
}

/// <summary>
/// Payload for updating a system prompt.
/// </summary>
public record SystemPromptUpdateDto
{
    public string PromptText { get; init; } = string.Empty;
}
