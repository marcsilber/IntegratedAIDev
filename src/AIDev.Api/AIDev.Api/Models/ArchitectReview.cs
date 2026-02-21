using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models;

public class ArchitectReview
{
    public int Id { get; set; }

    public int DevRequestId { get; set; }
    public DevRequest? DevRequest { get; set; }

    [Required]
    [MaxLength(50)]
    public string AgentType { get; set; } = "Architect";

    [Required]
    public string SolutionSummary { get; set; } = string.Empty;

    [Required]
    public string Approach { get; set; } = string.Empty;

    /// <summary>
    /// Full solution JSON (ImpactedFiles, NewFiles, Risks, etc.)
    /// </summary>
    [Required]
    public string SolutionJson { get; set; } = string.Empty;

    [MaxLength(50)]
    public string EstimatedComplexity { get; set; } = string.Empty;

    [MaxLength(50)]
    public string EstimatedEffort { get; set; } = string.Empty;

    public int FilesAnalysed { get; set; }

    /// <summary>JSON array of file paths that were read</summary>
    public string? FilesReadJson { get; set; }

    // Human review
    public ArchitectDecision Decision { get; set; } = ArchitectDecision.Pending;
    public string? HumanFeedback { get; set; }

    [MaxLength(200)]
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // LLM token tracking (two-step)
    public int Step1PromptTokens { get; set; }
    public int Step1CompletionTokens { get; set; }
    public int Step2PromptTokens { get; set; }
    public int Step2CompletionTokens { get; set; }

    [Required]
    [MaxLength(100)]
    public string ModelUsed { get; set; } = string.Empty;

    public int TotalDurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RequestComment> Comments { get; set; } = new();
}
