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
    public string? FeedbackResponse { get; init; }
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
    private readonly ISystemPromptService _systemPrompts;
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
        - For CSS/styling changes: include ALL CSS files (index.css, App.css, etc.) and ALL
          component files that render the affected UI, not just the "main" stylesheet
        - For UI issues: include EVERY component that could be affected, even if not
          explicitly mentioned — thoroughness prevents incomplete fixes
        - ALWAYS include index.css, App.css, and any global stylesheet — CSS issues often
          stem from global defaults (e.g. #root, body, html) overriding specific rules
        - When in doubt, include more files rather than fewer

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
        1. ROOT CAUSE FIRST: Before proposing any fix, identify and clearly state the ROOT CAUSE
           of the problem. Do not just treat symptoms. For example, if text is invisible, explain
           WHY (e.g., CSS variable override, wrong specificity, conflicting stylesheets) — not
           just "add a class to fix it". The solutionSummary MUST include the root cause.
        2. Ground your solution in the ACTUAL codebase you've been given — reference real files,
           classes, methods, and line numbers. Quote the specific code that causes the issue.
        3. Follow existing patterns (e.g., if the codebase uses controller + service + EF Core,
           don't propose a different architecture).
        4. If the request is ambiguous, include clarificationQuestions and set estimatedComplexity to "unknown".
        5. FILE PATHS MUST BE EXACT: Use the full path from the repository root, e.g.
           "src/AIDev.Api/AIDev.Api/Controllers/RequestsController.cs" not just "RequestsController.cs".
           The implementation agent relies entirely on the paths you provide. Wrong or partial
           paths will cause the implementation to fail. Double-check every path against the
           repository file tree provided above.
        6. If the request requires frontend + backend changes, cover both.
        7. Include data migration steps if any database schema changes are needed.
        8. Identify any breaking changes to existing API contracts.
        9. IMAGE ATTACHMENTS: If image attachments are provided with the request (e.g. screenshots,
           mockups, diagrams, logos, icons, new assets), carefully examine them and incorporate what
           you see into your solution. Describe any relevant visual elements, UI layouts, error messages,
           or design details in your "approach" and "solutionSummary" fields so that the implementation
           agent (which cannot see the images) has full context to work from. Be specific — mention
           colours, layouts, component placement, text content, error messages, etc.

           CRITICAL — ASSET ATTACHMENTS: When the request includes image attachments that are meant
           to be used AS assets in the project (e.g. a new logo, icon, or image to display), the
           "ATTACHMENTS" section in the user message below lists the EXACT file names and their
           staging paths in `_temp-attachments/{requestId}/`. Your solution MUST:
           a) List `_temp-attachments/{requestId}/{filename}` in impactedFiles with action "move"
              and a description saying where to move it (e.g. `src/AIDev.Web/src/assets/`).
           b) Include the destination file in impactedFiles or newFiles as appropriate.
           c) In implementationOrder, include an explicit step: "Move
              `_temp-attachments/{requestId}/{filename}` to `{destination}` and delete the
              `_temp-attachments/` folder."
           d) In the approach, describe the image (format, dimensions, what it depicts) so the
              implementation agent can verify it is the correct type of asset.
           e) List the code files that reference this asset and describe the exact change needed
              (e.g. update the import path or `src` attribute).
        10. COMPLETENESS: List ALL files that need changes, not just the primary ones.
            For CSS/styling: check every stylesheet and component file for hardcoded colours,
            conflicting variable definitions, specificity issues, and inline styles.
            The implementation agent will ONLY modify files you list — anything you miss stays broken.
        11. APPROACH DETAIL: The "approach" field should contain enough detail that a developer
            (or AI coding agent) can implement the fix without needing to re-investigate.
            Include specific CSS selectors, variable names, line numbers, and exact values.
        12. The impactedFiles "description" field should say EXACTLY what change is needed in that
            file — not vague descriptions like "update styles" but specific ones like
            "Remove conflicting --text variable definition on line 2 that overrides App.css".
        13. NEVER TRUST PRIOR OPINIONS BLINDLY: The Product Owner Agent may say a feature
            "already exists" or a request is "redundant". You MUST independently verify this
            claim by examining the actual code. If the code contradicts the PO's assessment
            (e.g. there IS a problem despite the PO saying it's "already fixed"), trust the
            code and the user's report, not the PO. Your job is to find the truth.
        14. CSS CASCADE ANALYSIS: For any CSS/styling issue, trace the FULL cascade from the
            root element down. Check #root, body, html, and :root for global defaults FIRST.
            A selector like `text-align: left` on `.some-class` does NOT prove the global
            default is correct — the issue may be in a different selector (e.g. #root in
            index.css setting `text-align: center`). Always identify the HIGHEST-LEVEL
            conflicting rule, not just the most specific one.
        15. IMAGE EVIDENCE VS CODE ANALYSIS: If an attached screenshot/image shows a visible
            bug (e.g. centered text, invisible text, wrong colours), and your code analysis
            suggests the issue is "already fixed" — your code analysis is WRONG. The image
            is ground truth. Re-examine the code more carefully to find what you missed.
            Never conclude "no changes needed" if a screenshot shows otherwise.
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ArchitectLlmService(
        ILlmClientFactory clientFactory,
        IReferenceDocumentService refDocs,
        ISystemPromptService systemPrompts,
        IConfiguration configuration,
        ILogger<ArchitectLlmService> logger)
    {
        _refDocs = refDocs;
        _systemPrompts = systemPrompts;
        _logger = logger;

        // Allow architect to use a different (stronger) model than the default
        _modelName = configuration["ArchitectAgent:ModelName"]
            ?? clientFactory.ModelName;
        _temperature = float.Parse(configuration["ArchitectAgent:Temperature"] ?? "0.2");
        _maxTokens = int.Parse(configuration["ArchitectAgent:MaxTokens"] ?? "4000");
        _maxFilesToRead = int.Parse(configuration["ArchitectAgent:MaxFilesToRead"] ?? "20");
        // Max input chars: gpt-4o supports 128K context. ~4 chars per token.
        var maxInputTokens = int.Parse(configuration["ArchitectAgent:MaxInputTokens"] ?? "6000");
        _maxInputChars = maxInputTokens * 4;

        _chatClient = clientFactory.CreateChatClient(_modelName);
        _logger.LogInformation("Architect agent using model: {Model}", _modelName);
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

        var step1SystemPrompt = _systemPrompts
            .GetPrompt(SystemPromptService.Keys.ArchitectFileSelection)
            .Replace("{0}", _maxFilesToRead.ToString());
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

        // ── Step 1b: Iterative File Lookup ──────────────────────
        // After reading the initial files, give the model a chance to request
        // additional files it discovered are needed (e.g. referenced services,
        // interfaces, or config files mentioned in the code it just read).

        if (fileContents.Count > 0 && selectedFiles.Count < _maxFilesToRead)
        {
            var remainingBudget = _maxFilesToRead - selectedFiles.Count;
            var step1bUserMessage = BuildIterativeFileSelectionMessage(
                request, repositoryMap, fileContents, selectedFiles, remainingBudget);

            var step1bMessages = new List<ChatMessage>
            {
                new SystemChatMessage(step1SystemPrompt),
                new UserChatMessage(step1bUserMessage)
            };

            try
            {
                ChatCompletion step1bCompletion = await _chatClient.CompleteChatAsync(step1bMessages, step1Options);
                var step1bResponse = step1bCompletion.Content[0].Text;
                step1PromptTokens += (int)(step1bCompletion.Usage?.InputTokenCount ?? 0);
                step1CompletionTokens += (int)(step1bCompletion.Usage?.OutputTokenCount ?? 0);

                var additionalFiles = ParseFileSelectionResponse(step1bResponse)
                    .Where(f => !selectedFiles.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .Take(remainingBudget)
                    .ToList();

                if (additionalFiles.Count > 0)
                {
                    _logger.LogInformation(
                        "Architect Step 1b — requesting {Count} additional files: {Files}",
                        additionalFiles.Count, string.Join(", ", additionalFiles));

                    var additionalContents = await fileReader(additionalFiles);
                    foreach (var (path, content) in additionalContents)
                        fileContents[path] = content;

                    selectedFiles.AddRange(additionalFiles);

                    _logger.LogInformation(
                        "Architect Step 1b complete — total files: {Count}, step1 tokens: {Tokens}",
                        fileContents.Count, step1PromptTokens + step1CompletionTokens);
                }
                else
                {
                    _logger.LogInformation("Architect Step 1b — no additional files needed");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Architect Step 1b failed — continuing with initial file selection");
            }
        }

        // ── Step 2: Solution Proposal ───────────────────────────

        _logger.LogInformation("Architect Step 2 — Solution Proposal for request #{Id}", request.Id);

        var fileContentsSerialized = BuildFileContentsBlock(fileContents);
        var referenceContext = _refDocs.GetSystemPromptContext();

        // Truncate content to fit within the model's input token limit.
        // Calculate overhead dynamically from the actual template + user message.
        var rawTemplate = _systemPrompts.GetPrompt(SystemPromptService.Keys.ArchitectSolution);
        // Template overhead = template length minus the 3 large placeholders ({0},{1},{2})
        // {3}-{6} will be replaced with small PO review strings, estimate ~300 chars.
        var templateOverhead = rawTemplate.Length - "{0}".Length - "{1}".Length - "{2}".Length + 300;
        var step2UserMessage = BuildSolutionUserMessage(request, conversationHistory, attachments);
        var userMessageOverhead = step2UserMessage.Length;
        // Images consume tokens too: ~765 tokens per image on vision models
        var imageCount = attachments?.Count(a => a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) ?? 0;
        var imageTokenCost = imageCount * 765 * 4; // convert to chars estimate

        var fixedOverhead = templateOverhead + userMessageOverhead + imageTokenCost;
        var availableForContent = Math.Max(1000, _maxInputChars - fixedOverhead);

        // Split budget: 40% reference context, 20% repo map, 40% file contents
        var maxRefChars = (int)(availableForContent * 0.4);
        var maxMapChars = (int)(availableForContent * 0.2);
        var maxFileChars = (int)(availableForContent * 0.4);

        _logger.LogInformation(
            "Step 2 budget — template: {Template}, userMsg: {UserMsg}, images: {Images}, available: {Available} chars",
            templateOverhead, userMessageOverhead, imageTokenCost, availableForContent);

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

        var step2SystemPrompt = _systemPrompts
            .GetPrompt(SystemPromptService.Keys.ArchitectSolution)
            .Replace("{0}", referenceContext)
            .Replace("{1}", repositoryMap)
            .Replace("{2}", fileContentsSerialized)
            .Replace("{3}", productOwnerReview.Decision.ToString())
            .Replace("{4}", productOwnerReview.Reasoning)
            .Replace("{5}", productOwnerReview.AlignmentScore.ToString())
            .Replace("{6}", productOwnerReview.CompletenessScore.ToString());

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
            // For file selection, focus on HUMAN feedback (most relevant for picking files)
            var humanFeedback = conversationHistory
                .Where(c => !c.IsAgentComment)
                .OrderBy(c => c.CreatedAt)
                .ToList();

            if (humanFeedback.Count > 0)
            {
                sb.AppendLine("HUMAN FEEDBACK (select files relevant to these points):");
                foreach (var comment in humanFeedback)
                {
                    sb.AppendLine($"  >> {comment.Content}");
                }
            }
            else
            {
                sb.AppendLine("PRIOR CONVERSATION:");
                foreach (var comment in conversationHistory.OrderBy(c => c.CreatedAt))
                {
                    var source = comment.IsAgentComment ? "Agent" : "Human";
                    // Truncate agent proposals to save tokens in file selection
                    var text = comment.IsAgentComment && comment.Content.Length > 300
                        ? comment.Content[..300] + "..."
                        : comment.Content;
                    sb.AppendLine($"[{source}] {text}");
                }
            }
        }

        return sb.ToString();
    }

    private static string BuildIterativeFileSelectionMessage(
        DevRequest request,
        string repositoryMap,
        Dictionary<string, string> fileContents,
        List<string> alreadySelected,
        int remainingBudget)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DEVELOPMENT REQUEST:");
        sb.AppendLine($"Title: {request.Title}");
        sb.AppendLine($"Type: {request.RequestType}");
        sb.AppendLine($"Description: {request.Description}");

        sb.AppendLine();
        sb.AppendLine("REPOSITORY MAP:");
        sb.AppendLine(repositoryMap);

        sb.AppendLine();
        sb.AppendLine("FILES ALREADY SELECTED AND READ:");
        foreach (var path in alreadySelected)
            sb.AppendLine($"  - {path}");

        sb.AppendLine();
        sb.AppendLine("CONTENTS OF SELECTED FILES (summary of what was found):");
        foreach (var (path, content) in fileContents.OrderBy(f => f.Key))
        {
            // Include first 500 chars so the model can see what's in each file
            var preview = content.Length > 500 ? content[..500] + "\n[...truncated]" : content;
            sb.AppendLine($"=== {path} ===");
            sb.AppendLine(preview);
            sb.AppendLine();
        }

        sb.AppendLine();
        sb.AppendLine($"TASK: Based on the code you've now read, do you need any ADDITIONAL files to design a complete solution?");
        sb.AppendLine($"You can select up to {remainingBudget} more files.");
        sb.AppendLine("Look for:");
        sb.AppendLine("- Services, interfaces, or classes referenced in the code you've read but not yet selected");
        sb.AppendLine("- Config files (appsettings.json, Program.cs) if the code references configuration");
        sb.AppendLine("- Related components, models, or DTOs that would be impacted");
        sb.AppendLine("- CSS/style files if UI components reference them");
        sb.AppendLine();
        sb.AppendLine("Return ONLY a JSON array of additional file paths (NOT files already selected).");
        sb.AppendLine("If no additional files are needed, return an empty array: []");

        return sb.ToString();
    }

    private static string BuildSolutionUserMessage(
        DevRequest request,
        List<RequestComment>? conversationHistory,
        List<Attachment>? attachments = null)
    {
        var sb = new StringBuilder();

        // Determine if this is a revision (has human feedback)
        var humanComments = conversationHistory?
            .Where(c => !c.IsAgentComment)
            .OrderBy(c => c.CreatedAt)
            .ToList();
        var agentComments = conversationHistory?
            .Where(c => c.IsAgentComment)
            .OrderBy(c => c.CreatedAt)
            .ToList();
        var isRevision = humanComments is { Count: > 0 };

        if (isRevision)
        {
            // REVISION MODE: Lead with human feedback so the model prioritises it
            sb.AppendLine("══════════════════════════════════════════════════════════");
            sb.AppendLine("THIS IS A REVISION. The human has reviewed your previous");
            sb.AppendLine("proposal and provided feedback. You MUST substantially");
            sb.AppendLine("change your design to address EVERY point below.");
            sb.AppendLine("DO NOT repeat the same solution — improve it.");
            sb.AppendLine("══════════════════════════════════════════════════════════");
            sb.AppendLine();
            sb.AppendLine("HUMAN FEEDBACK — ADDRESS EVERY POINT:");
            foreach (var comment in humanComments!)
            {
                sb.AppendLine($"  >> {comment.Content}");
            }
            sb.AppendLine();
            sb.AppendLine("Your 'feedbackResponse' field MUST directly answer each point above,");
            sb.AppendLine("explaining what you changed and why.");
            sb.AppendLine("══════════════════════════════════════════════════════════");
            sb.AppendLine();

            // Show previous proposal so the model knows what to improve
            if (agentComments is { Count: > 0 })
            {
                sb.AppendLine("YOUR PREVIOUS PROPOSAL (to revise — do NOT copy this unchanged):");
                // Show the last agent proposal in full (up to 2000 chars) for context
                var lastProposal = agentComments.Last();
                var proposalText = lastProposal.Content.Length > 2000
                    ? lastProposal.Content[..2000] + "\n[...truncated]"
                    : lastProposal.Content;
                sb.AppendLine(proposalText);
                sb.AppendLine();
            }
        }
        else
        {
            sb.AppendLine("Design a technical solution for the following request:");
        }

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

        // Include attachment metadata so the LLM knows file names and staging paths
        var imageAttachments = attachments?.Where(a => a.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)).ToList();
        if (imageAttachments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("ATTACHMENTS:");
            sb.AppendLine($"Image files for this request are staged in `_temp-attachments/{request.Id}/` in the repository.");
            foreach (var att in imageAttachments)
            {
                sb.AppendLine($"- `_temp-attachments/{request.Id}/{att.FileName}` ({att.ContentType}, {att.FileSizeBytes:N0} bytes)");
            }
            sb.AppendLine("These files are available for the implementation agent to move into the project.");
            sb.AppendLine("Your solution MUST include instructions to move them to the correct location and delete the `_temp-attachments/` folder.");
        }

        // For non-revision mode, include any prior proposals as context
        if (!isRevision && agentComments is { Count: > 0 })
        {
            sb.AppendLine();
            sb.AppendLine("PRIOR PROPOSALS (summaries of previous architect solutions):");
            foreach (var comment in agentComments)
            {
                var summary = comment.Content.Length > 500
                    ? comment.Content[..500] + "..."
                    : comment.Content;
                sb.AppendLine($"[Prior Proposal] {summary}");
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
                    FeedbackResponse = parsed.FeedbackResponse,
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
        public string? FeedbackResponse { get; set; }
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
