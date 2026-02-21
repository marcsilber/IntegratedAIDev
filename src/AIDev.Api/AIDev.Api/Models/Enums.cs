namespace AIDev.Api.Models;

public enum RequestType
{
    Bug,
    Feature,
    Enhancement,
    Question
}

public enum Priority
{
    Low,
    Medium,
    High,
    Critical
}

public enum RequestStatus
{
    New,
    NeedsClarification,
    Triaged,
    ArchitectReview,
    Approved,
    InProgress,
    Done,
    Rejected
}

public enum AgentDecision
{
    Approve,
    Reject,
    Clarify
}

public enum ArchitectDecision
{
    Pending,
    Approved,
    Rejected,
    Revised
}

public enum CopilotImplementationStatus
{
    Pending,        // Issue assigned to Copilot, waiting for session to start
    Working,        // Copilot is actively implementing
    PrOpened,       // Copilot opened a PR, awaiting review
    ReviewApproved, // Code Review Agent approved the PR, merging
    PrMerged,       // PR merged — implementation complete
    Failed          // Copilot couldn't complete the task
}

/// <summary>
/// Tracks whether a merged PR has been deployed to the UAT environment.
/// </summary>
public enum DeploymentStatus
{
    None,           // No deployment tracked yet
    Pending,        // Merge detected, waiting for GitHub Actions workflow
    InProgress,     // Workflow run detected and running
    Succeeded,      // Deployment workflow completed successfully
    Failed          // Deployment workflow failed
}

/// <summary>
/// Controls whether deployments happen automatically after PR merge
/// or require manual trigger from the admin panel.
/// </summary>
public enum DeploymentMode
{
    /// <summary>PRs auto-merge and deploy immediately after code review approval.</summary>
    Auto,
    /// <summary>PRs are approved but not merged until a human clicks Deploy.</summary>
    Staged
}

/// <summary>
/// Severity of a pipeline stall alert.
/// </summary>
public enum StallSeverity
{
    Warning,
    Critical
}
