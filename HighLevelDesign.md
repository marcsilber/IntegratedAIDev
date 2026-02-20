# AI Dev Pipeline — High Level Design

## 1. Vision

An AI-assisted iterative development platform that enables multiple testers to submit structured requests (bugs, features, enhancements) which flow through an AI-agent pipeline for triage, architecture, planning, and implementation — with human checkpoints at key decision points.

## 2. Problem Statement

In the current workflow, a single developer using an AI coding agent (GitHub Copilot) in VS Code operates serially: test → report → fix → deploy → retest. Testers find issues faster than they can be resolved because:

- Only one agent works at a time in VS Code
- Fixes are deployed one at a time (too many deploys)
- No structured intake for tester requests
- No parallel work streams (branches)

## 3. Solution Overview

A web application backed by an AI-agent pipeline that:

1. **Captures** structured requests from testers via a web UI
2. **Triages** requests through AI agents (Product Owner → Architect → Planner)
3. **Implements** fixes in parallel branches via coding agents
4. **Deploys** in batches to UAT after merge

## 4. Phased Delivery

| Phase | Scope | Status |
|-------|-------|--------|
| **Phase 1** | Structured request intake, tester web app, GitHub Issues sync | 🔨 In Progress |
| **Phase 2** | Product Owner Agent (triage, clarification, approval) | ⏳ Planned |
| **Phase 3** | Architect Agent (solution design, impact analysis) | ⏳ Planned |
| **Phase 4** | Planning Agent (branch management, agent assignment, deploy orchestration) | ⏳ Planned |

## 5. Phase 1 Architecture

```
┌──────────────────────┐     ┌─────────────────────┐     ┌──────────────────┐
│   React Frontend     │────▶│   .NET 8 Web API    │────▶│   GitHub API     │
│   (Vite + TypeScript)│     │   (EF Core + SQLite) │     │   (Issues on     │
│   MSAL.js (Entra ID) │     │   JWT Bearer Auth    │     │   IntegratedAIDev)│
└──────────────────────┘     └─────────────────────┘     └──────────────────┘
                                       │
                                ┌──────┴──────┐
                                │   SQLite    │
                                │  (Local DB) │
                                └─────────────┘
```

### 5.1 Frontend — AIDev.Web

- **Framework:** React 18 + TypeScript + Vite
- **Auth:** MSAL.js 2.x with Microsoft Entra ID
- **UI:** Component library (to be decided — starting with clean CSS)
- **Key Pages:**
  - **Submit Request** — structured form for bugs/features
  - **Request List** — filterable/searchable list of all requests
  - **Request Detail** — full view with status history and comments
  - **Dashboard** — summary stats, status breakdown, recent activity

### 5.2 Backend — AIDev.Api

- **Framework:** .NET 8 Minimal APIs / Controllers
- **Auth:** Microsoft.Identity.Web (JWT Bearer from Entra ID)
- **Database:** SQLite via EF Core (upgradeable to SQL Server / Azure SQL)
- **External Integration:** Octokit (GitHub API client) for Issue sync
- **Key Endpoints:**
  - `POST /api/requests` — create a new request
  - `GET /api/requests` — list/filter requests
  - `GET /api/requests/{id}` — get request detail
  - `PUT /api/requests/{id}` — update request
  - `GET /api/dashboard` — dashboard stats

### 5.3 Data Model

#### DevRequest

| Field | Type | Description |
|-------|------|-------------|
| Id | int (PK) | Auto-increment |
| Title | string | Short summary (required) |
| Description | string | Detailed explanation (required) |
| RequestType | enum | Bug, Feature, Enhancement, Question |
| Priority | enum | Critical, High, Medium, Low |
| StepsToReproduce | string? | For bugs — how to reproduce |
| ExpectedBehavior | string? | What should happen |
| ActualBehavior | string? | What actually happens |
| Status | enum | New, Triaged, Approved, InProgress, Done, Rejected |
| SubmittedBy | string | User display name (from Entra ID) |
| SubmittedByEmail | string | User email (from Entra ID) |
| GitHubIssueNumber | int? | Linked GitHub Issue number |
| GitHubIssueUrl | string? | URL to the GitHub Issue |
| CreatedAt | DateTime | UTC timestamp |
| UpdatedAt | DateTime | UTC timestamp |

#### RequestComment

| Field | Type | Description |
|-------|------|-------------|
| Id | int (PK) | Auto-increment |
| DevRequestId | int (FK) | Parent request |
| Author | string | Comment author |
| Content | string | Comment text |
| CreatedAt | DateTime | UTC timestamp |

### 5.4 Authentication Flow

1. User opens React app → MSAL.js redirects to Microsoft login
2. User authenticates → receives an ID token + access token
3. React app sends access token as `Authorization: Bearer <token>` to API
4. .NET API validates JWT against Entra ID tenant
5. API extracts user claims (name, email) for request attribution

### 5.5 GitHub Issues Sync

When a request is created or updated:
1. API calls GitHub REST API via Octokit
2. Creates an Issue on `IntegratedAIDev` repo with:
   - Title from request
   - Body formatted as markdown (description, steps, priority, etc.)
   - Labels: `bug`/`feature`/`enhancement` + priority label
3. Stores the Issue number and URL back on the DevRequest record

## 6. Phase 2 Architecture — Product Owner Agent

### 6.1 Overview

The Product Owner Agent is a background service that automatically triages new requests by reviewing them against the product's objectives (`ApplicationObjectives.md`) and sales positioning (`ApplicationSalesPack.md`). It checks for completeness, alignment, and priority — then either approves the request, rejects it with reasoning, or posts clarification questions back to the submitter. Human override is always available.

```
                                 ┌─────────────────────┐
                                 │  ApplicationObjecti  │
                                 │  ves.md + SalesPack  │
                                 │  (System Prompt)     │
                                 └─────────┬───────────┘
                                           │
┌──────────────┐   Status=New   ┌──────────▼──────────┐    ┌──────────────────┐
│  DevRequest  │ ──────────────▶│  Product Owner      │───▶│  GitHub Models   │
│  (New)       │                │  Agent Service      │    │  (GPT-4o)        │
└──────────────┘                └──────────┬──────────┘    └──────────────────┘
                                           │
                          ┌────────────────┼────────────────┐
                          ▼                ▼                ▼
                   ┌─────────────┐  ┌───────────┐  ┌──────────────┐
                   │  Approve    │  │  Reject   │  │  Clarify     │
                   │  Status →   │  │  Status → │  │  Post comment│
                   │  Triaged    │  │  Rejected │  │  Status →    │
                   │  + comment  │  │  + comment│  │  NeedsClarif │
                   └─────────────┘  └───────────┘  └──────────────┘
                                                          │
                                                          ▼
                                                   Submitter replies
                                                          │
                                                          ▼
                                                   Agent re-reviews
```

### 6.2 Trigger Mechanism

The agent runs as a **hosted background service** (`IHostedService`) inside the existing AIDev.Api process. It polls the database on a configurable interval.

| Trigger | Mechanism |
|---------|-----------|
| New request submitted | Poll for `Status = New` requests |
| Clarification reply received | Poll for `Status = NeedsClarification` with new comments since last agent review |
| Manual re-review | API endpoint to queue a request for re-review |

**Polling interval:** Configurable (default 30 seconds). Low overhead since it's a simple DB query.

**Why polling over webhooks/events?**
- No external infrastructure needed (no message queue, no webhook endpoint)
- Works identically in local dev and Azure deploy
- Simple to debug and monitor
- Can be upgraded to event-driven (SignalR / Azure Service Bus) later if volume demands it

### 6.3 New Status Values

The existing `RequestStatus` enum is extended:

| Status | Description | Set By |
|--------|-------------|--------|
| `New` | Just submitted, awaiting triage | System (existing) |
| `NeedsClarification` | Agent needs more info from submitter | **Product Owner Agent** |
| `Triaged` | Agent approved, awaiting architecture | **Product Owner Agent** |
| `Rejected` | Agent rejected (not aligned / out of scope) | **Product Owner Agent** |
| `Approved` | Human approved architecture (Phase 3) | Human (existing) |
| `InProgress` | Being implemented (Phase 4) | Planning Agent (existing) |
| `Done` | Deployed to UAT | System (existing) |

### 6.4 LLM Integration

**Provider:** GitHub Models (https://github.com/marketplace/models) — LLM inference hosted by GitHub, accessed via an OpenAI-compatible REST API using your existing GitHub PAT. No separate Azure OpenAI resource or additional subscription needed.

**Model:** GPT-4o — chosen for strong reasoning, large context window (128K), and fast response times. Available on GitHub Models marketplace.

**Endpoint:** `https://models.inference.ai.azure.com` — OpenAI-compatible API.

**NuGet Package:** `Azure.AI.OpenAI` (v2.x) — the same SDK works with GitHub Models since it exposes an OpenAI-compatible endpoint. The client is pointed at the GitHub Models inference URL with the GitHub PAT as the API key.

#### System Prompt Construction

The agent's system prompt is assembled from the project's reference documents stored in the repository:

```
┌─ System Prompt ─────────────────────────────────────────────┐
│                                                             │
│  Role: You are a Product Owner Agent for the AI Dev         │
│  Pipeline platform.                                         │
│                                                             │
│  Reference Documents:                                       │
│  ┌─ ApplicationObjectives.md (full content) ──────────────┐ │
│  │  Product purpose, core objectives, success criteria,   │ │
│  │  guiding principles                                    │ │
│  └────────────────────────────────────────────────────────┘ │
│  ┌─ ApplicationSalesPack.md (full content) ───────────────┐ │
│  │  Features, benefits, target audience, value prop       │ │
│  └────────────────────────────────────────────────────────┘ │
│                                                             │
│  Instructions:                                              │
│  1. Review the request for completeness                     │
│  2. Check alignment with product objectives                 │
│  3. Check alignment with sales pack positioning             │
│  4. Return a structured JSON decision                       │
│                                                             │
└─────────────────────────────────────────────────────────────┘
```

#### LLM Request/Response Contract

The agent sends a structured prompt and expects a JSON response:

**User message** (per request):
```
Review the following development request:

Title: {title}
Type: {requestType}
Priority: {priority}
Description: {description}
Steps to Reproduce: {stepsToReproduce}
Expected Behavior: {expectedBehavior}
Actual Behavior: {actualBehavior}
Project: {projectName}
Submitted By: {submittedBy}

{If NeedsClarification re-review:}
Previous agent questions:
{previous agent comment}

Submitter's reply:
{latest submitter comment}
```

**Expected JSON response:**
```json
{
  "decision": "approve" | "reject" | "clarify",
  "reasoning": "Detailed explanation of why...",
  "alignmentScore": 0-100,
  "completenessScore": 0-100,
  "salesAlignmentScore": 0-100,
  "clarificationQuestions": ["Question 1?", "Question 2?"],
  "suggestedPriority": "High",
  "tags": ["ui", "performance", "data-model"]
}
```

### 6.5 Data Model Changes

#### New Entity: AgentReview

| Field | Type | Description |
|-------|------|-------------|
| Id | int (PK) | Auto-increment |
| DevRequestId | int (FK) | The request being reviewed |
| AgentType | string | "ProductOwner" (extensible for future agents) |
| Decision | enum | Approve, Reject, Clarify |
| Reasoning | string | LLM's explanation |
| AlignmentScore | int | 0-100 score vs product objectives |
| CompletenessScore | int | 0-100 score for request detail |
| SalesAlignmentScore | int | 0-100 score vs sales pack |
| SuggestedPriority | string? | Agent's priority recommendation |
| Tags | string? | JSON array of suggested tags |
| PromptTokens | int | LLM token usage (prompt) |
| CompletionTokens | int | LLM token usage (completion) |
| ModelUsed | string | e.g. "gpt-4o-2024-08-06" |
| DurationMs | int | Time taken for LLM call |
| CreatedAt | DateTime | UTC timestamp |

#### RequestComment Changes

Add a field to distinguish human vs agent comments:

| Field | Type | Description |
|-------|------|-------------|
| IsAgentComment | bool | `false` for human, `true` for agent |
| AgentReviewId | int? (FK) | Links to the AgentReview that generated this comment |

#### DevRequest Changes

| Field | Type | Description |
|-------|------|-------------|
| LastAgentReviewAt | DateTime? | When the agent last reviewed this request |
| AgentReviewCount | int | Number of times the agent has reviewed (prevents infinite loops) |

### 6.6 New Backend Components

#### 6.6.1 Services/ProductOwnerAgentService.cs (IHostedService)

**Responsibilities:**
- Runs on a timer (configurable interval)
- Queries for unreviewed requests (`Status = New`) and re-review candidates (`Status = NeedsClarification` with new comments)
- Calls the LLM service to evaluate each request
- Processes the LLM decision:
  - **Approve** → set status to `Triaged`, post approval comment, create AgentReview record
  - **Reject** → set status to `Rejected`, post rejection reasoning as comment, create AgentReview record
  - **Clarify** → set status to `NeedsClarification`, post questions as comment, create AgentReview record
- Updates GitHub Issue labels/comments via existing GitHubService
- Enforces a max review count (default: 3) to prevent infinite clarification loops — escalates to human after max

**Error handling:**
- LLM call failures are logged and retried on next poll cycle
- Malformed LLM responses fall back to a "needs human review" status
- Each request is processed independently — one failure doesn't block others

#### 6.6.2 Services/LlmService.cs

**Responsibilities:**
- Wraps GitHub Models client (OpenAI-compatible API via `Azure.AI.OpenAI` SDK)
- Constructs system prompt from reference documents (loaded once at startup, cached)
- Sends request data as user message
- Parses structured JSON response
- Tracks token usage and timing
- Configurable model, temperature, max tokens

**Interface:**
```csharp
public interface ILlmService
{
    Task<AgentReviewResult> ReviewRequestAsync(DevRequest request, 
        List<RequestComment>? conversationHistory = null);
}

public record AgentReviewResult(
    AgentDecision Decision,
    string Reasoning,
    int AlignmentScore,
    int CompletenessScore,
    int SalesAlignmentScore,
    List<string>? ClarificationQuestions,
    string? SuggestedPriority,
    List<string>? Tags,
    int PromptTokens,
    int CompletionTokens,
    string ModelUsed,
    int DurationMs
);

public enum AgentDecision { Approve, Reject, Clarify }
```

#### 6.6.3 Services/ReferenceDocumentService.cs

**Responsibilities:**
- Loads `ApplicationObjectives.md` and `ApplicationSalesPack.md` from the file system (or embedded resources)
- Caches content in memory
- Provides combined content for system prompt construction
- Supports reload (for hot-updating reference docs without restart)

#### 6.6.4 Controllers/AgentController.cs

New API endpoints for agent-related operations:

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/agent/reviews` | List all agent reviews (filterable by decision, request) |
| `GET` | `/api/agent/reviews/{id}` | Get a specific agent review with full detail |
| `GET` | `/api/requests/{id}/reviews` | Get all agent reviews for a request |
| `POST` | `/api/agent/review/{requestId}` | Manually trigger a re-review for a request |
| `POST` | `/api/agent/override/{requestId}` | Human override — approve/reject bypassing the agent |
| `GET` | `/api/agent/stats` | Agent performance stats (approval rate, avg scores, tokens used) |
| `GET` | `/api/agent/config` | Current agent configuration |
| `PUT` | `/api/agent/config` | Update agent configuration (polling interval, max reviews, etc.) |

### 6.7 Configuration

New section in `appsettings.json`:

```json
{
  "GitHubModels": {
    "Endpoint": "https://models.inference.ai.azure.com",
    "ModelName": "gpt-4o"
  },
  "ProductOwnerAgent": {
    "Enabled": true,
    "PollingIntervalSeconds": 30,
    "MaxReviewsPerRequest": 3,
    "MaxConcurrentReviews": 3,
    "Temperature": 0.3,
    "MaxTokens": 2000,
    "ReferenceDocsPath": "./",
    "AutoApproveThreshold": 85,
    "AutoRejectThreshold": 25
  }
}
```

**Secrets** (stored in User Secrets / Azure Key Vault, never in appsettings.json):
- `GitHub:PersonalAccessToken` — already configured from Phase 1, reused for GitHub Models API authentication

### 6.8 Frontend Changes

#### 6.8.1 Request Detail — Agent Review Panel

The `RequestDetail` component gains a new section showing agent review history:

- **Agent Decision Badge** — Approved (green), Rejected (red), Needs Clarification (amber)
- **Scores** — Alignment, Completeness, Sales Alignment as progress bars or score chips
- **Reasoning** — Expandable section showing the agent's full explanation
- **Suggested Priority** — Shown if the agent recommends a different priority than submitted
- **Tags** — Display agent-suggested tags as chips
- **Review History** — Timeline of all agent reviews for this request (supports multi-round clarification)

#### 6.8.2 Request Detail — Agent Comments

Agent comments are visually distinct from human comments:
- Different background colour / left-border stripe
- "AI Agent" avatar/icon instead of user avatar
- Labels indicating comment source: "Product Owner Agent"

#### 6.8.3 Request List — New Filters

- Filter by `NeedsClarification` status
- Filter by agent decision (Approved / Rejected / Clarify)
- "My requests needing clarification" quick filter for submitters

#### 6.8.4 Dashboard — Agent Stats

New dashboard section:
- Requests triaged today / this week
- Approval / rejection / clarification rate pie chart
- Average alignment score trend
- Token usage and cost tracking
- Agent response time (median, P95)

#### 6.8.5 Admin Settings — Agent Configuration

New admin tab:
- Enable/disable the Product Owner Agent
- Adjust polling interval, thresholds, temperature
- View/reload reference documents
- View agent activity log

#### 6.8.6 New API Types (api.ts additions)

```typescript
interface AgentReview {
  id: number;
  devRequestId: number;
  agentType: string;
  decision: 'Approve' | 'Reject' | 'Clarify';
  reasoning: string;
  alignmentScore: number;
  completenessScore: number;
  salesAlignmentScore: number;
  suggestedPriority?: string;
  tags?: string[];
  promptTokens: number;
  completionTokens: number;
  modelUsed: string;
  durationMs: number;
  createdAt: string;
}

interface AgentStats {
  totalReviews: number;
  approvalRate: number;
  rejectionRate: number;
  clarificationRate: number;
  avgAlignmentScore: number;
  avgCompletenessScore: number;
  totalTokensUsed: number;
  avgResponseTimeMs: number;
}
```

### 6.9 GitHub Integration

When the agent makes a decision:

1. **Approve** → Add label `agent:approved` to the GitHub Issue, post reasoning as Issue comment
2. **Reject** → Add label `agent:rejected`, close the Issue with rejection reasoning as comment
3. **Clarify** → Add label `agent:needs-info`, post clarification questions as Issue comment

When a submitter replies (via the web app), the agent removes `agent:needs-info` and re-reviews.

### 6.10 Security & Guardrails

| Concern | Mitigation |
|---------|-----------|
| LLM hallucination | Structured JSON response with validation; fallback to human review on parse failure |
| Infinite clarification loops | Max review count per request (default: 3); escalates to human |
| Agent bias | All decisions logged with full reasoning; human override always available |
| Cost control | Token tracking per review; configurable max tokens; daily/monthly budget alerting |
| Prompt injection via request content | Request content is clearly delimited in the prompt; system prompt cannot be overridden |
| Sensitive data in LLM calls | GitHub Models hosted on Azure infrastructure; governed by GitHub's data policies; no training on customer data |

### 6.11 Sequence Diagram — Happy Path (Approve)

```
Tester           API              DB              PO Agent          LLM
  │                │                │                 │                │
  │ POST /requests │                │                 │                │
  │───────────────▶│ Save(New)      │                 │                │
  │                │───────────────▶│                 │                │
  │  201 Created   │                │                 │                │
  │◀───────────────│                │                 │                │
  │                │                │   Poll(New)     │                │
  │                │                │◀────────────────│                │
  │                │                │   [request]     │                │
  │                │                │────────────────▶│                │
  │                │                │                 │  Review(req)   │
  │                │                │                 │───────────────▶│
  │                │                │                 │  {approve,85}  │
  │                │                │                 │◀───────────────│
  │                │                │  Save(Triaged)  │                │
  │                │                │◀────────────────│                │
  │                │                │  Save(Review)   │                │
  │                │                │◀────────────────│                │
  │                │                │  Save(Comment)  │                │
  │                │                │◀────────────────│                │
```

### 6.12 Sequence Diagram — Clarification Flow

```
Tester           API              DB              PO Agent          LLM
  │                │                │                 │                │
  │                │                │   Poll(New)     │                │
  │                │                │◀────────────────│                │
  │                │                │                 │  Review(req)   │
  │                │                │                 │───────────────▶│
  │                │                │                 │  {clarify}     │
  │                │                │                 │◀───────────────│
  │                │                │  Status=NeedsC  │                │
  │                │                │◀────────────────│                │
  │                │                │  Comment(Qs)    │                │
  │                │                │◀────────────────│                │
  │                │                │                 │                │
  │ [Sees questions on web app]    │                 │                │
  │ POST comment   │                │                 │                │
  │───────────────▶│ Save comment   │                 │                │
  │                │───────────────▶│                 │                │
  │                │                │                 │                │
  │                │                │  Poll(NeedsC+   │                │
  │                │                │  new comments)  │                │
  │                │                │◀────────────────│                │
  │                │                │                 │  Review(req+   │
  │                │                │                 │  conversation) │
  │                │                │                 │───────────────▶│
  │                │                │                 │  {approve,90}  │
  │                │                │                 │◀───────────────│
  │                │                │  Status=Triaged │                │
  │                │                │◀────────────────│                │
```

### 6.13 Migration Plan

| Step | Change | Risk |
|------|--------|------|
| 1 | Add `NeedsClarification` to `RequestStatus` enum | Low — additive enum change |
| 2 | Add `AgentReview` entity + migration | Low — new table only |
| 3 | Add `IsAgentComment`, `AgentReviewId` to `RequestComment` | Low — nullable columns |
| 4 | Add `LastAgentReviewAt`, `AgentReviewCount` to `DevRequest` | Low — nullable/default columns |
| 5 | Add `Azure.AI.OpenAI` NuGet package (used with GitHub Models endpoint) | Low — new dependency |
| 6 | Implement `LlmService`, `ReferenceDocumentService` | None — new code |
| 7 | Implement `ProductOwnerAgentService` | Low — background service, controlled by config flag |
| 8 | Add `AgentController` endpoints | Low — new controller |
| 9 | Frontend: agent review panel, comment styling, filters | None — UI additions |
| 10 | Frontend: admin agent config, dashboard stats | None — UI additions |

No breaking changes to existing Phase 1 functionality. The agent is disabled by default and opt-in via configuration.

## 7. Future Phases (Preview)

### Phase 3 — Architect Agent
- Triggered on Product Owner approval (status = `Triaged`)
- Reads codebase from the target repo via GitHub API
- Proposes solution, lists impacted files, migration needs
- Posts solution as an Issue comment and AgentReview record
- Human reviews and approves before implementation

### Phase 4 — Planning Agent
- Creates feature branches from approved solutions
- Triggers implementation agents (via GitHub Actions / API)
- Monitors PR status and test results
- Batches merges and triggers UAT deployment

## 8. Tech Stack Summary

| Layer | Technology |
|-------|-----------|
| Frontend | React 19, TypeScript, Vite 7, MSAL.js |
| Backend | .NET 10, C#, EF Core, SQLite |
| Auth | Microsoft Entra ID (Azure AD) |
| AI/LLM | GitHub Models (GPT-4o) via OpenAI-compatible API |
| GitHub | Octokit, GitHub REST API |
| CI/CD | GitHub Actions |
| Hosting | Azure App Service + Static Web Apps |
