using System.Diagnostics;
using System.Text;
using System.Text.Json;
using AIDev.Api.Models;
using OpenAI.Chat;

namespace AIDev.Api.Services;

// ────────────────────────────────────────────────────────────
// Result types
// ────────────────────────────────────────────────────────────

public record ArchitectSolutionResult
{
    public required string SolutionSummary { get; init; }
    public required string Approach { get; init; }
    public required List<ImpactedFile> ImpactedFiles { get; init; }
    public required List<NewFile> NewFiles { get; init; }
    public required DataMigration DataMigration { get; init; }
    public required List<string> BreakingChanges { get; init; }
    public required List<DependencyChange> DependencyChanges { get; init; }
    public required List<Risk> Risks { get; init; }
    public required string EstimatedComplexity { get; init; }
    public required string EstimatedEffort { get; init; }
    public required List<string> ImplementationOrder { get; init; }
    public required string TestingNotes { get; init; }
    public required string ArchitecturalNotes { get; init; }
    public List<string>? ClarificationQuestions { get; init; }

    // LLM metadata
    public int Step1PromptTokens { get; init; }
    public int Step1CompletionTokens { get; init; }
    public int Step2PromptTokens { get; init; }
    public int Step2CompletionTokens { get; init; }
    public string ModelUsed { get; init; } = string.Empty;
    public int TotalDurationMs { get; init; }
    public List<string> FilesRead { get; init; } = new();
}

public record ImpactedFile(string Path, string Action, string Description, int EstimatedLinesChanged);
public record NewFile(string Path, string Description, int EstimatedLines);
public record DataMigration(bool Required, string? Description, List<string> Steps);
public record DependencyChange(string Package, string Action, string Version, string Reason);
public record Risk(string Description, string Severity, string Mitigation);

// ────────────────────────────────────────────────────────────
// Interface
// ────────────────────────────────────────────────────────────

/// <summary>
/// Orchestrates the two-step LLM interaction for architecture analysis.
/// </summary>
public interface IArchitectLlmService
{
    /// <summary>
    /// Analyse a triaged request and produce a solution proposal.
    /// Two-step process: file selection → solution generation.
    /// </summary>
    Task<ArchitectSolutionResult> AnalyseRequestAsync(
        DevRequest request,
        AgentReview productOwnerReview,
        string repositoryMap,
        Func<IEnumerable<string>, Task<Dictionary<string, string>>> fileReader,
        List<RequestComment>? conversationHistory = null,
        List<Attachment>? attachments = null);
}

// ────────────────────────────────────────────────────────────
// Implementation
// ────────────────────────────────────────────────────────────

public class ArchitectLlmService : IArchitectLlmService
{
    private readonly ChatClient _chatClient;
    private readonly IReferenceDocumentService _refDocs;
    private readonly ILogger<ArchitectLlmService> _logger;
    private readonly string _modelName;
    private readonly float _temperature;
    private readonly int _maxTokens;
    private readonly int _maxFilesToRead;
    private readonly int _maxInputChars;

    private const string FileSelectionSystemPrompt = """
        You are a Software Architect Agent for the AI Dev Pipeline platform.

        You have been given a development request that needs a technical solution.
        Below is the repository file tree with line counts.

        TASK: Identify which source files you need to read to design a solution
        for this request. Return a JSON array of file paths, ordered by relevance.

        Rules:
        - Select at most {0} files
        - Prioritise files directly relevant to the request (controllers, services, models)
        - Include configuration files if the change requires new settings
        - Include test files if the change needs new tests
        - Do NOT select binary files, migration files, lock files, or build outputs

        Return ONLY a JSON array of strings. No markdown, no code fences, no explanation.
        Example: ["src/AIDev.Api/AIDev.Api/Controllers/RequestsController.cs"]
        """;

    private const string SolutionProposalSystemPrompt = """
        You are a Software Architect Agent for the AI Dev Pipeline platform.

        PRODUCT CONTEXT:
        {0}

        CODEBASE CONTEXT:
        {1}

        SELECTED FILE CONTENTS:
        {2}

        PRODUCT OWNER ASSESSMENT:
        Decision: {3}
        Reasoning: {4}
        Alignment Score: {5}/100
        Completeness Score: {6}/100

        TASK: Design a technical solution for the development request below.

        RESPONSE FORMAT (strict JSON — no markdown, no code fences, just the JSON object):
        {{
          "solutionSummary": "2-3 sentence overview of the approach",
          "approach": "Detailed technical approach - what patterns to use, why",
          "impactedFiles": [
            {{
              "path": "src/AIDev.Api/AIDev.Api/Controllers/RequestsController.cs",
              "action": "modify",
              "description": "Add new GET endpoint for filtered request search",
              "estimatedLinesChanged": 25
            }}
          ],
          "newFiles": [
            {{
              "path": "src/AIDev.Api/AIDev.Api/Services/SearchService.cs",
              "description": "New service encapsulating search logic",
              "estimatedLines": 80
            }}
          ],
          "dataMigration": {{
            "required": false,
            "description": null,
            "steps": []
          }},
          "breakingChanges": [],
          "dependencyChanges": [
            {{
              "package": "SomePackage",
              "action": "add",
              "version": "1.2.3",
              "reason": "Required for full-text search"
            }}
          ],
          "risks": [
            {{
              "description": "The existing search endpoint may need deprecation",
              "severity": "low",
              "mitigation": "Add backward-compatible alias"
            }}
          ],
          "estimatedComplexity": "low | medium | high | unknown",
          "estimatedEffort": "e.g. 2-4 hours",
          "implementationOrder": [
            "1. Add new model/DTO",
            "2. Create service",
            "3. Add controller endpoint",
            "4. Update frontend API client",
            "5. Add UI component"
          ],
          "testingNotes": "Test with various filter combinations; verify pagination",
          "architecturalNotes": "Follows existing service pattern; no new patterns introduced",
          "clarificationQuestions": []
        }}

        RULES:
        1. Ground your solution in the ACTUAL codebase you've been given — reference real files, classes, and methods.
        2. Follow existing patterns (e.g., if the codebase uses controller + service + EF Core, don't propose a different architecture).
        3. If the request is ambiguous, include clarificationQuestions and set estimatedComplexity to "unknown".
        4. Be specific about file paths — use the exact paths from the repository map.
        5. If the request requires frontend + backend changes, cover both.
        6. Include data migration steps if any database schema changes are needed.
        7. Identify any breaking changes to existing API contracts.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ArchitectLlmService(
        ILlmClientFactory clientFactory,
        IReferenceDocumentService refDocs,
        IConfiguration configuration,
        ILogger<ArchitectLlmService> logger)
    {
        _refDocs = refDocs;
        _logger = logger;

        _modelName = clientFactory.ModelName;
        _temperature = float.Parse(configuration["ArchitectAgent:Temperature"] ?? "0.2");
        _maxTokens = int.Parse(configuration["ArchitectAgent:MaxTokens"] ?? "4000");
        _maxFilesToRead = int.Parse(configuration["ArchitectAgent:MaxFilesToRead"] ?? "20");
        // Max input chars: GitHub Models free tier allows 8K tokens for gpt-4o-mini.
        // Reserve space for output tokens. ~4 chars per token.
        var maxInputTokens = int.Parse(configuration["ArchitectAgent:MaxInputTokens"] ?? "6000");
        _maxInputChars = maxInputTokens * 4;

        _chatClient = clientFactory.CreateChatClient();
    }

    public async Task<ArchitectSolutionResult> AnalyseRequestAsync(
        DevRequest request,
        AgentReview productOwnerReview,
        string repositoryMap,
        Func<IEnumerable<string>, Task<Dictionary<string, string>>> fileReader,
        List<RequestComment>? conversationHistory = null,
        List<Attachment>? attachments = null)
    {
        var totalSw = Stopwatch.StartNew();

        // ── Step 1: File Selection ──────────────────────────────

        _logger.LogInformation("Architect Step 1 — File Selection for request #{Id} '{Title}'",
            request.Id, request.Title);

        var step1SystemPrompt = string.Format(FileSelectionSystemPrompt, _maxFilesToRead);
        var step1UserMessage = BuildFileSelectionUserMessage(request, repositoryMap, conversationHistory);

        var step1Messages = new List<ChatMessage>
        {
            new SystemChatMessage(step1SystemPrompt),
            new UserChatMessage(step1UserMessage)
        };

        var step1Options = new ChatCompletionOptions
        {
            Temperature = _temperature,
            MaxOutputTokenCount = 1000, // File list is small
        };

        ChatCompletion step1Completion = await _chatClient.CompleteChatAsync(step1Messages, step1Options);

        var step1Response = step1Completion.Content[0].Text;
        var step1PromptTokens = (int)(step1Completion.Usage?.InputTokenCount ?? 0);
        var step1CompletionTokens = (int)(step1Completion.Usage?.OutputTokenCount ?? 0);

        var selectedFiles = ParseFileSelectionResponse(step1Response);

        _logger.LogInformation("Architect Step 1 complete — selected {Count} files, {Tokens} tokens",
            selectedFiles.Count, step1PromptTokens + step1CompletionTokens);

        // ── Fetch selected files ────────────────────────────────

        var fileContents = await fileReader(selectedFiles);

        _logger.LogInformation("Fetched {Count}/{Requested} files from repository",
            fileContents.Count, selectedFiles.Count);

        // ── Step 2: Solution Proposal ───────────────────────────

        _logger.LogInformation("Architect Step 2 — Solution Proposal for request #{Id}", request.Id);

        var fileContentsSerialized = BuildFileContentsBlock(fileContents);
        var referenceContext = _refDocs.GetSystemPromptContext();

        // Truncate content to fit within the model's input token limit.
        // System prompt template + JSON example is ~2500 chars. Reserve that + output budget.
        var fixedOverhead = 3000; // system prompt structure + PO review fields
        var availableForContent = Math.Max(1000, _maxInputChars - fixedOverhead);

        // Split budget: 40% reference context, 20% repo map, 40% file contents
        var maxRefChars = (int)(availableForContent * 0.4);
        var maxMapChars = (int)(availableForContent * 0.2);
        var maxFileChars = (int)(availableForContent * 0.4);

        if (referenceContext.Length > maxRefChars)
        {
            referenceContext = referenceContext[..maxRefChars] + "\n[...truncated]";
            _logger.LogInformation("Truncated reference context to {Chars} chars", maxRefChars);
        }
        if (repositoryMap.Length > maxMapChars)
        {
            repositoryMap = repositoryMap[..maxMapChars] + "\n[...truncated]";
            _logger.LogInformation("Truncated repo map to {Chars} chars", maxMapChars);
        }
        if (fileContentsSerialized.Length > maxFileChars)
        {
            fileContentsSerialized = fileContentsSerialized[..maxFileChars] + "\n[...truncated]";
            _logger.LogInformation("Truncated file contents to {Chars} chars", maxFileChars);
        }

        _logger.LogInformation(
            "Step 2 prompt sizes — ref: {Ref}, map: {Map}, files: {Files}, total: {Total} chars",
            referenceContext.Length, repositoryMap.Length, fileContentsSerialized.Length,
            referenceContext.Length + repositoryMap.Length + fileContentsSerialized.Length + fixedOverhead);

        var step2SystemPrompt = string.Format(
            SolutionProposalSystemPrompt,
            referenceContext,
            repositoryMap,
            fileContentsSerialized,
            productOwnerReview.Decision,
            productOwnerReview.Reasoning,
            productOwnerReview.AlignmentScore,
            productOwnerReview.CompletenessScore);

        var step2UserMessage = BuildSolutionUserMessage(request, conversationHistory);

        // Build multimodal user message with text + any image attachments for Step 2
        var step2UserChatMessage = LlmService.BuildMultimodalUserMessage(step2UserMessage, attachments, _logger);

        var step2Messages = new List<ChatMessage>
        {
            new SystemChatMessage(step2SystemPrompt),
            step2UserChatMessage
        };

        var step2Options = new ChatCompletionOptions
        {
            Temperature = _temperature,
            MaxOutputTokenCount = _maxTokens,
        };

        ChatCompletion step2Completion = await _chatClient.CompleteChatAsync(step2Messages, step2Options);

        var step2Response = step2Completion.Content[0].Text;
        var step2PromptTokens = (int)(step2Completion.Usage?.InputTokenCount ?? 0);
        var step2CompletionTokens = (int)(step2Completion.Usage?.OutputTokenCount ?? 0);

        totalSw.Stop();

        _logger.LogInformation(
            "Architect Step 2 complete — {Tokens} tokens in {Duration}ms total",
            step2PromptTokens + step2CompletionTokens,
            totalSw.ElapsedMilliseconds);

        // ── Parse and return ────────────────────────────────────

        var result = ParseSolutionResponse(step2Response);

        return result with
        {
            Step1PromptTokens = step1PromptTokens,
            Step1CompletionTokens = step1CompletionTokens,
            Step2PromptTokens = step2PromptTokens,
            Step2CompletionTokens = step2CompletionTokens,
            ModelUsed = _modelName,
            TotalDurationMs = (int)totalSw.ElapsedMilliseconds,
            FilesRead = selectedFiles
        };
    }

    // ────────────────────────────────────────────────────────────
    // Helper methods
    // ────────────────────────────────────────────────────────────

    private static string BuildFileSelectionUserMessage(
        DevRequest request,
        string repositoryMap,
        List<RequestComment>? conversationHistory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DEVELOPMENT REQUEST:");
        sb.AppendLine($"Title: {request.Title}");
        sb.AppendLine($"Type: {request.RequestType}");
        sb.AppendLine($"Description: {request.Description}");

        if (!string.IsNullOrWhiteSpace(request.StepsToReproduce))
            sb.AppendLine($"Steps to Reproduce: {request.StepsToReproduce}");
        if (!string.IsNullOrWhiteSpace(request.ExpectedBehavior))
            sb.AppendLine($"Expected Behavior: {request.ExpectedBehavior}");
        if (!string.IsNullOrWhiteSpace(request.ActualBehavior))
            sb.AppendLine($"Actual Behavior: {request.ActualBehavior}");

        sb.AppendLine();
        sb.AppendLine("REPOSITORY MAP:");
        sb.AppendLine(repositoryMap);

        if (conversationHistory is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("PRIOR CONVERSATION:");
            foreach (var comment in conversationHistory.OrderBy(c => c.CreatedAt))
            {
                var source = comment.IsAgentComment ? "Agent" : "Human";
                sb.AppendLine($"[{source}] {comment.Content}");
            }
        }

        return sb.ToString();
    }

    private static string BuildSolutionUserMessage(
        DevRequest request,
        List<RequestComment>? conversationHistory)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Design a technical solution for the following request:");
        sb.AppendLine();
        sb.AppendLine($"Title: {request.Title}");
        sb.AppendLine($"Type: {request.RequestType}");
        sb.AppendLine($"Priority: {request.Priority}");
        sb.AppendLine($"Description: {request.Description}");

        if (!string.IsNullOrWhiteSpace(request.StepsToReproduce))
            sb.AppendLine($"Steps to Reproduce: {request.StepsToReproduce}");
        if (!string.IsNullOrWhiteSpace(request.ExpectedBehavior))
            sb.AppendLine($"Expected Behavior: {request.ExpectedBehavior}");
        if (!string.IsNullOrWhiteSpace(request.ActualBehavior))
            sb.AppendLine($"Actual Behavior: {request.ActualBehavior}");

        if (conversationHistory is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("CONVERSATION HISTORY (includes human feedback on prior proposals):");
            foreach (var comment in conversationHistory.OrderBy(c => c.CreatedAt))
            {
                var source = comment.IsAgentComment ? "Agent" : "Human";
                sb.AppendLine($"[{source}] {comment.Content}");
            }
        }

        return sb.ToString();
    }

    private static string BuildFileContentsBlock(Dictionary<string, string> fileContents)
    {
        if (fileContents.Count == 0) return "(No files could be read)";

        var sb = new StringBuilder();
        foreach (var (path, content) in fileContents.OrderBy(f => f.Key))
        {
            sb.AppendLine($"=== {path} ===");
            sb.AppendLine(content);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private List<string> ParseFileSelectionResponse(string response)
    {
        try
        {
            var json = StripCodeFences(response);
            var files = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
            if (files != null)
            {
                // Cap at max files
                return files.Take(_maxFilesToRead).ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse file selection response: {Response}", response);
        }

        return new List<string>();
    }

    private ArchitectSolutionResult ParseSolutionResponse(string response)
    {
        try
        {
            var json = StripCodeFences(response);
            var parsed = JsonSerializer.Deserialize<SolutionResponseDto>(json, JsonOptions);

            if (parsed != null)
            {
                return new ArchitectSolutionResult
                {
                    SolutionSummary = parsed.SolutionSummary ?? "No summary provided",
                    Approach = parsed.Approach ?? "No approach provided",
                    ImpactedFiles = parsed.ImpactedFiles?.Select(f =>
                        new ImpactedFile(f.Path ?? "", f.Action ?? "modify", f.Description ?? "", f.EstimatedLinesChanged))
                        .ToList() ?? new(),
                    NewFiles = parsed.NewFiles?.Select(f =>
                        new NewFile(f.Path ?? "", f.Description ?? "", f.EstimatedLines))
                        .ToList() ?? new(),
                    DataMigration = parsed.DataMigration != null
                        ? new DataMigration(parsed.DataMigration.Required, parsed.DataMigration.Description, parsed.DataMigration.Steps ?? new())
                        : new DataMigration(false, null, new()),
                    BreakingChanges = parsed.BreakingChanges ?? new(),
                    DependencyChanges = parsed.DependencyChanges?.Select(d =>
                        new DependencyChange(d.Package ?? "", d.Action ?? "", d.Version ?? "", d.Reason ?? ""))
                        .ToList() ?? new(),
                    Risks = parsed.Risks?.Select(r =>
                        new Risk(r.Description ?? "", r.Severity ?? "unknown", r.Mitigation ?? ""))
                        .ToList() ?? new(),
                    EstimatedComplexity = parsed.EstimatedComplexity ?? "unknown",
                    EstimatedEffort = parsed.EstimatedEffort ?? "unknown",
                    ImplementationOrder = parsed.ImplementationOrder ?? new(),
                    TestingNotes = parsed.TestingNotes ?? "",
                    ArchitecturalNotes = parsed.ArchitecturalNotes ?? "",
                    ClarificationQuestions = parsed.ClarificationQuestions
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse solution response: {Response}",
                response[..Math.Min(500, response.Length)]);
        }

        // Fallback for unparseable responses
        return new ArchitectSolutionResult
        {
            SolutionSummary = "Agent response could not be parsed.",
            Approach = $"Raw response: {response[..Math.Min(1000, response.Length)]}",
            ImpactedFiles = new(),
            NewFiles = new(),
            DataMigration = new DataMigration(false, null, new()),
            BreakingChanges = new(),
            DependencyChanges = new(),
            Risks = new() { new Risk("Agent response was unparseable", "high", "Human review required") },
            EstimatedComplexity = "unknown",
            EstimatedEffort = "unknown",
            ImplementationOrder = new(),
            TestingNotes = "",
            ArchitecturalNotes = "",
            ClarificationQuestions = new() { "The automated analysis encountered an issue. A human architect should review this request." }
        };
    }

    private static string StripCodeFences(string text)
    {
        var json = text.Trim();
        if (json.StartsWith("```"))
        {
            var firstNewline = json.IndexOf('\n');
            var lastFence = json.LastIndexOf("```");
            if (firstNewline > 0 && lastFence > firstNewline)
                json = json[(firstNewline + 1)..lastFence].Trim();
        }
        return json;
    }

    // ────────────────────────────────────────────────────────────
    // DTOs for JSON deserialization
    // ────────────────────────────────────────────────────────────

    private class SolutionResponseDto
    {
        public string? SolutionSummary { get; set; }
        public string? Approach { get; set; }
        public List<ImpactedFileDto>? ImpactedFiles { get; set; }
        public List<NewFileDto>? NewFiles { get; set; }
        public DataMigrationDto? DataMigration { get; set; }
        public List<string>? BreakingChanges { get; set; }
        public List<DependencyChangeDto>? DependencyChanges { get; set; }
        public List<RiskDto>? Risks { get; set; }
        public string? EstimatedComplexity { get; set; }
        public string? EstimatedEffort { get; set; }
        public List<string>? ImplementationOrder { get; set; }
        public string? TestingNotes { get; set; }
        public string? ArchitecturalNotes { get; set; }
        public List<string>? ClarificationQuestions { get; set; }
    }

    private class ImpactedFileDto
    {
        public string? Path { get; set; }
        public string? Action { get; set; }
        public string? Description { get; set; }
        public int EstimatedLinesChanged { get; set; }
    }

    private class NewFileDto
    {
        public string? Path { get; set; }
        public string? Description { get; set; }
        public int EstimatedLines { get; set; }
    }

    private class DataMigrationDto
    {
        public bool Required { get; set; }
        public string? Description { get; set; }
        public List<string>? Steps { get; set; }
    }

    private class DependencyChangeDto
    {
        public string? Package { get; set; }
        public string? Action { get; set; }
        public string? Version { get; set; }
        public string? Reason { get; set; }
    }

    private class RiskDto
    {
        public string? Description { get; set; }
        public string? Severity { get; set; }
        public string? Mitigation { get; set; }
    }
}
