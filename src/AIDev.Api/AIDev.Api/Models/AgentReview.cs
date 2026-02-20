using System.ComponentModel.DataAnnotations;

namespace AIDev.Api.Models;

public class AgentReview
{
    public int Id { get; set; }

    public int DevRequestId { get; set; }
    public DevRequest? DevRequest { get; set; }

    [Required]
    [MaxLength(50)]
    public string AgentType { get; set; } = "ProductOwner";

    public AgentDecision Decision { get; set; }

    [Required]
    public string Reasoning { get; set; } = string.Empty;

    public int AlignmentScore { get; set; }

    public int CompletenessScore { get; set; }

    public int SalesAlignmentScore { get; set; }

    [MaxLength(50)]
    public string? SuggestedPriority { get; set; }

    /// <summary>
    /// JSON array of suggested tags
    /// </summary>
    public string? Tags { get; set; }

    public int PromptTokens { get; set; }

    public int CompletionTokens { get; set; }

    [Required]
    [MaxLength(100)]
    public string ModelUsed { get; set; } = string.Empty;

    public int DurationMs { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RequestComment> Comments { get; set; } = new();
}
