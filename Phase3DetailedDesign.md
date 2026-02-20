# Phase 3 — Architect Agent: Detailed Design

## 1. Overview

The Architect Agent is an AI agent that picks up Product-Owner-approved requests (status = `Triaged`) and produces a technical solution proposal. It reads the target repository's codebase via the GitHub API, analyses the request in context, and outputs a structured solution with file-level impact analysis, migration needs, risk assessment, and implementation guidance. The solution is posted for human review; once a human approves it, the request advances to `Approved` and is ready for Phase 4 (implementation via GitHub Copilot Coding Agent — see [Phase4DetailedDesign.md](Phase4DetailedDesign.md)).

### 1.1 Goals

| Goal | Description |
|------|-------------|
| Codebase-aware solutions | Agent reads actual source files from the target repo to ground its proposals in reality |
| Impact analysis | Identifies every file to create/modify/delete, data migration needs, breaking changes, dependency additions |
| Human checkpoint | No code is written until a human reviews and approves the proposed solution |
| Consistency | Maintains architectural patterns even across parallel work streams |
| Conversational refinement | Humans can ask the agent questions or request changes before approving |

### 1.2 Non-Goals (Phase 3)

- The agent does **not** write implementation code (that’s Phase 4 — GitHub Copilot Coding Agent)
- The agent does **not** create branches or PRs
- The agent does **not** modify the codebase directly

---

## 2. Agent Lifecycle

```
                              ┌─────────────────────────┐
                              │  GitHub Repository API   │
                              │  (file tree + contents)  │
                              └───────────┬─────────────┘
                                          │ reads codebase
                                          │
┌──────────────┐  Status=Triaged  ┌───────▼───────────┐    ┌──────────────────┐
│  DevRequest  │ ────────────────▶│  Architect Agent   │───▶│  GitHub Models   │
│  (Triaged)   │                  │  Service           │    │  (GPT-4o)        │
└──────────────┘                  └───────┬───────────┘    └──────────────────┘
                                          │
                         ┌────────────────┼──────────────────┐
                         ▼                ▼                  ▼
                  ┌─────────────┐  ┌───────────────┐  ┌──────────────┐
                  │  Propose    │  │  Clarify      │  │  Escalate    │
                  │  Solution   │  │  Post question│  │  Cannot solve│
                  │  Status →   │  │  Status →     │  │  Status →    │
                  │  ArchReview │  │  ArchReview   │  │  ArchReview  │
                  └──────┬──────┘  └───────────────┘  └──────────────┘
                         │
                         ▼
                  Human reviews
                  solution proposal
                         │
              ┌──────────┼──────────┐
              ▼          ▼          ▼
         Approve    Request      Reject
         Status →   Changes      Status →
         Approved   Agent revises Triaged (back
                    proposal      to PO queue)
```

---

## 3. Trigger Mechanism

The Architect Agent follows the same **polling BackgroundService** pattern established by the Product Owner Agent.

| Trigger | Condition |
|---------|-----------|
| PO-approved request | `Status = Triaged`, `ArchitectReviewCount == 0` |
| Human feedback on proposal | `Status = ArchitectReview`, new human comment since `LastArchitectReviewAt` |
| Manual re-review | API endpoint resets request to `Triaged` for fresh architecture analysis |

**Polling interval:** Configurable (default 60 seconds — longer than PO agent because architecture analysis is heavier).

**Batch size:** 3 per cycle (lower than PO agent's 5 because each takes more tokens / time).

---

## 4. Status Model Changes

### 4.1 New Status Values

The `RequestStatus` enum gains one new value:

```csharp
public enum RequestStatus
{
    New,                  // Submitted, awaiting PO triage
    NeedsClarification,   // PO Agent needs more info
    Triaged,              // PO approved → awaiting architecture
    ArchitectReview,      // Architect proposal posted, awaiting human review   ← NEW
    Approved,             // Human approved architecture → ready for Phase 4 (Copilot Coding Agent)
    InProgress,           // Being implemented (Phase 4 — Copilot Coding Agent)
    Done,                 // Deployed to UAT
    Rejected              // Rejected by PO or human
}
```

### 4.2 Status Flow

```
New → NeedsClarification → Triaged → ArchitectReview → Approved → InProgress → Done
  ↘ Rejected                          ↗ (human rejects  ↗
                                        back to Triaged)
```

| Transition | Triggered By |
|-----------|--------------|
| `Triaged` → `ArchitectReview` | Architect Agent posts solution proposal |
| `ArchitectReview` → `Approved` | Human approves the solution |
| `ArchitectReview` → `Triaged` | Human rejects — back to architecture queue (with feedback) |
| `ArchitectReview` → `ArchitectReview` | Agent revises proposal after human feedback (re-post) |

---

## 5. Codebase Reading Strategy

The Architect Agent needs to understand the target repository's codebase to produce grounded solutions. This is the most complex part of Phase 3.

### 5.1 GitHub API Integration

Use the existing Octokit client (already configured with PAT) to read repository content:

| Operation | Octokit Method | Purpose |
|-----------|---------------|---------|
| Get file tree | `client.Git.Tree.GetRecursive(owner, repo, "main")` | Full repository file listing |
| Read file content | `client.Repository.Content.GetAllContents(owner, repo, path)` | Individual file contents |
| Get default branch | `client.Repository.Get(owner, repo)` | Determine HEAD branch |

### 5.2 Two-Phase Codebase Context

Because LLMs have token limits, the agent cannot send the entire codebase. Instead, it uses a **two-phase approach**:

#### Phase A — Repository Map (always included)

A compact structural map of the entire repository tree, filtered to relevant source files:

```
PROJECT STRUCTURE:
src/AIDev.Api/AIDev.Api/
  Controllers/
    AdminController.cs (45 lines)
    AgentController.cs (260 lines)
    DashboardController.cs (82 lines)
    RequestsController.cs (180 lines)
  Models/
    AgentReview.cs (42 lines)
    DevRequest.cs (48 lines)
    Enums.cs (8 lines)
    ...
  Services/
    GitHubService.cs (260 lines)
    LlmService.cs (324 lines)
    ...
src/AIDev.Web/src/
  components/
    AdminSettings.tsx (215 lines)
    ...
  services/
    api.ts (380 lines)
```

**Filter rules:**
- Include: `.cs`, `.ts`, `.tsx`, `.json` (config), `.csproj`, `.md` (docs)
- Exclude: `bin/`, `obj/`, `node_modules/`, `.git/`, migration files, `*.db`, lock files, build outputs
- Show file sizes in lines to help the LLM judge complexity

**Estimated tokens:** ~500–1,500 tokens for the map (varies with repo size).

#### Phase B — Targeted File Contents (request-specific)

The agent uses a **two-step LLM call**:

1. **Step 1 — File Selection:** Send the repository map + request details to the LLM. Ask it to return a JSON list of files it needs to read (max 20 files, priority-ranked).

2. **Step 2 — Solution Proposal:** Send the selected file contents + request details + reference docs. Ask for the structured solution.

This avoids blindly sending irrelevant files and keeps context focused.

```
┌───────────────┐      ┌─────────────────┐      ┌────────────────┐
│ Repo Map +    │ LLM  │ "I need to read │ API  │ File contents  │
│ Request       │─────▶│ these 12 files" │─────▶│ fetched from   │
│ details       │ #1   │ (JSON list)     │      │ GitHub API     │
└───────────────┘      └─────────────────┘      └───────┬────────┘
                                                        │
                                              ┌─────────▼─────────┐
                                              │ File contents +   │
                                              │ Request + RefDocs │
                                              │ → Solution        │
                                              │   proposal        │ LLM #2
                                              └───────────────────┘
```

### 5.3 File Content Budget

| Budget Item | Token Estimate |
|-------------|---------------|
| Repository map | ~1,000 |
| System prompt + reference docs | ~4,000 |
| Request details + PO review | ~500 |
| File contents (up to 20 files) | ~12,000 |
| Reserved for output | ~4,000 |
| **Total budget** | **~21,500** |

**Free-tier limit (GitHub Models):** 8,000 input tokens. On free tier, the agent will:
- Reduce files requested to max 8
- Truncate large files to first 200 lines + last 50 lines
- Prioritise smaller, more relevant files

**Paid tier:** Full 128K context window — can include more files and complete contents.

### 5.4 Codebase Cache

To avoid hitting the GitHub API on every review:

| Strategy | Detail |
|----------|--------|
| **Repo map cache** | Cache the file tree per project for 15 minutes. Invalidate on config or manual refresh. |
| **File content cache** | Cache individual file contents with SHA-based keys. GitHub API returns file SHA — only re-fetch if SHA changes. |
| **Cache storage** | In-memory `ConcurrentDictionary` with TTL expiry (same pattern as `ReferenceDocumentService`). |

---

## 6. LLM Integration

### 6.1 Architecture-Specific LLM Service

Rather than modifying the existing `LlmService` (which is Product-Owner-specific), Phase 3 introduces a new `IArchitectLlmService` with its own prompts and response contract. Both services share the underlying `ChatClient` via a shared `ILlmClientFactory`.

### 6.2 LLM Client Factory

Extract the `ChatClient` creation from `LlmService` into a shared factory:

```csharp
public interface ILlmClientFactory
{
    ChatClient CreateChatClient();
}

public class LlmClientFactory : ILlmClientFactory
{
    private readonly ChatClient _client;

    public LlmClientFactory(IConfiguration config)
    {
        var endpoint = config["GitHubModels:Endpoint"]
            ?? "https://models.inference.ai.azure.com";
        var apiKey = config["GitHub:PersonalAccessToken"]
            ?? throw new InvalidOperationException("GitHub PAT not configured");
        var modelName = config["GitHubModels:ModelName"] ?? "gpt-4o";

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions { Endpoint = new Uri(endpoint) });
        _client = openAiClient.GetChatClient(modelName);
    }

    public ChatClient CreateChatClient() => _client;
}
```

Both `LlmService` (PO Agent) and `ArchitectLlmService` consume `ILlmClientFactory`.

### 6.3 System Prompt — File Selection (Step 1)

```
You are a Software Architect Agent for the AI Dev Pipeline platform.

You have been given a development request that needs a technical solution.
Below is the repository file tree with line counts.

TASK: Identify which source files you need to read to design a solution
for this request. Return a JSON array of file paths, ordered by relevance.

Rules:
- Select at most {maxFiles} files
- Prioritise files directly relevant to the request (controllers, services, models)
- Include configuration files if the change requires new settings
- Include test files if the change needs new tests
- Do NOT select binary files, migration files, lock files, or build outputs

Return ONLY a JSON array of strings:
["src/AIDev.Api/AIDev.Api/Controllers/RequestsController.cs", ...]
```

### 6.4 System Prompt — Solution Proposal (Step 2)

```
You are a Software Architect Agent for the AI Dev Pipeline platform.

PRODUCT CONTEXT:
{applicationObjectives}
{applicationSalesPack}

CODEBASE CONTEXT:
{repositoryMap}
{selectedFileContents}

PRODUCT OWNER ASSESSMENT:
Decision: {poDecision}
Reasoning: {poReasoning}
Alignment Score: {alignmentScore}/100
Completeness Score: {completenessScore}/100

TASK: Design a technical solution for the development request below.

RESPONSE FORMAT (strict JSON):
{
  "solutionSummary": "2-3 sentence overview of the approach",
  "approach": "Detailed technical approach - what patterns to use, why",
  "impactedFiles": [
    {
      "path": "src/AIDev.Api/AIDev.Api/Controllers/RequestsController.cs",
      "action": "modify",
      "description": "Add new GET endpoint for filtered request search",
      "estimatedLinesChanged": 25
    }
  ],
  "newFiles": [
    {
      "path": "src/AIDev.Api/AIDev.Api/Services/SearchService.cs",
      "description": "New service encapsulating search logic",
      "estimatedLines": 80
    }
  ],
  "dataMigration": {
    "required": false,
    "description": null,
    "steps": []
  },
  "breakingChanges": [],
  "dependencyChanges": [
    {
      "package": "SomePackage",
      "action": "add",
      "version": "1.2.3",
      "reason": "Required for full-text search"
    }
  ],
  "risks": [
    {
      "description": "The existing search endpoint may need deprecation",
      "severity": "low",
      "mitigation": "Add backward-compatible alias"
    }
  ],
  "estimatedComplexity": "medium",
  "estimatedEffort": "2-4 hours",
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
}

RULES:
1. Ground your solution in the ACTUAL codebase you've been given — reference real files, classes, and methods.
2. Follow existing patterns (e.g., if the codebase uses controller + service + EF Core, don't propose a different architecture).
3. If the request is ambiguous, include clarificationQuestions and set estimatedComplexity to "unknown".
4. Be specific about file paths — use the exact paths from the repository map.
5. If the request requires frontend + backend changes, cover both.
6. Include data migration steps if any database schema changes are needed.
7. Identify any breaking changes to existing API contracts.
```

### 6.5 Response Model

```csharp
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
```

---

## 7. Data Model Changes

### 7.1 New Entity: ArchitectReview

```csharp
public class ArchitectReview
{
    public int Id { get; set; }
    public int DevRequestId { get; set; }
    public DevRequest? DevRequest { get; set; }

    [Required] [MaxLength(50)]
    public string AgentType { get; set; } = "Architect";

    // Solution proposal (stored as JSON for flexibility)
    [Required]
    public string SolutionSummary { get; set; } = string.Empty;

    [Required]
    public string Approach { get; set; } = string.Empty;

    /// <summary>Full solution JSON (ImpactedFiles, NewFiles, Risks, etc.)</summary>
    [Required]
    public string SolutionJson { get; set; } = string.Empty;

    public string EstimatedComplexity { get; set; } = string.Empty;
    public string EstimatedEffort { get; set; } = string.Empty;

    // Codebase context metadata
    public int FilesAnalysed { get; set; }

    /// <summary>JSON array of file paths that were read</summary>
    public string? FilesReadJson { get; set; }

    // Human review
    public ArchitectDecision Decision { get; set; } = ArchitectDecision.Pending;
    public string? HumanFeedback { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }

    // LLM token tracking (two-step)
    public int Step1PromptTokens { get; set; }
    public int Step1CompletionTokens { get; set; }
    public int Step2PromptTokens { get; set; }
    public int Step2CompletionTokens { get; set; }

    [Required] [MaxLength(100)]
    public string ModelUsed { get; set; } = string.Empty;

    public int TotalDurationMs { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public List<RequestComment> Comments { get; set; } = new();
}
```

### 7.2 New Enum: ArchitectDecision

```csharp
public enum ArchitectDecision
{
    Pending,    // Proposal posted, awaiting human review
    Approved,   // Human approved — ready for implementation
    Rejected,   // Human rejected — back to Triaged for re-analysis
    Revised     // Agent has posted a revised proposal
}
```

### 7.3 DevRequest Changes

| New Field | Type | Description |
|-----------|------|-------------|
| `LastArchitectReviewAt` | `DateTime?` | When the architect agent last reviewed |
| `ArchitectReviewCount` | `int` | Number of architect reviews (prevents infinite loops) |

```csharp
// Add to DevRequest.cs
public DateTime? LastArchitectReviewAt { get; set; }
public int ArchitectReviewCount { get; set; } = 0;
public List<ArchitectReview> ArchitectReviews { get; set; } = new();
```

### 7.4 RequestComment Changes

The existing `AgentReviewId` (nullable FK to `AgentReview`) remains for Product Owner comments. Add a parallel FK for Architect comments:

| New Field | Type | Description |
|-----------|------|-------------|
| `ArchitectReviewId` | `int?` (FK) | Links to the `ArchitectReview` that generated this comment |

### 7.5 AppDbContext Changes

```csharp
public DbSet<ArchitectReview> ArchitectReviews => Set<ArchitectReview>();

// In OnModelCreating:
modelBuilder.Entity<ArchitectReview>()
    .Property(r => r.Decision)
    .HasConversion<string>();

modelBuilder.Entity<ArchitectReview>()
    .HasOne(r => r.DevRequest)
    .WithMany(d => d.ArchitectReviews)
    .HasForeignKey(r => r.DevRequestId)
    .OnDelete(DeleteBehavior.Cascade);
```

### 7.6 EF Migration

Single migration: `AddArchitectAgent`
- Add `ArchitectReview` table
- Add `LastArchitectReviewAt`, `ArchitectReviewCount` columns to `DevRequests`
- Add `ArchitectReviewId` nullable FK column to `RequestComments`

---

## 8. New Backend Services

### 8.1 Services/CodebaseService.cs

Responsible for reading and caching repository content from GitHub.

```csharp
public interface ICodebaseService
{
    /// <summary>
    /// Get a compact text representation of the repo file tree with line counts.
    /// Cached per project for 15 minutes.
    /// </summary>
    Task<string> GetRepositoryMapAsync(string owner, string repo);

    /// <summary>
    /// Read the contents of specific files from the repository.
    /// Cached by file SHA.
    /// </summary>
    Task<Dictionary<string, string>> GetFileContentsAsync(
        string owner, string repo, IEnumerable<string> filePaths);

    /// <summary>
    /// Invalidate all caches for a repository.
    /// </summary>
    void InvalidateCache(string owner, string repo);
}
```

**Implementation details:**

- Uses `IGitHubService` (Octokit) under the hood — extends the existing interface with `GetTreeRecursiveAsync` and `GetFileContentAsync` methods
- **File tree filtering:**
  - Include: `*.cs`, `*.ts`, `*.tsx`, `*.js`, `*.jsx`, `*.json`, `*.csproj`, `*.md`, `*.css`, `*.html`, `*.yaml`, `*.yml`
  - Exclude patterns: `bin/`, `obj/`, `node_modules/`, `.git/`, `Migrations/`, `*.db`, `*.lock`, `*.min.js`, `*.map`, `wwwroot/lib/`
- **Line counting:** For the repository map, fetch file size in bytes from the tree API (GitHub provides `size`). Estimate lines as `size / 40` (average line length).
- **Concurrent file fetching:** Use `Task.WhenAll` with throttling (`SemaphoreSlim(5)`) to fetch multiple files in parallel without overwhelming the API.

### 8.2 Services/ArchitectLlmService.cs

Orchestrates the two-step LLM interaction for architecture analysis.

```csharp
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
        List<RequestComment>? conversationHistory = null);
}
```

**Implementation:**

1. Inject `ILlmClientFactory` (shared), `IReferenceDocumentService`, `IConfiguration`, `ILogger`
2. **Step 1 — File Selection:**
   - Build system prompt with repo map + request details
   - Call LLM → parse JSON array of file paths
   - Track tokens for Step 1
3. **Fetch files** using the provided `fileReader` delegate (from `CodebaseService`)
4. **Step 2 — Solution Proposal:**
   - Build system prompt with reference docs + file contents + PO review context
   - Call LLM → parse JSON solution into `ArchitectSolutionResult`
   - Track tokens for Step 2
5. Return combined result with total timing and token counts

**Configuration** (new section in `appsettings.json`):

```json
{
  "ArchitectAgent": {
    "Enabled": true,
    "PollingIntervalSeconds": 60,
    "MaxReviewsPerRequest": 3,
    "MaxFilesToRead": 20,
    "MaxFileContentChars": 50000,
    "Temperature": 0.2,
    "MaxTokens": 4000,
    "DailyTokenBudget": 0,
    "MonthlyTokenBudget": 0
  }
}
```

### 8.3 Services/ArchitectAgentService.cs (BackgroundService)

Follows the exact pattern of `ProductOwnerAgentService`:

```csharp
public class ArchitectAgentService : BackgroundService
{
    // Dependencies: IServiceScopeFactory, IArchitectLlmService,
    //   ICodebaseService, IGitHubService, ILogger, IConfiguration

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Same pattern: startup delay → poll loop → ProcessPendingRequestsAsync
    }

    private async Task ProcessPendingRequestsAsync(CancellationToken ct)
    {
        // 1. Check token budget (daily/monthly)
        // 2. Query candidates:
        //    a. Status = Triaged, ArchitectReviewCount == 0
        //    b. Status = ArchitectReview, new human comment since LastArchitectReviewAt,
        //       ArchitectReviewCount < MaxReviewsPerRequest
        // 3. Take top 3, process each independently
    }

    private async Task AnalyseRequestAsync(AppDbContext db, DevRequest request, CancellationToken ct)
    {
        // 1. Get latest PO review for context
        // 2. Get repository map from CodebaseService
        // 3. Call ArchitectLlmService.AnalyseRequestAsync
        // 4. Create ArchitectReview record
        // 5. Create formatted agent comment with solution summary
        // 6. Set Status = ArchitectReview
        // 7. Update LastArchitectReviewAt, increment ArchitectReviewCount
        // 8. Post comment + label to GitHub Issue (agent:architect-review)
    }
}
```

---

## 9. API Endpoints

### 9.1 New Endpoints on AgentController (or new ArchitectController)

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/architect/reviews` | List architect reviews (filter by requestId, decision) |
| `GET` | `/api/architect/reviews/{id}` | Single architect review with full solution JSON |
| `POST` | `/api/architect/reviews/{id}/approve` | Human approves solution → status = `Approved` |
| `POST` | `/api/architect/reviews/{id}/reject` | Human rejects → status back to `Triaged` with feedback |
| `POST` | `/api/architect/reviews/{id}/feedback` | Human posts feedback for agent to revise |
| `POST` | `/api/architect/reviews/re-analyse/{requestId}` | Manual re-analysis trigger |
| `GET` | `/api/architect/config` | Current architect agent configuration |
| `PUT` | `/api/architect/config` | Update architect agent config at runtime |
| `GET` | `/api/architect/budget` | Token budget status for architect agent |
| `GET` | `/api/architect/stats` | Aggregate architect stats |

### 9.2 Endpoint Details

#### POST `/api/architect/reviews/{id}/approve`

```json
// Request body
{
  "reason": "Solution looks good, proceed with implementation"
}

// Response: updated ArchitectReviewResponseDto
```

- Sets `ArchitectReview.Decision = Approved`, `ApprovedBy = currentUser`, `ApprovedAt = utcNow`
- Sets `DevRequest.Status = Approved`
- Posts comment: "Solution approved by {user}: {reason}"
- Updates GitHub Issue label → `agent:approved-solution`

#### POST `/api/architect/reviews/{id}/reject`

```json
{
  "reason": "The approach doesn't account for backward compatibility with the existing API clients"
}
```

- Sets `ArchitectReview.Decision = Rejected`
- Sets `DevRequest.Status = Triaged` (back to architecture queue)
- Posts comment with rejection reason
- Agent will re-analyse on next poll cycle (if under max review count)

#### POST `/api/architect/reviews/{id}/feedback`

```json
{
  "feedback": "Can you explore using the existing SearchService instead of creating a new one?"
}
```

- Posts feedback as a human comment on the request (linked to the ArchitectReview)
- Does NOT change status — agent detects new comment on next poll and revises its proposal
- Increment context: the conversation history is included in the next LLM call

### 9.3 DTOs

```csharp
public class ArchitectReviewResponseDto
{
    public int Id { get; set; }
    public int DevRequestId { get; set; }
    public string RequestTitle { get; set; } = string.Empty;
    public string SolutionSummary { get; set; } = string.Empty;
    public string Approach { get; set; } = string.Empty;
    public List<ImpactedFileDto> ImpactedFiles { get; set; } = new();
    public List<NewFileDto> NewFiles { get; set; } = new();
    public DataMigrationDto DataMigration { get; set; } = new();
    public List<string> BreakingChanges { get; set; } = new();
    public List<DependencyChangeDto> DependencyChanges { get; set; } = new();
    public List<RiskDto> Risks { get; set; } = new();
    public string EstimatedComplexity { get; set; } = string.Empty;
    public string EstimatedEffort { get; set; } = string.Empty;
    public List<string> ImplementationOrder { get; set; } = new();
    public string TestingNotes { get; set; } = string.Empty;
    public string ArchitecturalNotes { get; set; } = string.Empty;
    public ArchitectDecision Decision { get; set; }
    public string? HumanFeedback { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public int FilesAnalysed { get; set; }
    public int TotalTokensUsed { get; set; }
    public string ModelUsed { get; set; } = string.Empty;
    public int TotalDurationMs { get; set; }
    public DateTime CreatedAt { get; set; }
}

public record ImpactedFileDto(string Path, string Action, string Description, int EstimatedLinesChanged);
public record NewFileDto(string Path, string Description, int EstimatedLines);
public record DataMigrationDto(bool Required, string? Description, List<string> Steps);
public record DependencyChangeDto(string Package, string Action, string Version, string Reason);
public record RiskDto(string Description, string Severity, string Mitigation);

public class ArchitectApprovalDto
{
    public string? Reason { get; set; }
}

public class ArchitectFeedbackDto
{
    [Required]
    public string Feedback { get; set; } = string.Empty;
}

public class ArchitectConfigDto
{
    public bool Enabled { get; set; }
    public int PollingIntervalSeconds { get; set; }
    public int MaxReviewsPerRequest { get; set; }
    public int MaxFilesToRead { get; set; }
    public float Temperature { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public int DailyTokenBudget { get; set; }
    public int MonthlyTokenBudget { get; set; }
}

public class ArchitectStatsDto
{
    public int TotalAnalyses { get; set; }
    public int PendingReview { get; set; }
    public int Approved { get; set; }
    public int Rejected { get; set; }
    public int Revised { get; set; }
    public double AverageFilesAnalysed { get; set; }
    public int TotalTokensUsed { get; set; }
    public double AverageDurationMs { get; set; }
}
```

---

## 10. Frontend Changes

### 10.1 New Component: ArchitectReviewPanel

Displayed on the `RequestDetail` page when a request has `status = ArchitectReview` or `status = Approved` and has architect reviews.

**Layout:**

```
┌──────────────────────────────────────────────────────────────────┐
│  Architect Agent Solution Proposal                               │
│  ┌────────────────────────────────────────────────────────────┐  │
│  │ Status: Pending Review    Complexity: Medium   Effort: 2-4h│  │
│  └────────────────────────────────────────────────────────────┘  │
│                                                                  │
│  Summary                                                         │
│  Add a new search endpoint with full-text filtering support...   │
│                                                                  │
│  Approach                                                        │
│  Follow existing controller + service pattern. Create a new...   │
│                                                                  │
│  ┌─ Impacted Files ─────────────────────────────────────────┐   │
│  │  ✏️ Controllers/RequestsController.cs  (+25 lines)       │   │
│  │  ✏️ Services/SearchService.cs          (new, ~80 lines)  │   │
│  │  ✏️ Models/DTOs/RequestDtos.cs         (+15 lines)       │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─ Data Migration ─────────────────────────────────────────┐   │
│  │  ✅ No migration required                                 │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  ┌─ Risks ──────────────────────────────────────────────────┐   │
│  │  ⚠️ Low: Existing search endpoint may need deprecation   │   │
│  │     Mitigation: Add backward-compatible alias             │   │
│  └──────────────────────────────────────────────────────────┘   │
│                                                                  │
│  Implementation Order                                            │
│  1. Add new model/DTO                                            │
│  2. Create service                                               │
│  3. Add controller endpoint                                      │
│  4. Update frontend API client                                   │
│  5. Add UI component                                             │
│                                                                  │
│  ┌──────────────┐  ┌──────────────┐  ┌─────────────────────┐   │
│  │ ✅ Approve    │  │ ❌ Reject    │  │ 💬 Request Changes  │   │
│  └──────────────┘  └──────────────┘  └─────────────────────┘   │
│                                                                  │
│  Files Analysed: 12  │  Tokens: 8,450  │  Model: gpt-4o        │
│  Duration: 12.3s     │  Review #1                                │
└──────────────────────────────────────────────────────────────────┘
```

### 10.2 Actions

| Button | Action |
|--------|--------|
| **Approve** | Calls `POST /api/architect/reviews/{id}/approve` with optional reason. Sets status → `Approved`. |
| **Reject** | Prompts for reason. Calls `POST /api/architect/reviews/{id}/reject`. Returns to `Triaged`. |
| **Request Changes** | Opens a comment input. Posts feedback via `POST /api/architect/reviews/{id}/feedback`. Agent revises on next cycle. |

### 10.3 New API Functions (api.ts)

```typescript
export interface ArchitectReview {
  id: number;
  devRequestId: number;
  requestTitle: string;
  solutionSummary: string;
  approach: string;
  impactedFiles: ImpactedFile[];
  newFiles: NewFile[];
  dataMigration: DataMigration;
  breakingChanges: string[];
  dependencyChanges: DependencyChange[];
  risks: Risk[];
  estimatedComplexity: string;
  estimatedEffort: string;
  implementationOrder: string[];
  testingNotes: string;
  architecturalNotes: string;
  decision: 'Pending' | 'Approved' | 'Rejected' | 'Revised';
  humanFeedback?: string;
  approvedBy?: string;
  approvedAt?: string;
  filesAnalysed: number;
  totalTokensUsed: number;
  modelUsed: string;
  totalDurationMs: number;
  createdAt: string;
}

export interface ImpactedFile { path: string; action: string; description: string; estimatedLinesChanged: number; }
export interface NewFile { path: string; description: string; estimatedLines: number; }
export interface DataMigration { required: boolean; description?: string; steps: string[]; }
export interface DependencyChange { package: string; action: string; version: string; reason: string; }
export interface Risk { description: string; severity: string; mitigation: string; }

export async function getArchitectReviews(params?: { requestId?: number; decision?: string }): Promise<ArchitectReview[]> { ... }
export async function getArchitectReview(id: number): Promise<ArchitectReview> { ... }
export async function approveArchitectReview(id: number, reason?: string): Promise<ArchitectReview> { ... }
export async function rejectArchitectReview(id: number, reason: string): Promise<ArchitectReview> { ... }
export async function postArchitectFeedback(id: number, feedback: string): Promise<void> { ... }
export async function getArchitectConfig(): Promise<ArchitectConfig> { ... }
export async function updateArchitectConfig(config: Partial<ArchitectConfig>): Promise<ArchitectConfig> { ... }
export async function getArchitectBudget(): Promise<TokenBudget> { ... }
export async function getArchitectStats(): Promise<ArchitectStats> { ... }
```

### 10.4 Dashboard Updates

Add an "Architect Agent" stats section alongside the existing "Product Owner Agent" section:

- Analyses completed today / this month
- Pending human review count
- Approval / rejection / revision rates
- Average files analysed per proposal
- Token usage + average response time

### 10.5 Admin Settings Updates

New "Architect Agent" configuration section (same editable form pattern as PO Agent):

- Enable/disable toggle
- Polling interval
- Max reviews per request
- Max files to read
- Temperature
- Daily/monthly token budgets
- Save button (runtime-only changes)

### 10.6 Request List Updates

- New `ArchitectReview` status badge (purple/blue)
- Filter by `ArchitectReview` status
- "Awaiting my review" filter for architects/leads

---

## 11. GitHub Integration

When the Architect Agent posts a solution:

| Event | GitHub Action |
|-------|---------------|
| Solution proposed | Add label `agent:architect-review`, post formatted solution as Issue comment |
| Human approves | Replace label with `agent:approved-solution`, post approval comment |
| Human rejects | Replace label with `agent:architect-rejected`, post rejection reason |
| Agent revises | Post updated solution as new Issue comment |

**Solution comment format on GitHub:**

```markdown
## 🏗️ Architect Agent — Solution Proposal

**Complexity:** Medium | **Effort:** 2-4 hours

### Summary
Add a new search endpoint with full-text filtering support...

### Impacted Files
| File | Action | Changes |
|------|--------|---------|
| `Controllers/RequestsController.cs` | Modify | +25 lines |
| `Services/SearchService.cs` | New | ~80 lines |

### Risks
- ⚠️ **Low:** Existing search endpoint may need deprecation
  *Mitigation:* Add backward-compatible alias

### Implementation Order
1. Add new model/DTO
2. Create service
3. Add controller endpoint

---
*Review #1 · 12 files analysed · 8,450 tokens · gpt-4o · 12.3s*
```

---

## 12. Configuration

### 12.1 New appsettings.json Section

```json
{
  "ArchitectAgent": {
    "Enabled": true,
    "PollingIntervalSeconds": 60,
    "MaxReviewsPerRequest": 3,
    "MaxFilesToRead": 20,
    "MaxFileContentChars": 50000,
    "Temperature": 0.2,
    "MaxTokens": 4000,
    "DailyTokenBudget": 0,
    "MonthlyTokenBudget": 0
  }
}
```

### 12.2 Configuration Notes

| Setting | Default | Notes |
|---------|---------|-------|
| `PollingIntervalSeconds` | 60 | Longer than PO (30s) because architecture analysis is heavier |
| `MaxFilesToRead` | 20 | Reduced to 8 on free tier to fit token budget |
| `MaxFileContentChars` | 50000 | Total chars across all files (~12,500 tokens) |
| `Temperature` | 0.2 | Lower than PO (0.3) for more deterministic technical output |
| `MaxTokens` | 4000 | Higher than PO (2000) because solution proposals are detailed |

---

## 13. Service Registration (Program.cs Changes)

```csharp
// Existing GitHub PAT check block — extend with:
if (!string.IsNullOrEmpty(githubPat))
{
    // Phase 2 (existing)
    builder.Services.AddSingleton<ILlmClientFactory, LlmClientFactory>();  // NEW — shared
    builder.Services.AddSingleton<ILlmService, LlmService>();
    builder.Services.AddHostedService<ProductOwnerAgentService>();

    // Phase 3 — Architect Agent
    builder.Services.AddSingleton<ICodebaseService, CodebaseService>();
    builder.Services.AddSingleton<IArchitectLlmService, ArchitectLlmService>();
    builder.Services.AddHostedService<ArchitectAgentService>();
}
```

---

## 14. Security & Guardrails

| Concern | Mitigation |
|---------|-----------|
| LLM hallucinating file paths | Solution references validated against actual repo map before posting |
| Excessive GitHub API calls | Caching (15 min tree, SHA-based file content), throttled parallel fetching |
| Token cost on large repos | Two-step approach limits files read; configurable max files and content budget |
| Prompt injection via codebase content | File contents delimited clearly; system prompt establishes agent role before any user content |
| Infinite revision loops | Max review count per request (default 3); escalates to manual review |
| Agent reads sensitive files (.env, secrets) | File filter excludes `.env`, `.env.*`, `*secret*`, `appsettings.*.json` (local dev only — production uses Key Vault) |
| Solution quality drift | All proposals require human approval; rejection sends back for re-analysis |
| Budget blow-outs | Per-agent daily/monthly token budgets (separate from PO Agent budgets) |

---

## 15. Sequence Diagrams

### 15.1 Happy Path — Propose and Approve

```
Human      API       DB        Architect Agent   CodebaseService     LLM
  │         │         │              │                  │              │
  │         │         │  Poll(Triaged)│                  │              │
  │         │         │◀─────────────│                  │              │
  │         │         │  [request]   │                  │              │
  │         │         │─────────────▶│                  │              │
  │         │         │              │  GetRepoMap()    │              │
  │         │         │              │────────────────▶│              │
  │         │         │              │  [file tree]    │              │
  │         │         │              │◀────────────────│              │
  │         │         │              │  Step 1: Select files          │
  │         │         │              │─────────────────────────────▶│
  │         │         │              │  ["file1.cs","file2.ts",...]  │
  │         │         │              │◀─────────────────────────────│
  │         │         │              │  GetFileContents()            │
  │         │         │              │────────────────▶│              │
  │         │         │              │  [file contents] │              │
  │         │         │              │◀────────────────│              │
  │         │         │              │  Step 2: Propose solution     │
  │         │         │              │─────────────────────────────▶│
  │         │         │              │  {solution JSON}              │
  │         │         │              │◀─────────────────────────────│
  │         │         │ Save(ArchRev)│                  │              │
  │         │         │◀─────────────│                  │              │
  │         │         │ Status=ArchR │                  │              │
  │         │         │◀─────────────│                  │              │
  │         │         │              │                  │              │
  │ [Sees solution on web]          │                  │              │
  │ POST approve     │              │                  │              │
  │────────▶│ Save   │              │                  │              │
  │         │───────▶│              │                  │              │
  │         │ Status=Approved       │                  │              │
  │◀────────│        │              │                  │              │
```

### 15.2 Feedback → Revision Flow

```
Human      API       DB        Architect Agent     LLM
  │         │         │              │                │
  │ [Reviews solution, wants change] │                │
  │ POST feedback     │              │                │
  │────────▶│ Save comment           │                │
  │         │───────▶│              │                │
  │◀────────│        │              │                │
  │         │        │              │                │
  │         │        │ Poll(ArchRev │                │
  │         │        │ + new comment)│                │
  │         │        │◀─────────────│                │
  │         │        │              │ Step1+Step2    │
  │         │        │              │ (with feedback │
  │         │        │              │  in context)   │
  │         │        │              │───────────────▶│
  │         │        │              │ {revised soln} │
  │         │        │              │◀───────────────│
  │         │        │ New ArchRev  │                │
  │         │        │◀─────────────│                │
  │         │        │              │                │
  │ [Reviews revised solution]      │                │
  │ POST approve     │              │                │
  │────────▶│ Status=Approved       │                │
```

---

## 16. Migration Plan

| Step | Change | Risk | Depends On |
|------|--------|------|------------|
| 1 | Add `ArchitectReview` to status enum | Low — additive | — |
| 2 | Create `ArchitectReview` entity + migration | Low — new table | Step 1 |
| 3 | Add `LastArchitectReviewAt`, `ArchitectReviewCount` to `DevRequest` | Low — nullable defaults | Step 2 |
| 4 | Add `ArchitectReviewId` FK to `RequestComment` | Low — nullable column | Step 2 |
| 5 | Extract `ILlmClientFactory` from `LlmService` | Low — refactor, no behaviour change | — |
| 6 | Extend `IGitHubService` with tree/content methods | Low — additive | — |
| 7 | Implement `CodebaseService` | None — new code | Step 6 |
| 8 | Implement `ArchitectLlmService` | None — new code | Step 5 |
| 9 | Implement `ArchitectAgentService` (BackgroundService) | Low — controlled by config flag | Steps 7, 8 |
| 10 | Add `ArchitectController` endpoints | Low — new controller | Step 9 |
| 11 | Add ArchitectReviewPanel component (frontend) | None — UI addition | Step 10 |
| 12 | Update Dashboard + Admin Settings (frontend) | None — UI additions | Step 10 |
| 13 | Add `ArchitectAgent` section to appsettings.json | Low — config only | — |
| 14 | Update CI/CD if needed (no changes expected) | None | — |

**No breaking changes** to Phase 1 or Phase 2 functionality. The Architect Agent is disabled by default and opt-in.

---

## 17. Testing Strategy

| Test Type | Scope |
|-----------|-------|
| **Unit** | `ArchitectLlmService` — mock `ILlmClientFactory`, verify prompt construction and response parsing |
| **Unit** | `CodebaseService` — mock Octokit, verify filtering, caching, line counting |
| **Unit** | `ArchitectAgentService` — mock all dependencies, verify polling logic and status transitions |
| **Integration** | Post a `Triaged` request → verify agent picks it up → verify `ArchitectReview` created |
| **Integration** | Approve/reject flow — verify status transitions |
| **Integration** | Feedback → revision flow — verify conversation context included |
| **Manual/UAT** | End-to-end: submit request → PO approves → Architect proposes → human approves |
| **Manual/UAT** | Verify GitHub Issue labels/comments update correctly |
| **Manual/UAT** | Verify token budget enforcement stops the agent when exceeded |

---

## 18. Estimated Scope

| Component | Estimated Files | Estimated Lines |
|-----------|----------------|-----------------|
| Models (ArchitectReview, enums, DTOs) | 3 | ~200 |
| Services (CodebaseService, ArchitectLlmService, ArchitectAgentService) | 3 | ~600 |
| Controller (ArchitectController) | 1 | ~300 |
| LlmClientFactory refactor | 2 (modify) | ~50 |
| GitHubService extensions | 1 (modify) | ~80 |
| EF Migration | 1 | ~100 |
| Program.cs registration | 1 (modify) | ~10 |
| Frontend (api.ts, ArchitectReviewPanel, Dashboard, Admin) | 4 | ~500 |
| Configuration (appsettings.json) | 1 (modify) | ~10 |
| **Total** | **~17** | **~1,850** |
