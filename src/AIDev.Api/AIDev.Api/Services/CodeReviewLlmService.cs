using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AIDev.Api.Models;
using OpenAI.Chat;

namespace AIDev.Api.Services;

// ────────────────────────────────────────────────────────────
// Result types
// ────────────────────────────────────────────────────────────

/// <summary>
/// Result of an automated code review performed by the LLM.
/// </summary>
public record CodeReviewResult
{
    public required CodeReviewDecision Decision { get; init; }
    public required string Summary { get; init; }
    public required bool DesignCompliance { get; init; }
    public required string DesignComplianceNotes { get; init; }
    public required bool SecurityPass { get; init; }
    public required string SecurityNotes { get; init; }
    public required bool CodingStandardsPass { get; init; }
    public required string CodingStandardsNotes { get; init; }
    public required int QualityScore { get; init; }

    // LLM metadata
    public int PromptTokens { get; init; }
    public int CompletionTokens { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
    public int DurationMs { get; init; }
}

// ────────────────────────────────────────────────────────────
// Interface
// ────────────────────────────────────────────────────────────

/// <summary>
/// Performs LLM-based code review of a PR diff against an approved solution.
/// </summary>
public interface ICodeReviewLlmService
{
    /// <summary>
    /// Review a PR diff against the approved architect solution, security criteria,
    /// and coding standards.
    /// </summary>
    Task<CodeReviewResult> ReviewPrAsync(
        DevRequest request,
        ArchitectReview architectReview,
        string diff,
        int filesChanged,
        int linesAdded,
        int linesRemoved);
}

// ────────────────────────────────────────────────────────────
// Implementation
// ────────────────────────────────────────────────────────────

public class CodeReviewLlmService : ICodeReviewLlmService
{
    private readonly ChatClient _chatClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CodeReviewLlmService> _logger;
    private readonly string _modelName;
    private readonly int _maxInputChars;

    private static readonly string SystemPrompt = """
        You are a senior code reviewer for an enterprise software project.
        Your job is to review a pull request diff against:
        1. The APPROVED SOLUTION (architecture design)
        2. Security criteria
        3. Coding standards

        ## Security Criteria
        - No hardcoded secrets, API keys, or credentials
        - No SQL injection vulnerabilities
        - No cross-site scripting (XSS) vulnerabilities
        - Authentication/authorization properly implemented
        - Input validation on all user inputs
        - No sensitive data in logs
        - Dependencies are standard and well-known

        ## Coding Standards
        - Use nullable reference types
        - Follow existing controller/service patterns
        - Add XML doc comments on public members
        - Use record types for DTOs
        - Enums use string conversion in EF Core
        - Functional React components with TypeScript interfaces
        - API calls in services layer using Axios
        - Inline styles (no CSS modules or Tailwind)
        - Clear, descriptive variable and function names
        - No commented-out code blocks
        - Proper error handling

        ## Response Format
        Respond with ONLY valid JSON (no markdown fences) matching this schema:
        {
            "decision": "Approved" | "ChangesRequested",
            "summary": "Brief 1-3 sentence summary of the review",
            "designCompliance": true | false,
            "designComplianceNotes": "How well the PR matches the approved solution scope",
            "securityPass": true | false,
            "securityNotes": "Any security issues found or 'No security issues found'",
            "codingStandardsPass": true | false,
            "codingStandardsNotes": "Any coding standard violations or 'Coding standards met'",
            "qualityScore": 1-10
        }

        IMPORTANT RULES:
        - Approve if there are no critical issues. Minor style issues should NOT block approval.
        - A PR must match the general intent of the approved solution, but implementation details may vary.
        - Focus on correctness, security, and significant code quality issues.
        - Be pragmatic — don't reject for trivial issues.
        """;

    public CodeReviewLlmService(
        ILlmClientFactory clientFactory,
        IConfiguration configuration,
        ILogger<CodeReviewLlmService> logger)
    {
        _chatClient = clientFactory.CreateChatClient();
        _modelName = clientFactory.ModelName;
        _configuration = configuration;
        _logger = logger;

        var maxInputTokens = int.Parse(configuration["CodeReviewAgent:MaxInputTokens"] ?? "6000");
        _maxInputChars = maxInputTokens * 4; // rough char-to-token ratio
    }

    public async Task<CodeReviewResult> ReviewPrAsync(
        DevRequest request,
        ArchitectReview architectReview,
        string diff,
        int filesChanged,
        int linesAdded,
        int linesRemoved)
    {
        var sw = Stopwatch.StartNew();
        var temperature = float.Parse(_configuration["CodeReviewAgent:Temperature"] ?? "0.2");
        var maxTokens = int.Parse(_configuration["CodeReviewAgent:MaxTokens"] ?? "2000");

        // Build user message
        var userMessage = new StringBuilder();
        userMessage.AppendLine("## Request Being Implemented");
        userMessage.AppendLine($"**Title:** {request.Title}");
        userMessage.AppendLine($"**Description:** {request.Description}");
        userMessage.AppendLine();

        // Approved solution context (40% of budget)
        var solutionBudget = _maxInputChars * 40 / 100;
        var solutionContext = $"""
            ## Approved Solution
            **Summary:** {architectReview.SolutionSummary}
            **Approach:** {architectReview.Approach}
            **Complexity:** {architectReview.EstimatedComplexity}
            **Solution Details:**
            {architectReview.SolutionJson}
            """;
        if (solutionContext.Length > solutionBudget)
        {
            solutionContext = solutionContext[..solutionBudget] + "\n... [truncated]";
        }
        userMessage.AppendLine(solutionContext);
        userMessage.AppendLine();

        // PR diff (60% of budget)
        var diffBudget = _maxInputChars * 60 / 100;
        userMessage.AppendLine($"## Pull Request Diff ({filesChanged} files changed, +{linesAdded} -{linesRemoved})");
        if (diff.Length > diffBudget)
        {
            userMessage.AppendLine(diff[..diffBudget]);
            userMessage.AppendLine("... [diff truncated due to size]");
        }
        else
        {
            userMessage.AppendLine(diff);
        }

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(SystemPrompt),
            new UserChatMessage(userMessage.ToString())
        };

        var options = new ChatCompletionOptions
        {
            Temperature = temperature,
            MaxOutputTokenCount = maxTokens
        };

        _logger.LogInformation("CodeReview LLM call: {InputChars} chars input, model={Model}",
            userMessage.Length, _modelName);

        var completion = await _chatClient.CompleteChatAsync(messages, options);
        var responseText = completion.Value.Content[0].Text?.Trim() ?? "";
        var promptTokens = completion.Value.Usage?.InputTokenCount ?? 0;
        var completionTokens = completion.Value.Usage?.OutputTokenCount ?? 0;

        sw.Stop();

        _logger.LogInformation("CodeReview LLM response: {OutputChars} chars, {PromptTokens}+{CompletionTokens} tokens, {DurationMs}ms",
            responseText.Length, promptTokens, completionTokens, sw.ElapsedMilliseconds);

        // Parse the response
        var cleaned = StripCodeFences(responseText);
        try
        {
            var parsed = JsonSerializer.Deserialize<CodeReviewLlmResponse>(cleaned,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (parsed == null)
                throw new JsonException("Null result from deserialization");

            var decision = parsed.Decision?.Equals("Approved", StringComparison.OrdinalIgnoreCase) == true
                ? CodeReviewDecision.Approved
                : CodeReviewDecision.ChangesRequested;

            return new CodeReviewResult
            {
                Decision = decision,
                Summary = parsed.Summary ?? "No summary provided",
                DesignCompliance = parsed.DesignCompliance,
                DesignComplianceNotes = parsed.DesignComplianceNotes ?? "",
                SecurityPass = parsed.SecurityPass,
                SecurityNotes = parsed.SecurityNotes ?? "",
                CodingStandardsPass = parsed.CodingStandardsPass,
                CodingStandardsNotes = parsed.CodingStandardsNotes ?? "",
                QualityScore = Math.Clamp(parsed.QualityScore, 1, 10),
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                ModelUsed = _modelName,
                DurationMs = (int)sw.ElapsedMilliseconds
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to parse code review LLM response. Raw: {Response}", responseText[..Math.Min(500, responseText.Length)]);

            // Fallback: if response mentions "approve" it's likely an approval
            var isApproval = responseText.Contains("Approved", StringComparison.OrdinalIgnoreCase)
                          && !responseText.Contains("ChangesRequested", StringComparison.OrdinalIgnoreCase);

            return new CodeReviewResult
            {
                Decision = isApproval ? CodeReviewDecision.Approved : CodeReviewDecision.ChangesRequested,
                Summary = $"LLM response could not be parsed. Raw excerpt: {responseText[..Math.Min(300, responseText.Length)]}",
                DesignCompliance = isApproval,
                DesignComplianceNotes = "Could not parse structured response",
                SecurityPass = isApproval,
                SecurityNotes = "Could not parse structured response",
                CodingStandardsPass = isApproval,
                CodingStandardsNotes = "Could not parse structured response",
                QualityScore = isApproval ? 7 : 4,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                ModelUsed = _modelName,
                DurationMs = (int)sw.ElapsedMilliseconds
            };
        }
    }

    private static string StripCodeFences(string text)
    {
        text = text.Trim();
        if (text.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
            text = text[7..];
        else if (text.StartsWith("```"))
            text = text[3..];
        if (text.EndsWith("```"))
            text = text[..^3];
        return text.Trim();
    }

    /// <summary>Internal DTO for JSON deserialization of LLM response.</summary>
    private record CodeReviewLlmResponse
    {
        public string? Decision { get; init; }
        public string? Summary { get; init; }
        public bool DesignCompliance { get; init; }
        public string? DesignComplianceNotes { get; init; }
        public bool SecurityPass { get; init; }
        public string? SecurityNotes { get; init; }
        public bool CodingStandardsPass { get; init; }
        public string? CodingStandardsNotes { get; init; }
        public int QualityScore { get; init; }
    }
}
