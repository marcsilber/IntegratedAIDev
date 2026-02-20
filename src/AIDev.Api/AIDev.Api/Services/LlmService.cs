using System.ClientModel;
using System.Diagnostics;
using System.Text.Json;
using AIDev.Api.Models;
using OpenAI;
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
        List<DevRequest>? existingRequests = null);
}

public class LlmService : ILlmService
{
    private readonly ChatClient _chatClient;
    private readonly IReferenceDocumentService _refDocs;
    private readonly ILogger<LlmService> _logger;
    private readonly string _modelName;
    private readonly float _temperature;
    private readonly int _maxTokens;

    private const string SystemPromptTemplate = """
        You are a Product Owner Agent for the AI Dev Pipeline platform.
        
        Your role is to triage incoming development requests (bugs, features, enhancements, questions)
        by evaluating them against the product's objectives and sales positioning.
        
        REFERENCE DOCUMENTS:
        {0}
        
        The reference documents include:
        - ApplicationObjectives.md — the product's goals and success criteria
        - ApplicationSalesPack.md — the product's market positioning and value propositions
        - ApplicationFeatures.md — a comprehensive inventory of ALREADY IMPLEMENTED features
          Use this to determine if a requested feature already exists in the application.
        
        EVALUATION CRITERIA:
        1. COMPLETENESS (0-100): Does the request contain enough detail to act on?
           - For bugs: steps to reproduce, expected vs actual behavior
           - For features: clear description of what is needed and why
           - For questions: enough context to provide an answer
        
        2. ALIGNMENT (0-100): Does this request align with the product objectives?
           - Is it in scope for what the product is designed to do?
           - Does it support the stated goals and principles?
        
        3. SALES ALIGNMENT (0-100): Does this enhance the product's market positioning?
           - Would it strengthen the sales pack / value proposition?
           - Does it serve the target audience?
        
        4. DUPLICATE / ALREADY EXISTS CHECK:
           - Compare against the EXISTING REQUESTS list provided below.
           - If the request describes functionality that already exists, is already being worked on,
             or has already been completed (status Done/InProgress/Approved/Triaged), flag it as a duplicate.
           - If a similar request was previously Rejected, note this but still evaluate on merit.
           - Consider both exact duplicates and requests that substantially overlap.
        
        DECISION RULES:
        - REJECT if the request is a duplicate of an existing Done/InProgress/Approved request.
          Clearly state which existing request(s) it duplicates.
        - APPROVE if alignment >= 60 AND completeness >= 50 AND not a duplicate.
        - CLARIFY if completeness < 50: The request lacks detail. Ask specific questions.
        - REJECT if alignment < 30: The request is clearly out of scope or contradicts product direction.
        - When in doubt between approve and clarify, prefer clarify.
        
        You MUST respond with valid JSON only. No markdown, no code fences, no explanation outside the JSON.
        
        JSON SCHEMA:
        {{
          "decision": "approve" | "reject" | "clarify",
          "reasoning": "string — your detailed explanation",
          "alignmentScore": number (0-100),
          "completenessScore": number (0-100),
          "salesAlignmentScore": number (0-100),
          "clarificationQuestions": ["string"] | null,
          "suggestedPriority": "Low" | "Medium" | "High" | "Critical" | null,
          "tags": ["string"] | null,
          "isDuplicate": boolean,
          "duplicateOfRequestId": number | null
        }}
        """;

    public LlmService(IConfiguration configuration, IReferenceDocumentService refDocs, ILogger<LlmService> logger)
    {
        _refDocs = refDocs;
        _logger = logger;

        var endpoint = configuration["GitHubModels:Endpoint"] ?? "https://models.inference.ai.azure.com";
        _modelName = configuration["GitHubModels:ModelName"] ?? "gpt-4o";
        _temperature = float.Parse(configuration["ProductOwnerAgent:Temperature"] ?? "0.3");
        _maxTokens = int.Parse(configuration["ProductOwnerAgent:MaxTokens"] ?? "2000");

        // GitHub Models uses the GitHub PAT as the API key
        var apiKey = configuration["GitHub:PersonalAccessToken"]
            ?? throw new InvalidOperationException("GitHub:PersonalAccessToken is required for LLM access via GitHub Models.");

        var client = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });

        _chatClient = client.GetChatClient(_modelName);
    }

    public async Task<AgentReviewResult> ReviewRequestAsync(DevRequest request,
        List<RequestComment>? conversationHistory = null,
        List<DevRequest>? existingRequests = null)
    {
        var sw = Stopwatch.StartNew();

        var systemPrompt = string.Format(SystemPromptTemplate, _refDocs.GetSystemPromptContext());
        var userMessage = BuildUserMessage(request, conversationHistory, existingRequests);

        _logger.LogInformation("Reviewing request #{RequestId} '{Title}' via {Model}",
            request.Id, request.Title, _modelName);

        var messages = new List<ChatMessage>
        {
            new SystemChatMessage(systemPrompt),
            new UserChatMessage(userMessage)
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
