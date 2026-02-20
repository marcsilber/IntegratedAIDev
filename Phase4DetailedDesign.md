# Phase 4 — Implementation via GitHub Copilot Coding Agent: Detailed Design

## 1. Overview

Phase 4 leverages GitHub's built-in **Copilot Coding Agent** (`copilot-swe-agent[bot]`) to implement approved architectural solutions. Rather than building a custom code-generation agent, the AIDev platform triggers GitHub's existing infrastructure — which has full codebase access, a secure cloud execution environment, built-in test/linter validation, and automatic PR creation.

This is a **hybrid approach**: the AIDev pipeline retains full control over triage (Phase 2) and solution design (Phase 3), then delegates the implementation to GitHub's purpose-built coding agent for Phase 4.

### 1.1 Goals

| Goal | Description |
|------|-------------|
| Automated implementation | Approved solutions are implemented without manual coding effort |
| Full codebase access | GitHub's agent clones the entire repo — no file-selection heuristics needed |
| Built-in validation | Agent runs tests, linters, and builds before opening a PR |
| Human review gate | PR review is the final quality checkpoint before merge |
| Pipeline integration | AIDev tracks the full lifecycle: Issue → Triage → Architecture → Implementation → PR → Done |
| Cost efficiency | No custom code-generation infrastructure to build or maintain |

### 1.2 Non-Goals (Phase 4)

- AIDev does **not** auto-merge PRs — human review is always required
- AIDev does **not** manage the agent's internal execution (that's GitHub's responsibility)
- AIDev does **not** replace the Architect Agent's design step — the Coding Agent receives the approved solution as instructions, not a blank issue

---

## 2. Architecture

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                              AIDev Pipeline                                  │
│                                                                              │
│  ┌────────────┐    ┌─────────────┐    ┌────────────────┐    ┌─────────────┐ │
│  │  Phase 1   │    │  Phase 2    │    │  Phase 3       │    │  Phase 4    │ │
│  │  Request   │───▶│  PO Agent   │───▶│  Architect     │───▶│  Trigger    │ │
│  │  Intake    │    │  Triage     │    │  Agent Design  │    │  Copilot    │ │
│  └────────────┘    └─────────────┘    └────────────────┘    └──────┬──────┘ │
│                                                                     │        │
└─────────────────────────────────────────────────────────────────────┼────────┘
                                                                      │
                                            ┌─────────────────────────▼────────┐
                                            │  GitHub Copilot Coding Agent     │
                                            │                                  │
                                            │  1. Clones repo                  │
                                            │  2. Reads approved solution      │
                                            │  3. Implements changes           │
                                            │  4. Runs tests & linters         │
                                            │  5. Creates PR                   │
                                            │  6. Requests review              │
                                            └──────────────────────────────────┘
```

### 2.1 End-to-End Flow

```
DevRequest (Approved)
  │
  ▼
┌─────────────────────────────────┐
│  ImplementationTriggerService   │
│  (BackgroundService)            │
│                                 │
│  1. Polls for Approved requests │
│  2. Formats custom_instructions │
│     from ArchitectReview JSON   │
│  3. Calls GitHub REST API:      │
│     POST /issues/{n}/assignees  │
│     assignee: copilot-swe-agent │
│  4. Status → InProgress         │
│  5. Stores PR tracking info     │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│  GitHub Copilot Coding Agent    │
│                                 │
│  • Clones repo in cloud env    │
│  • Reads issue + instructions   │
│  • Implements solution          │
│  • Runs tests, linters, build   │
│  • Creates branch + PR          │
│  • Requests review              │
└───────────────┬─────────────────┘
                │
                ▼
┌─────────────────────────────────┐
│  PrMonitorService               │
│  (BackgroundService)            │
│                                 │
│  1. Polls GitHub PR status      │
│  2. Tracks agent session state  │
│  3. Updates DevRequest status   │
│  4. Notifies via dashboard      │
│  5. On merge → Status = Done    │
└─────────────────────────────────┘
```

---

## 3. Prerequisites

### 3.1 GitHub Copilot Plan

The Copilot Coding Agent requires one of:
- **Copilot Pro** (available for personal accounts)
- **Copilot Pro+** (premium features + model selection)
- **Copilot Business** (organisation accounts)
- **Copilot Enterprise** (enterprise accounts)

### 3.2 Repository Configuration

The coding agent must be **enabled** for the target repository:
- Repository Settings → Copilot → Enable "Copilot coding agent"
- The bot `copilot-swe-agent` must appear in the repository's suggested actors

### 3.3 GitHub Actions

The coding agent runs in a GitHub Actions-powered cloud environment. Ensure:
- GitHub Actions is enabled for the repository
- Sufficient Actions minutes are available (the agent consumes minutes during execution)
- Repository has a working CI pipeline (tests, linters) for the agent to validate against

### 3.4 Authentication

AIDev needs a GitHub token (PAT or GitHub App) with permissions:
- **Read access:** metadata
- **Read and write access:** actions, contents, issues, pull requests

The existing `GitHub:PersonalAccessToken` from Phase 1 may need upgraded scopes.

---

## 4. Trigger Mechanism

### 4.1 ImplementationTriggerService (BackgroundService)

Follows the same polling pattern as the PO Agent and Architect Agent.

| Trigger | Condition |
|---------|-----------|
| Architecture approved | `Status = Approved`, `CopilotSessionId == null` |
| Manual re-trigger | API endpoint re-assigns the Issue to Copilot |

**Polling interval:** Configurable (default 60 seconds).

**Batch size:** 1 per cycle (each Copilot session consumes significant Actions minutes — conservative batching).

### 4.2 Trigger Logic

```csharp
public class ImplementationTriggerService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var approvedRequests = await _db.DevRequests
                .Include(r => r.ArchitectReviews)
                .Where(r => r.Status == RequestStatus.Approved
                         && r.CopilotSessionId == null
                         && r.GitHubIssueNumber != null)
                .OrderBy(r => r.UpdatedAt)
                .Take(_config.BatchSize)
                .ToListAsync(stoppingToken);

            foreach (var request in approvedRequests)
            {
                await TriggerCopilotCodingAgent(request, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.PollingIntervalSeconds), stoppingToken);
        }
    }
}
```

---

## 5. Copilot Assignment via GitHub REST API

### 5.1 Assigning the Issue

When a request is approved, the service assigns the corresponding GitHub Issue to the Copilot Coding Agent:

```http
POST /repos/{owner}/{repo}/issues/{issue_number}/assignees
Content-Type: application/json
Authorization: Bearer {github_pat}
Accept: application/vnd.github+json
X-GitHub-Api-Version: 2022-11-28

{
  "assignees": ["copilot-swe-agent[bot]"],
  "agent_assignment": {
    "target_repo": "{owner}/{repo}",
    "base_branch": "main",
    "custom_instructions": "{formatted_solution}",
    "custom_agent": "",
    "model": ""
  }
}
```

### 5.2 Custom Instructions Construction

The `custom_instructions` field is built from the approved `ArchitectReview` solution JSON. This is the critical bridge between Phase 3 (design) and Phase 4 (implementation):

```csharp
private string BuildCustomInstructions(ArchitectReview review)
{
    var sb = new StringBuilder();
    
    sb.AppendLine("## Approved Solution");
    sb.AppendLine();
    sb.AppendLine($"**Approach:** {review.SolutionSummary}");
    sb.AppendLine();
    
    // Impacted files
    sb.AppendLine("## Files to Modify");
    foreach (var file in review.ImpactedFiles)
    {
        sb.AppendLine($"- `{file.Path}` — {file.ChangeDescription}");
    }
    sb.AppendLine();
    
    // New files
    if (review.NewFiles?.Any() == true)
    {
        sb.AppendLine("## New Files to Create");
        foreach (var file in review.NewFiles)
        {
            sb.AppendLine($"- `{file.Path}` — {file.Purpose}");
        }
        sb.AppendLine();
    }
    
    // Data migration
    if (!string.IsNullOrEmpty(review.DataMigration))
    {
        sb.AppendLine("## Data Migration");
        sb.AppendLine(review.DataMigration);
        sb.AppendLine();
    }
    
    // Implementation order
    sb.AppendLine("## Implementation Order");
    sb.AppendLine(review.ImplementationGuidance);
    sb.AppendLine();
    
    // Risks and considerations
    if (review.Risks?.Any() == true)
    {
        sb.AppendLine("## Risks & Considerations");
        foreach (var risk in review.Risks)
        {
            sb.AppendLine($"- ⚠️ {risk}");
        }
        sb.AppendLine();
    }
    
    // Dependencies
    if (review.DependencyChanges?.Any() == true)
    {
        sb.AppendLine("## Dependency Changes");
        foreach (var dep in review.DependencyChanges)
        {
            sb.AppendLine($"- {dep}");
        }
    }
    
    sb.AppendLine();
    sb.AppendLine("## Important");
    sb.AppendLine("- Follow existing code patterns and conventions in the repository");
    sb.AppendLine("- Run all existing tests and ensure they pass");
    sb.AppendLine("- Add tests for new functionality");
    sb.AppendLine("- Do not modify files outside the scope listed above unless absolutely necessary");
    
    return sb.ToString();
}
```

### 5.3 Custom Agent Profile (Optional)

For additional control, a custom agent profile can be placed in the repository at `.github/agents/aidev-implementer.agent.md`:

```markdown
---
name: aidev-implementer
description: Implements approved architectural solutions from the AIDev pipeline. Follows the solution specification precisely, respects existing patterns, and ensures test coverage.
tools: ["read", "edit", "search", "terminal", "test"]
---

You are an implementation agent for the AIDev development pipeline. You receive approved solution specifications from the Architect Agent and implement them precisely.

Your responsibilities:
- Implement the solution exactly as specified in the custom instructions
- Follow existing code patterns, naming conventions, and architecture in the repository
- Create or modify only the files specified in the solution
- Add appropriate unit tests for all new functionality
- Run the full test suite and fix any regressions
- Add EF Core migrations if data model changes are specified
- Update configuration files as needed

Rules:
- Do NOT deviate from the approved solution without documenting why
- Do NOT introduce new dependencies unless specified in the solution
- Do NOT modify files outside the solution scope unless fixing a direct dependency
- Ensure all existing tests continue to pass
- Write clear, concise commit messages referencing the issue number
```

### 5.4 Model Selection

If using Copilot Pro+, you can specify the model in the `agent_assignment`:

```json
{
  "model": "claude-sonnet-4-20250514"
}
```

Leave empty (`""`) to use the default model.

---

## 6. Status Model Changes

### 6.1 New DevRequest Fields

| Field | Type | Description |
|-------|------|-------------|
| CopilotSessionId | string? | GitHub Copilot agent session ID (returned after assignment) |
| CopilotPrNumber | int? | PR number created by Copilot |
| CopilotPrUrl | string? | URL to the PR |
| CopilotTriggeredAt | DateTime? | When the Issue was assigned to Copilot |
| CopilotCompletedAt | DateTime? | When Copilot finished (PR opened or failed) |
| CopilotStatus | enum? | Pending, Working, PrOpened, PrMerged, Failed |

### 6.2 CopilotImplementationStatus Enum

```csharp
public enum CopilotImplementationStatus
{
    Pending,    // Issue assigned to Copilot, waiting for session to start
    Working,    // Copilot is actively implementing
    PrOpened,   // Copilot opened a PR, awaiting human review
    PrMerged,   // PR merged — implementation complete
    Failed      // Copilot couldn't complete the task
}
```

### 6.3 Status Transitions

```
Approved → InProgress (Copilot assigned)
  │
  ├─→ PrOpened (Copilot created PR) → PrMerged → Done
  │
  └─→ Failed → (human intervenes or re-triggers)
```

| Transition | Triggered By |
|-----------|--------------|
| `Approved` → `InProgress` | ImplementationTriggerService assigns Issue to Copilot |
| `InProgress` → `InProgress` (PrOpened) | PrMonitorService detects new PR |
| `InProgress` → `Done` | PrMonitorService detects PR merged |
| `InProgress` → `InProgress` (Failed) | PrMonitorService detects session failure |

---

## 7. PR Monitoring

### 7.1 PrMonitorService (BackgroundService)

A separate background service monitors the status of Copilot-initiated PRs:

```csharp
public class PrMonitorService : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var inProgressRequests = await _db.DevRequests
                .Where(r => r.Status == RequestStatus.InProgress
                         && r.CopilotSessionId != null
                         && r.CopilotStatus != CopilotImplementationStatus.PrMerged
                         && r.CopilotStatus != CopilotImplementationStatus.Failed)
                .ToListAsync(stoppingToken);

            foreach (var request in inProgressRequests)
            {
                await CheckCopilotProgress(request, stoppingToken);
            }

            await Task.Delay(TimeSpan.FromSeconds(_config.PrPollIntervalSeconds), stoppingToken);
        }
    }

    private async Task CheckCopilotProgress(DevRequest request, CancellationToken ct)
    {
        // Check if a PR was created by copilot-swe-agent for this issue
        if (request.CopilotPrNumber == null)
        {
            var pr = await _githubService.FindPrByIssueAndAuthor(
                request.GitHubIssueNumber!.Value, "copilot-swe-agent[bot]");
            
            if (pr != null)
            {
                request.CopilotPrNumber = pr.Number;
                request.CopilotPrUrl = pr.HtmlUrl;
                request.CopilotStatus = CopilotImplementationStatus.PrOpened;
                await _db.SaveChangesAsync(ct);
            }
        }
        else
        {
            // Check PR status
            var pr = await _githubService.GetPullRequest(request.CopilotPrNumber.Value);
            
            if (pr.Merged)
            {
                request.CopilotStatus = CopilotImplementationStatus.PrMerged;
                request.CopilotCompletedAt = DateTime.UtcNow;
                request.Status = RequestStatus.Done;
                await _db.SaveChangesAsync(ct);
            }
            else if (pr.State == "closed" && !pr.Merged)
            {
                request.CopilotStatus = CopilotImplementationStatus.Failed;
                request.CopilotCompletedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }
        }
    }
}
```

### 7.2 Polling Interval

**Default:** 120 seconds (PRs don't change as frequently — less aggressive polling)

### 7.3 GitHub Issue Labels

| Event | Label |
|-------|-------|
| Issue assigned to Copilot | `copilot:implementing` |
| PR opened | `copilot:pr-ready` |
| PR merged | `copilot:complete` |
| Session failed | `copilot:failed` |

---

## 8. API Endpoints

### 8.1 ImplementationController

| Method | Endpoint | Description |
|--------|----------|-------------|
| `POST` | `/api/implementation/trigger/{requestId}` | Manually trigger Copilot for an approved request |
| `POST` | `/api/implementation/re-trigger/{requestId}` | Re-assign to Copilot after a failure |
| `GET` | `/api/implementation/status/{requestId}` | Get Copilot implementation status |
| `GET` | `/api/implementation/sessions` | List all active/recent Copilot sessions |
| `GET` | `/api/implementation/config` | Current implementation config |
| `PUT` | `/api/implementation/config` | Update config (polling interval, model, auto-trigger) |
| `GET` | `/api/implementation/stats` | Aggregate stats (sessions, success rate, avg time) |

### 8.2 Endpoint Details

#### POST `/api/implementation/trigger/{requestId}`

Manually triggers Copilot for a specific approved request:

```json
// Request body (optional)
{
  "additionalInstructions": "Priority: focus on backward compatibility",
  "model": "claude-sonnet-4-20250514",
  "baseBranch": "main"
}

// Response
{
  "requestId": 42,
  "issueNumber": 15,
  "copilotStatus": "Pending",
  "triggeredAt": "2026-02-20T10:30:00Z"
}
```

- Validates request is `Approved` and has a GitHub Issue
- Calls GitHub REST API to assign Issue to `copilot-swe-agent[bot]`
- Sets `Status = InProgress`, `CopilotStatus = Pending`

#### GET `/api/implementation/status/{requestId}`

```json
{
  "requestId": 42,
  "copilotStatus": "PrOpened",
  "copilotSessionId": "cs_abc123",
  "prNumber": 27,
  "prUrl": "https://github.com/marcsilber/IntegratedAIDev/pull/27",
  "triggeredAt": "2026-02-20T10:30:00Z",
  "completedAt": null,
  "elapsedMinutes": 12
}
```

---

## 9. Frontend Changes

### 9.1 Request Detail — Implementation Panel

When a request reaches `Approved` or `InProgress` status, show an **Implementation Panel**:

| State | Display |
|-------|---------|
| `Approved` (not triggered) | "Ready for implementation" + **Trigger Copilot** button |
| `Pending` | Spinner + "Copilot is starting..." |
| `Working` | Progress indicator + "Copilot is implementing the approved solution..." |
| `PrOpened` | Link to PR + "Pull request ready for review" badge |
| `PrMerged` | Green checkmark + "Implementation complete" |
| `Failed` | Red alert + "Copilot could not complete the task" + **Re-trigger** button |

### 9.2 PR Review Link

When `CopilotPrUrl` is set, display a prominent link:

```
🔗 Pull Request #27: [Implement user notification system](https://github.com/...)
Status: Open | Checks: ✅ Passing | Review: Pending
```

### 9.3 Dashboard Updates

New dashboard metrics:
- Requests awaiting implementation
- Active Copilot sessions
- PRs awaiting review
- Average implementation time (trigger → PR opened)
- Copilot success rate (PrMerged / total triggered)

### 9.4 Admin Settings — Implementation Config

New admin section:
- Enable/disable auto-trigger on approval
- Copilot model preference
- Base branch override
- Maximum concurrent Copilot sessions
- Custom agent profile selection

---

## 10. Configuration

New section in `appsettings.json`:

```json
{
  "CopilotImplementation": {
    "Enabled": true,
    "AutoTriggerOnApproval": true,
    "PollingIntervalSeconds": 60,
    "PrPollIntervalSeconds": 120,
    "MaxConcurrentSessions": 3,
    "BaseBranch": "main",
    "Model": "",
    "CustomAgent": "",
    "MaxRetries": 2
  }
}
```

| Setting | Description | Default |
|---------|-------------|---------|
| `Enabled` | Master switch for Phase 4 | `true` |
| `AutoTriggerOnApproval` | Automatically assign to Copilot when Architect solution is approved | `true` |
| `PollingIntervalSeconds` | How often to check for approved requests | 60 |
| `PrPollIntervalSeconds` | How often to check PR status | 120 |
| `MaxConcurrentSessions` | Limit simultaneous Copilot sessions (controls Actions cost) | 3 |
| `BaseBranch` | Default branch for Copilot to branch from | `main` |
| `Model` | LLM model for Copilot (empty = default) | `""` |
| `CustomAgent` | Custom agent profile name (empty = default coding agent) | `""` |
| `MaxRetries` | Auto-retry count on failure before requiring manual intervention | 2 |

---

## 11. Security & Cost Controls

### 11.1 Cost Model

GitHub Copilot Coding Agent consumes:
- **GitHub Actions minutes** — the agent runs in a cloud environment powered by Actions
- **Copilot premium requests** — each session uses premium request quota

### 11.2 Cost Controls

| Control | Mechanism |
|---------|-----------|
| Concurrent session limit | `MaxConcurrentSessions` prevents runaway Actions usage |
| Human approval gate | Copilot only triggers after explicit human approval of the architecture (no auto-implementation of raw issues) |
| Auto-trigger toggle | Can disable auto-triggering and require manual button click per request |
| Model selection | Choose cheaper models when appropriate |
| Retry limit | `MaxRetries` prevents infinite re-triggering on persistent failures |

### 11.3 Security

| Concern | Mitigation |
|---------|-----------|
| Code quality | Copilot runs tests and linters before opening PR; human reviews PR |
| Unintended changes | Custom instructions scope the work to specific files from the Architect solution |
| Codebase access | Copilot runs in GitHub's secure cloud environment — no external egress by default |
| Token/secret exposure | Agent respects `.env` / secret patterns; hooks can add additional safeguards |
| Supply chain | New dependency additions are visible in the PR diff for human review |

---

## 12. GitHub Integration

### 12.1 Issue Updates

When implementation is triggered:
1. Assign `copilot-swe-agent[bot]` to the Issue
2. Add label `copilot:implementing`
3. Post comment: "🤖 Implementation triggered. Copilot is working on the approved solution."

When PR is opened:
1. Update label to `copilot:pr-ready`
2. Post comment: "✅ Copilot has opened PR #{n}. Ready for human review."

When PR is merged:
1. Update label to `copilot:complete`
2. Close the Issue (if not auto-closed by PR)

### 12.2 Custom Repository Instructions

Place a `.github/copilot-instructions.md` in the repo for global guidance:

```markdown
## Project Structure
- Backend: `src/AIDev.Api/` — .NET 10 Web API
- Frontend: `src/AIDev.Web/` — React 19 + TypeScript + Vite
- Database: SQLite via EF Core

## Conventions
- Use nullable reference types
- Follow existing controller/service patterns
- Add XML doc comments on public members
- Use record types for DTOs
- Frontend: functional components with TypeScript interfaces

## Testing
- Backend: xUnit tests in the test project
- Frontend: run `npx tsc --noEmit` to verify TypeScript compilation
- Run `dotnet test` before marking as complete
```

---

## 13. Sequence Diagram — Happy Path

```
Human           AIDev API        GitHub API       Copilot Agent    Human Reviewer
  │                │                │                  │                │
  │ POST approve   │                │                  │                │
  │ (Phase 3)      │                │                  │                │
  │───────────────▶│                │                  │                │
  │                │ Status=Approved│                  │                │
  │                │                │                  │                │
  │                │  [Trigger polls for Approved]     │                │
  │                │                │                  │                │
  │                │ POST /issues/  │                  │                │
  │                │ {n}/assignees  │                  │                │
  │                │───────────────▶│                  │                │
  │                │                │  Assign to       │                │
  │                │                │  copilot-swe-    │                │
  │                │                │  agent[bot]      │                │
  │                │                │─────────────────▶│                │
  │                │ Status=InProg  │                  │                │
  │                │                │                  │                │
  │                │                │                  │ Clone repo     │
  │                │                │                  │ Read solution  │
  │                │                │                  │ Implement      │
  │                │                │                  │ Run tests      │
  │                │                │                  │ Create PR      │
  │                │                │                  │───────────────▶│
  │                │  [PR Monitor detects PR]          │  Review req    │
  │                │                │                  │                │
  │                │ CopilotStatus  │                  │                │
  │                │ = PrOpened     │                  │                │
  │                │                │                  │                │
  │                │                │                  │                │ Reviews PR
  │                │                │                  │                │ Merges
  │                │  [PR Monitor detects merge]       │                │
  │                │ Status=Done    │                  │                │
```

---

## 14. Comparison: Custom Agent vs GitHub Copilot Coding Agent

This section documents the rationale for choosing the hybrid approach.

| Dimension | Custom Code Agent (original Phase 4) | GitHub Copilot Coding Agent (chosen) |
|-----------|--------------------------------------|--------------------------------------|
| **Build effort** | ~2,000+ lines, custom code generation, branch management, PR creation | ~500 lines (trigger + monitor services) |
| **Codebase access** | GitHub API (tree + targeted files — limited by token budget) | Full repo clone (complete access) |
| **Test execution** | Would need custom CI integration | Built-in — runs tests and linters automatically |
| **PR creation** | Would need to build branch + commit + PR logic via Octokit | Automatic — agent creates branch and PR |
| **Execution environment** | AIDev's App Service (limited to API calls) | Dedicated cloud VM via GitHub Actions |
| **Model quality** | Limited to GitHub Models free tier (GPT-4o) | Access to latest Copilot models (Claude, GPT, etc.) |
| **Error recovery** | Would need custom retry/rollback logic | Agent self-corrects; can re-trigger on failure |
| **Maintenance** | Ongoing maintenance of code-gen prompts, branch logic | Maintained by GitHub — updates automatically |
| **Cost** | GitHub Models API calls only | GitHub Actions minutes + Copilot premium requests |
| **Control** | Full control over every step | Less control over internal agent behaviour |

**Decision:** The significant reduction in build effort (~75%), superior codebase access, built-in test execution, and GitHub-maintained infrastructure outweigh the moderate cost increase and reduced internal control.

---

## 15. Migration Plan

| Step | Change | Risk | Depends On |
|------|--------|------|------------|
| 1 | Add `CopilotImplementationStatus` enum | Low — new enum | — |
| 2 | Add Copilot fields to `DevRequest` entity + migration | Low — nullable columns | Step 1 |
| 3 | Upgrade GitHub PAT scopes (actions, pull requests write) | Low — credential update | — |
| 4 | Enable Copilot Coding Agent on target repository | Low — setting toggle | Copilot subscription |
| 5 | Create `.github/copilot-instructions.md` in target repo | None — new file | — |
| 6 | Optionally create `.github/agents/aidev-implementer.agent.md` | None — new file | — |
| 7 | Implement `ImplementationTriggerService` | Low — new BackgroundService | Steps 1–4 |
| 8 | Implement `PrMonitorService` | Low — new BackgroundService | Step 7 |
| 9 | Add `ImplementationController` endpoints | Low — new controller | Steps 7, 8 |
| 10 | Add Implementation Panel to frontend | None — UI addition | Step 9 |
| 11 | Update Dashboard + Admin Settings | None — UI additions | Step 9 |
| 12 | Add `CopilotImplementation` section to appsettings.json | Low — config only | — |
| 13 | Update CI/CD pipeline if needed | Low — may need workflow permissions | — |

**No breaking changes** to Phase 1, 2, or 3 functionality. The implementation trigger is disabled by default and opt-in.

---

## 16. Testing Strategy

| Test Type | Scope |
|-----------|-------|
| **Unit** | `ImplementationTriggerService` — mock GitHub API, verify assignment payload construction |
| **Unit** | `PrMonitorService` — mock GitHub API, verify status transitions |
| **Unit** | `BuildCustomInstructions` — verify solution JSON is correctly formatted as markdown |
| **Integration** | Approve a request → verify Copilot is assigned via GitHub API |
| **Integration** | Mock PR creation → verify status transitions to PrOpened, then Done on merge |
| **Manual/UAT** | End-to-end: approved request → Copilot implements → PR opened → human reviews → merge → Done |
| **Manual/UAT** | Failure scenario: Copilot fails → status shows Failed → re-trigger works |
| **Manual/UAT** | Verify GitHub Issue labels update correctly through the lifecycle |
| **Manual/UAT** | Verify dashboard shows implementation metrics |

---

## 17. Estimated Scope

| Component | Estimated Files | Estimated Lines |
|-----------|----------------|-----------------|
| Models (CopilotImplementationStatus, DevRequest extensions, DTOs) | 3 | ~120 |
| Services (ImplementationTriggerService, PrMonitorService) | 2 | ~350 |
| Controller (ImplementationController) | 1 | ~200 |
| GitHubService extensions (assign Copilot, find PR, get PR status) | 1 (modify) | ~80 |
| EF Migration | 1 | ~60 |
| Program.cs registration | 1 (modify) | ~10 |
| Frontend (api.ts, ImplementationPanel, Dashboard, Admin updates) | 4 | ~400 |
| Configuration (appsettings.json) | 1 (modify) | ~15 |
| Repo config (.github/copilot-instructions.md, agent profile) | 2 | ~60 |
| **Total** | **~16** | **~1,295** |

Compared to building a custom code-generation agent (~2,000+ lines with branch management, commit logic, PR creation, and code-gen prompts), the hybrid approach saves approximately **35–40%** in implementation effort while gaining superior capabilities.

---

## 18. Future Enhancements

| Enhancement | Description |
|-------------|-------------|
| Webhook-based monitoring | Replace PR polling with GitHub webhooks for real-time status updates |
| Batch implementation | Group related approved requests into a single Copilot session |
| Auto-merge with conditions | Auto-merge PRs that have all checks passing and approvals |
| Cost tracking | Track Actions minutes and premium request consumption per request |
| MCP integration | Use GitHub MCP server to trigger Copilot from within AIDev's own agent context |
| Feedback loop | Feed PR review comments back to the Architect Agent to improve future solutions |
