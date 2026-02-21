using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models;

/// <summary>
/// Decision made by the Code Review Agent after analysing a PR diff.
/// </summary>
public enum CodeReviewDecision
{
    Approved,
    ChangesRequested,
    Failed
}

/// <summary>
/// Stores the result of an automated code review performed by the Code Review Agent.
/// </summary>
public class CodeReview
{
    public int Id { get; set; }

    public int DevRequestId { get; set; }
    public DevRequest? DevRequest { get; set; }

    /// <summary>PR number on GitHub that was reviewed.</summary>
    public int PrNumber { get; set; }

    /// <summary>Overall decision: approved or changes requested.</summary>
    public CodeReviewDecision Decision { get; set; }

    /// <summary>Human-readable summary of the review.</summary>
    [Required]
    public string Summary { get; set; } = string.Empty;

    /// <summary>Whether the PR matches the approved architect solution scope.</summary>
    public bool DesignCompliance { get; set; }

    /// <summary>Details on design compliance check.</summary>
    public string? DesignComplianceNotes { get; set; }

    /// <summary>Whether security criteria passed.</summary>
    public bool SecurityPass { get; set; }

    /// <summary>Details on security findings.</summary>
    public string? SecurityNotes { get; set; }

    /// <summary>Whether coding standards are met.</summary>
    public bool CodingStandardsPass { get; set; }

    /// <summary>Details on coding standards findings.</summary>
    public string? CodingStandardsNotes { get; set; }

    /// <summary>Overall quality score 1-10.</summary>
    public int QualityScore { get; set; }

    /// <summary>Number of files changed in the PR.</summary>
    public int FilesChanged { get; set; }

    /// <summary>Lines added in the PR.</summary>
    public int LinesAdded { get; set; }

    /// <summary>Lines removed in the PR.</summary>
    public int LinesRemoved { get; set; }

    // LLM token tracking
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }

    [Required]
    [MaxLength(100)]
    public string ModelUsed { get; set; } = string.Empty;

    public int DurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
