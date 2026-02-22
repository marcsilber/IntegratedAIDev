namespace AIDev.Api.Models;

/// <summary>
/// Stores an admin-editable system prompt for an AI agent.
/// </summary>
public class SystemPrompt
{
    public int Id { get; set; }

    /// <summary>
    /// Unique key identifying the prompt, e.g. "ProductOwner", "ArchitectFileSelection".
    /// </summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>
    /// Human-friendly name shown in the admin panel.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this prompt does and what placeholders are available.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// The full prompt text (may include {0}, {1}, etc. format placeholders).
    /// </summary>
    public string PromptText { get; set; } = string.Empty;

    /// <summary>
    /// Who last modified this prompt (display name or email).
    /// </summary>
    public string? UpdatedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
