using System.Diagnostics;
using System.Text.Json;
using AIDev.Api.Models;
using OpenAI.Chat;

namespace AIDev.Api.Services;

/// <summary>
/// Result from an LLM review of a development request.
/// </summary>
public record AgentReviewResult(
    AgentDecision Decision,
    string Reasoning,
    int AlignmentScore,
    int CompletenessScore,
    int SalesAlignmentScore,
    List<string>? ClarificationQuestions,
    string? SuggestedPriority,
    List<string>? Tags,
    bool IsDuplicate,
    int? DuplicateOfRequestId,
    int PromptTokens,
    int CompletionTokens,
    string ModelUsed,
    int DurationMs
);

/// <summary>
/// Wraps GitHub Models (OpenAI-compatible) for the Product Owner Agent.
/// </summary>
public interface ILlmService
{
    Task<AgentReviewResult> ReviewRequestAsync(DevRequest request,
        List<RequestComment>? conversationHistory = null,
        List<DevRequest>? existingRequests = null,
        List<Attachment>? attachments = null);
}

public class LlmService : ILlmService
{
    private readonly ChatClient _chatClient;
    private readonly IReferenceDocumentService _refDocs;
    private readonly ISystemPromptService _systemPrompts;
    private readonly ILogger<LlmService> _logger;
    private readonly string _modelName;
    private readonly float _temperature;
    private readonly int _maxTokens;

    public LlmService(ILlmClientFactory clientFactory, IConfiguration configuration, IReferenceDocumentService refDocs, ISystemPromptService systemPrompts, ILogger<LlmService> logger)
    {
        _refDocs = refDocs;
        _systemPrompts = systemPrompts;
        _logger = logger;

        _modelName = clientFactory.ModelName;
        _temperature = float.Parse(configuration["ProductOwnerAgent:Temperature"] ?? "0.3");
        _maxTokens = int.Parse(configuration["ProductOwnerAgent:MaxTokens"] ?? "2000");

        _chatClient = clientFactory.CreateChatClient();
    }

    public async Task<AgentReviewResult> ReviewRequestAsync(DevRequest request,
        List<RequestComment>? conversationHistory = null,
        List<DevRequest>? existingRequests = null,
        List<Attachment>? attachments = null)
    {
        var sw = Stopwatch.StartNew();

        var promptTemplate = _systemPrompts.GetPrompt(SystemPromptService.Keys.ProductOwner);
        var systemPrompt = string.Format(promptTemplate, _refDocs.GetSystemPromptContext());
        var userMessage = BuildUserMessage(request, conversationHistory, existingRequests);

        _logger.LogInformation("Reviewing request #{RequestId} '{Title}' via {Model}",
            request.Id, request.Title, _modelName);

        // Build multimodal user message with text + any image attachments
        var userChatMessage = BuildMultimodalUserMessage(userMessage, attachments, _logger);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            userChatMessage
        };

        var options = new ChatCompletionOptions
        {
            Temperature = _temperature,
            MaxOutputTokenCount = _maxTokens,
        };

        ChatCompletion completion = await _chatClient.CompleteChatAsync(messages, options);

        sw.Stop();

        var responseText = completion.Content[0].Text;
        var promptTokens = (int)(completion.Usage?.InputTokenCount ?? 0);
        var completionTokens = (int)(completion.Usage?.OutputTokenCount ?? 0);

        _logger.LogInformation("LLM response for request #{RequestId}: {Tokens} tokens in {Duration}ms",
            request.Id, promptTokens + completionTokens, sw.ElapsedMilliseconds);

        return ParseResponse(responseText, promptTokens, completionTokens, (int)sw.ElapsedMilliseconds);
    }

    private static string BuildUserMessage(DevRequest request, List<RequestComment>? conversationHistory, List<DevRequest>? existingRequests)
    {
        var parts = new List<string>
        {
            "Review the following development request:",
            "",
            $"Title: {request.Title}",
            $"Type: {request.RequestType}",
            $"Priority: {request.Priority}",
            $"Description: {request.Description}",
            $"Project: {request.Project?.DisplayName ?? "Unknown"}"
        };

        if (!string.IsNullOrWhiteSpace(request.StepsToReproduce))
            parts.Add($"Steps to Reproduce: {request.StepsToReproduce}");
        if (!string.IsNullOrWhiteSpace(request.ExpectedBehavior))
            parts.Add($"Expected Behavior: {request.ExpectedBehavior}");
        if (!string.IsNullOrWhiteSpace(request.ActualBehavior))
            parts.Add($"Actual Behavior: {request.ActualBehavior}");

        parts.Add($"Submitted By: {request.SubmittedBy}");

        // Include existing requests for duplicate detection
        if (existingRequests != null && existingRequests.Count > 0)
        {
            parts.Add("");
            parts.Add("EXISTING REQUESTS (check for duplicates/already-implemented features):");
            foreach (var er in existingRequests)
            {
                var ghRef = er.GitHubIssueNumber.HasValue ? $" [GitHub Issue #{er.GitHubIssueNumber}]" : "";
                parts.Add($"- Request #{er.Id}{ghRef}: [{er.Status}] [{er.RequestType}] {er.Title} — {Truncate(er.Description, 150)}");
            }
        }

        if (conversationHistory != null && conversationHistory.Count > 0)
        {
            parts.Add("");
            parts.Add("CONVERSATION HISTORY:");
            foreach (var comment in conversationHistory.OrderBy(c => c.CreatedAt))
            {
                var source = comment.IsAgentComment ? "Agent" : "Submitter";
                parts.Add($"[{source}] {comment.Content}");
            }
        }

        return string.Join("\n", parts);
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) return text;
        return text[..maxLength] + "...";
    }

    private AgentReviewResult ParseResponse(string responseText, int promptTokens, int completionTokens, int durationMs)
    {
        try
        {
            // Strip any markdown code fences the LLM might add despite instructions
            var json = responseText.Trim();
            if (json.StartsWith("```"))
            {
                var firstNewline = json.IndexOf('\n');
                var lastFence = json.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    json = json[(firstNewline + 1)..lastFence].Trim();
            }

            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<LlmResponseDto>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            if (parsed == null)
                throw new JsonException("Parsed response was null");

            var decision = parsed.Decision?.ToLowerInvariant() switch
            {
                "approve" => AgentDecision.Approve,
                "reject" => AgentDecision.Reject,
                "clarify" => AgentDecision.Clarify,
                _ => AgentDecision.Clarify // Default to clarify if unclear
            };

            return new AgentReviewResult(
                Decision: decision,
                Reasoning: parsed.Reasoning ?? "No reasoning provided",
                AlignmentScore: Math.Clamp(parsed.AlignmentScore, 0, 100),
                CompletenessScore: Math.Clamp(parsed.CompletenessScore, 0, 100),
                SalesAlignmentScore: Math.Clamp(parsed.SalesAlignmentScore, 0, 100),
                ClarificationQuestions: parsed.ClarificationQuestions,
                SuggestedPriority: parsed.SuggestedPriority,
                Tags: parsed.Tags,
                IsDuplicate: parsed.IsDuplicate,
                DuplicateOfRequestId: parsed.DuplicateOfRequestId,
                PromptTokens: promptTokens,
                CompletionTokens: completionTokens,
                ModelUsed: _modelName,
                DurationMs: durationMs
            );
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse LLM response, defaulting to clarify. Response: {Response}", responseText);

            // Fallback — treat unparseable response as needing human review
            return new AgentReviewResult(
                Decision: AgentDecision.Clarify,
                Reasoning: $"Agent response could not be parsed. Raw response: {responseText[..Math.Min(500, responseText.Length)]}",
                AlignmentScore: 50,
                CompletenessScore: 50,
                SalesAlignmentScore: 50,
                ClarificationQuestions: new List<string> { "The automated review encountered an issue. A human reviewer should assess this request." },
                SuggestedPriority: null,
                Tags: null,
                IsDuplicate: false,
                DuplicateOfRequestId: null,
                PromptTokens: promptTokens,
                CompletionTokens: completionTokens,
                ModelUsed: _modelName,
                DurationMs: durationMs
            );
        }
    }

    /// <summary>
    /// Builds a UserChatMessage with text and optional image attachments for vision-capable models.
    /// Shared by PO Agent and Architect Agent.
    /// </summary>
    internal static UserChatMessage BuildMultimodalUserMessage(
        string textMessage,
        List<Attachment>? attachments,
        ILogger logger)
    {
        var imageAttachments = attachments?
            .Where(a => a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (imageAttachments == null || imageAttachments.Count == 0)
        {
            return new UserChatMessage(textMessage);
        }

        var parts = new List<ChatMessageContentPart>
        {
            ChatMessageContentPart.CreateTextPart(textMessage)
        };

        foreach (var attachment in imageAttachments)
        {
            try
            {
                var filePath = Path.Combine(Directory.GetCurrentDirectory(), attachment.StoredPath);
                if (!File.Exists(filePath))
                {
                    logger.LogWarning("Attachment file not found: {Path}", filePath);
                    continue;
                }

                // Skip very large images (>5 MB) to avoid excessive token usage
                var fileInfo = new FileInfo(filePath);
                if (fileInfo.Length > 5 * 1024 * 1024)
                {
                    logger.LogWarning("Skipping oversized image attachment {FileName} ({Size} bytes)",
                        attachment.FileName, fileInfo.Length);
                    continue;
                }

                var imageBytes = File.ReadAllBytes(filePath);
                var binaryData = BinaryData.FromBytes(imageBytes);
                parts.Add(ChatMessageContentPart.CreateImagePart(binaryData, attachment.ContentType));

                logger.LogInformation("Included image attachment '{FileName}' ({ContentType}, {Size} bytes)",
                    attachment.FileName, attachment.ContentType, fileInfo.Length);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to load image attachment {FileName}", attachment.FileName);
            }
        }

        return new UserChatMessage(parts);
    }

    private class LlmResponseDto
    {
        public string? Decision { get; set; }
        public string? Reasoning { get; set; }
        public int AlignmentScore { get; set; }
        public int CompletenessScore { get; set; }
        public int SalesAlignmentScore { get; set; }
        public List<string>? ClarificationQuestions { get; set; }
        public string? SuggestedPriority { get; set; }
        public List<string>? Tags { get; set; }
        public bool IsDuplicate { get; set; }
        public int? DuplicateOfRequestId { get; set; }
    }
}
