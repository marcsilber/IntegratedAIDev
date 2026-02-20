# AI Dev Pipeline — Application Features Inventory

> **Generated:** 2026-02-20
> **Covers:** Phase 1 (Structured Request Intake) and Phase 2 (Product Owner Agent)

---

## 1. Authentication & Identity

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Microsoft Entra ID (Azure AD) SSO | Users authenticate via Microsoft Entra ID — no local passwords. MSAL.js handles browser-side OAuth redirect flow. | Phase 1 | ✅ Implemented |
| JWT Bearer Token Validation | All API endpoints are protected with `[Authorize]`. The .NET API validates tokens against the Entra ID tenant using `Microsoft.Identity.Web`. | Phase 1 | ✅ Implemented |
| User Claim Extraction | API extracts `name`, `preferred_username`, `upn`, and `email` claims from the JWT to attribute requests and comments to the authenticated user. | Phase 1 | ✅ Implemented |
| MSAL Token Interceptor | Axios interceptor silently acquires access tokens via `acquireTokenSilent` and falls back to interactive redirect on failure. Every API call is automatically authenticated. | Phase 1 | ✅ Implemented |
| Login / Logout UI | Navbar shows Sign In button for unauthenticated users and user's display name + Sign Out button when authenticated. `AuthenticatedTemplate` / `UnauthenticatedTemplate` gates all content. | Phase 1 | ✅ Implemented |
| Role-Based Access Control | Designed for tester/admin roles derived from Entra ID claims. | Phase 1 | 🔲 Planned |

---

## 2. Request Management

### 2.1 Request Submission

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Structured Request Form | Web form capturing: project, title, description, type (Bug/Feature/Enhancement/Question), and priority (Low/Medium/High/Critical). Full validation with required fields and maxLength constraints. | Phase 1 | ✅ Implemented |
| Bug-Specific Fields | Conditional fields for Bug type: Steps to Reproduce, Expected Behavior, Actual Behavior — shown/hidden dynamically based on selected request type. | Phase 1 | ✅ Implemented |
| Project Selection | Dropdown populated from active projects via `GET /api/projects`. Auto-selects if only one project is active. | Phase 1 | ✅ Implemented |
| Post-Submit Navigation | On successful creation, user is redirected to the newly created request's detail page. | Phase 1 | ✅ Implemented |
| Request Type Enum | Bug, Feature, Enhancement, Question — stored as string in SQLite via EF Core string enum converter. | Phase 1 | ✅ Implemented |
| Priority Enum | Low, Medium, High, Critical — stored as string. | Phase 1 | ✅ Implemented |

### 2.2 Request List & Filtering

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Request List View | Tabular list of all requests showing: ID, title, project, type, priority, status, submitted by, created date, and GitHub issue link. Sorted by newest first. | Phase 1 | ✅ Implemented |
| Search Filter | Free-text search across request titles and descriptions (server-side `Contains` query). | Phase 1 | ✅ Implemented |
| Status Filter | Dropdown filter for all status values: New, NeedsClarification, Triaged, Approved, InProgress, Done, Rejected. | Phase 1 | ✅ Implemented |
| Type Filter | Dropdown filter for request types: Bug, Feature, Enhancement, Question. | Phase 1 | ✅ Implemented |
| URL Query Params | Filter state persisted in URL search params — bookmarkable/shareable filtered views. | Phase 1 | ✅ Implemented |
| Color-Coded Badges | Status and priority badges use distinct colours (e.g., New = blue, Done = green, Critical = red). | Phase 1 | ✅ Implemented |
| Empty State | Friendly empty-state message with link to submit the first request when no results are found. | Phase 1 | ✅ Implemented |
| Priority Filter | Server-side filtering by priority. | Phase 1 | ✅ Implemented |

### 2.3 Request Detail View

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Full Request Detail | Displays all request fields: title, description, project, type, priority, status, submitter name/email, created/updated timestamps, and all bug-specific fields. | Phase 1 | ✅ Implemented |
| Status Update Buttons | Row of buttons for all 7 status values — click to update. Active status highlighted with its colour. | Phase 1 | ✅ Implemented |
| GitHub Issue Link | Clickable link to the synced GitHub Issue (when available), displayed as `GitHub Issue #N`. | Phase 1 | ✅ Implemented |
| Delete Request | Delete button with confirmation dialog. Removes request and navigates back to the list. | Phase 1 | ✅ Implemented |
| Back to List | Navigation button to return to the request list. | Phase 1 | ✅ Implemented |

### 2.4 Request CRUD API

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| `POST /api/requests` | Create request with full validation. Extracts user claims, saves to DB, triggers GitHub Issue creation. Returns 201 with the created resource. | Phase 1 | ✅ Implemented |
| `GET /api/requests` | List all requests with optional query params: status, type, priority, search. Includes comments, project, attachments, and agent reviews via eager loading. | Phase 1 | ✅ Implemented |
| `GET /api/requests/{id}` | Get single request by ID with all related data. | Phase 1 | ✅ Implemented |
| `PUT /api/requests/{id}` | Partial update — only fields present in the DTO are changed. Syncs updates to GitHub Issue. | Phase 1 | ✅ Implemented |
| `DELETE /api/requests/{id}` | Soft-delete not implemented; performs hard delete from database. | Phase 1 | ✅ Implemented |

---

## 3. Comments

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Add Comment | Users can post comments on any request via text area + button. Author is extracted from JWT claims. | Phase 1 | ✅ Implemented |
| Comment Display | Comments shown in chronological order with author name and timestamp. | Phase 1 | ✅ Implemented |
| Agent Comments (Visual Distinction) | Agent-generated comments are visually differentiated: purple left border, light purple background, robot emoji (🤖) prefix on author name. | Phase 2 | ✅ Implemented |
| `IsAgentComment` Flag | Boolean field on `RequestComment` distinguishes human from agent comments. | Phase 2 | ✅ Implemented |
| `AgentReviewId` Link | Agent comments are linked back to the specific `AgentReview` record that generated them. | Phase 2 | ✅ Implemented |
| Comment Count | Section heading shows total comment count. | Phase 1 | ✅ Implemented |

---

## 4. File Attachments

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| File Upload Endpoint | `POST /api/requests/{id}/attachments` — multipart/form-data upload, supports multiple files per request, 10 MB total / 5 MB per file limits. Stores files on disk with GUID-based filenames. | Phase 1 | ✅ Implemented |
| Attachment Download | `GET /api/requests/{requestId}/attachments/{attachmentId}` — serves the file with original content type and filename via authenticated download. | Phase 1 | ✅ Implemented |
| Attachment Deletion | `DELETE /api/requests/{requestId}/attachments/{attachmentId}` — removes file from disk and database. | Phase 1 | ✅ Implemented |
| Drag & Drop Upload | Drop zone in the request detail view — drag files onto the area to upload. Visual feedback on drag hover. | Phase 1 | ✅ Implemented |
| Click-to-Browse Upload | Click the drop zone to open a native file picker. Accepts images, PDF, text, and Word documents. | Phase 1 | ✅ Implemented |
| Clipboard Paste (Screenshots) | `onPaste` handler detects image data in the clipboard and uploads pasted screenshots directly. | Phase 1 | ✅ Implemented |
| Authenticated Image Thumbnails | Image attachments are rendered as inline thumbnails using `AuthImage` component that fetches through the authenticated API (blob URL + `revokeObjectURL` cleanup). | Phase 1 | ✅ Implemented |
| Attachment Grid | Attachments displayed in a grid layout with filename, size, delete button, and clickable download for non-image files. | Phase 1 | ✅ Implemented |
| File Type Support | Images (rendered as thumbnails), PDFs, text files, Word documents — server accepts any content type with a whitelist on the frontend file picker. | Phase 1 | ✅ Implemented |

---

## 5. GitHub Integration

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| GitHub Issue Auto-Creation | On each `POST /api/requests`, a GitHub Issue is automatically created on the project's linked repository with formatted markdown body. Non-blocking — request is saved even if GitHub call fails. | Phase 1 | ✅ Implemented |
| Issue Body Formatting | Issue body includes: type, priority, status, submitter, timestamps, description, and conditionally: steps to reproduce, expected behaviour, actual behaviour. | Phase 1 | ✅ Implemented |
| Label Mapping | Request type and priority are mapped to GitHub labels: `bug`/`feature`/`enhancement` + `priority:low`/`priority:medium`/etc. | Phase 1 | ✅ Implemented |
| Issue Number & URL Storage | GitHub issue number and HTML URL are stored on the `DevRequest` record and displayed in the UI. | Phase 1 | ✅ Implemented |
| Issue Update on Request Change | `PUT /api/requests/{id}` syncs title, body, and state (closed for Done/Rejected) back to the GitHub Issue. | Phase 1 | ✅ Implemented |
| Issue State Sync | When status changes to Done or Rejected, the GitHub Issue is closed. | Phase 1 | ✅ Implemented |
| NullGitHubService Fallback | When `GitHub:PersonalAccessToken` is not configured, a no-op service is registered that logs warnings but allows the app to function without GitHub integration. | Phase 1 | ✅ Implemented |
| Repository Listing | `GetRepositoriesAsync()` fetches all repositories for the authenticated GitHub user, used by the admin sync feature. | Phase 1 | ✅ Implemented |

---

## 6. Project Management

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Project Data Model | `Project` entity with: GitHubOwner, GitHubRepo, DisplayName, Description, FullName, IsActive, LastSyncedAt. Each request belongs to a project. | Phase 1 | ✅ Implemented |
| Active Projects API | `GET /api/projects` — returns only active projects for the request submission form. | Phase 1 | ✅ Implemented |
| Admin Projects API | `GET /api/admin/projects` — returns all projects (including inactive) for admin management. | Phase 1 | ✅ Implemented |
| GitHub Repo Sync | `POST /api/admin/projects/sync` — fetches all repos from the authenticated GitHub account via Octokit, upserts into the database. New repos default to inactive. | Phase 1 | ✅ Implemented |
| Project Update | `PUT /api/admin/projects/{id}` — rename display name, update description, or toggle active status. | Phase 1 | ✅ Implemented |
| Admin Settings UI | Dedicated admin page with: project table, Sync from GitHub button, active/inactive toggle switches, rename buttons, request count per project. | Phase 1 | ✅ Implemented |
| Project Request Count | Admin table shows the number of requests linked to each project. | Phase 1 | ✅ Implemented |
| Multi-Project Support | Requests are scoped to a project; the submission form dropdown restricts to active projects only. | Phase 1 | ✅ Implemented |

---

## 7. Product Owner Agent (AI Triage)

### 7.1 Core Agent

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Background Polling Service | `ProductOwnerAgentService` runs as an `IHostedService` / `BackgroundService`. Polls the database on a configurable interval (default 30s) for requests needing review. | Phase 2 | ✅ Implemented |
| New Request Auto-Review | Automatically picks up requests with `Status = New` and `AgentReviewCount == 0` for LLM-based review. | Phase 2 | ✅ Implemented |
| Clarification Re-Review | Re-processes requests with `Status = NeedsClarification` when the submitter posts a new human comment since the last agent review. | Phase 2 | ✅ Implemented |
| Max Reviews Per Request | Configurable cap (default 3) on how many times the agent will review a single request, preventing infinite clarification loops. | Phase 2 | ✅ Implemented |
| Batch Processing Limit | Processes at most 5 requests per polling cycle to avoid LLM rate-limiting. | Phase 2 | ✅ Implemented |
| Error Isolation | Each request is reviewed independently — one failure doesn't block others. Failures are logged and retried on the next cycle. | Phase 2 | ✅ Implemented |
| Configurable Enable/Disable | Agent can be toggled on/off via `ProductOwnerAgent:Enabled` configuration. | Phase 2 | ✅ Implemented |
| Startup Delay | 5-second delay on startup to let the application fully initialize before polling begins. | Phase 2 | ✅ Implemented |

### 7.2 LLM Integration

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| GitHub Models / OpenAI-Compatible API | Uses `OpenAI` SDK pointed at GitHub Models endpoint (`https://models.inference.ai.azure.com`). Authenticates with GitHub PAT. | Phase 2 | ✅ Implemented |
| GPT-4o Model | Default model is `gpt-4o` — configurable via `GitHubModels:ModelName`. | Phase 2 | ✅ Implemented |
| System Prompt with Reference Docs | System prompt includes full content of `ApplicationObjectives.md` and `ApplicationSalesPack.md` for alignment evaluation. | Phase 2 | ✅ Implemented |
| Structured JSON Response Contract | LLM is instructed to return a strict JSON schema with: decision, reasoning, scores (alignment, completeness, sales alignment), clarification questions, suggested priority, tags, duplicate detection. | Phase 2 | ✅ Implemented |
| Response Parsing with Fallback | Parses JSON responses with markdown fence stripping. On parse failure, defaults to "Clarify" with human escalation message. | Phase 2 | ✅ Implemented |
| Token Usage Tracking | Records prompt tokens and completion tokens per review for cost monitoring. | Phase 2 | ✅ Implemented |
| Timing Tracking | Records LLM call duration in milliseconds per review. | Phase 2 | ✅ Implemented |
| Configurable Temperature & Max Tokens | Temperature (default 0.3) and max output tokens (default 2000) are configurable via `appsettings.json`. | Phase 2 | ✅ Implemented |

### 7.3 Reference Document Service

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| File-Based Reference Loading | Loads `ApplicationObjectives.md` and `ApplicationSalesPack.md` from the file system at the configured path. | Phase 2 | ✅ Implemented |
| In-Memory Caching | Reference documents are cached in memory after first load. Thread-safe with lock. | Phase 2 | ✅ Implemented |
| Cache Reload Capability | `Reload()` method clears the cache — documents are re-read on next access. Supports hot-updating reference docs without restart. | Phase 2 | ✅ Implemented |
| Graceful Fallback | If no reference documents are found, the agent uses a fallback prompt: "No reference documents available. Use your best judgment." | Phase 2 | ✅ Implemented |

### 7.4 Agent Decisions & Status Transitions

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Approve → Triaged | Agent approves well-aligned, complete requests. Status set to `Triaged`. Approval comment posted with scores and reasoning. | Phase 2 | ✅ Implemented |
| Reject → Rejected | Agent rejects requests that are out of scope or contradict product direction. Status set to `Rejected`. Rejection reasoning posted as comment. | Phase 2 | ✅ Implemented |
| Clarify → NeedsClarification | Agent requests more detail when completeness is insufficient. Status set to `NeedsClarification`. Specific clarification questions posted as comment. | Phase 2 | ✅ Implemented |
| `NeedsClarification` Status | New enum value enabling the clarification conversation loop between agent and submitter. | Phase 2 | ✅ Implemented |
| Scoring: Alignment (0–100) | Measures how well the request aligns with product objectives. | Phase 2 | ✅ Implemented |
| Scoring: Completeness (0–100) | Measures whether the request has enough detail to act on. | Phase 2 | ✅ Implemented |
| Scoring: Sales Alignment (0–100) | Measures whether the request supports the product's market positioning. | Phase 2 | ✅ Implemented |
| Suggested Priority | Agent can suggest a different priority than what the submitter selected. | Phase 2 | ✅ Implemented |
| Suggested Tags | Agent can propose tags/labels for the request (e.g., "ui", "performance", "data-model"). | Phase 2 | ✅ Implemented |

### 7.5 Duplicate Detection

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Existing Request Comparison | Before reviewing, the agent fetches up to 50 existing requests from the same project and includes them in the LLM prompt. | Phase 2 | ✅ Implemented |
| Duplicate Flagging | LLM response includes `isDuplicate` boolean and optional `duplicateOfRequestId` reference. | Phase 2 | ✅ Implemented |
| Duplicate Warning in Comments | When a duplicate is detected, the agent comment includes a "⚠️ Duplicate detected" warning with a reference to the duplicated request. | Phase 2 | ✅ Implemented |
| Reject Duplicate Rule | System prompt instructs the agent to reject requests that duplicate Done/InProgress/Approved/Triaged requests. | Phase 2 | ✅ Implemented |

### 7.6 Agent Review Records

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| AgentReview Entity | Full audit trail per review: decision, reasoning, all three scores, suggested priority, tags (JSON), token usage, model name, duration, and timestamp. | Phase 2 | ✅ Implemented |
| Agent Type Field | Extensible `AgentType` field (currently "ProductOwner") — ready for Architect Agent and Planning Agent in future phases. | Phase 2 | ✅ Implemented |
| Request-Review Relationship | `DevRequest.AgentReviews` — one-to-many. Tracks `LastAgentReviewAt` and `AgentReviewCount` on the request. | Phase 2 | ✅ Implemented |
| Latest Review in Request DTO | `RequestResponseDto` includes `LatestAgentReview` and `AgentReviewCount` for display in the UI. | Phase 2 | ✅ Implemented |

### 7.7 GitHub Integration

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| GitHub Agent Labels | After each review, creates and applies a label (`agent:approved` green, `agent:rejected` red, `agent:needs-info` yellow) to the linked GitHub Issue. Removes any previous `agent:` label first. | Phase 2 | ✅ Implemented |
| GitHub Agent Comments | Posts the full agent review reasoning (decision, scores, suggested priority, tags, clarification questions) as a comment on the linked GitHub Issue. | Phase 2 | ✅ Implemented |

### 7.8 Token Budget Management

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Daily Token Budget | Configurable daily token budget (`DailyTokenBudget`, default 0 = no limit). Reviews are skipped when the budget is exceeded. | Phase 2 | ✅ Implemented |
| Monthly Token Budget | Configurable monthly token budget (`MonthlyTokenBudget`, default 0 = no limit). Reviews are skipped when the budget is exceeded. | Phase 2 | ✅ Implemented |
| Budget Enforcement | `ProductOwnerAgentService` checks token budgets before each polling cycle. Logs a warning and skips the cycle when exceeded. | Phase 2 | ✅ Implemented |

---

## 8. Agent API & Human Override

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| `GET /api/agent/reviews` | List all agent reviews with optional filters: requestId, decision. Ordered by newest first. | Phase 2 | ✅ Implemented |
| `GET /api/agent/reviews/{id}` | Get a single agent review with full detail. | Phase 2 | ✅ Implemented |
| `POST /api/agent/reviews/{id}/override` | Human override — changes the request status and posts a "Manual Override" comment with the overriding user's name and optional reason. | Phase 2 | ✅ Implemented |
| `GET /api/agent/stats` | Aggregate agent statistics: total reviews, counts by decision, average scores, total tokens used, average response time. | Phase 2 | ✅ Implemented |
| `GET /api/agent/config` | Returns the current agent configuration (enabled state, polling interval, max reviews, temperature, model name) from config. | Phase 2 | ✅ Implemented |
| Manual Re-Review Trigger | `POST /api/agent/reviews/re-review/{requestId}` — resets request to New status with AgentReviewCount=0, posts "Re-review triggered" comment, so the agent picks it up on next polling cycle. | Phase 2 | ✅ Implemented |
| Agent Config Update API | `PUT /api/agent/config` — accepts partial updates (enabled, polling interval, max reviews, temperature, daily/monthly token budgets). Changes are applied in-memory and persist until app restart. | Phase 2 | ✅ Implemented |
| Token Budget API | `GET /api/agent/budget` — returns daily/monthly token usage, configured budgets, exceeded flags, and review counts. | Phase 2 | ✅ Implemented |

---

## 9. Agent Review Panel (Frontend)

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Agent Review Card | Displayed on the request detail page when a review exists. Shows: decision badge (colour-coded), reasoning text, all three scores (colour-coded pass/fail thresholds), suggested priority, tags, model name, duration, and token count. | Phase 2 | ✅ Implemented |
| Override → Approve Button | Allows a human to override the agent decision and set status to Approved. Prompts for optional reason. | Phase 2 | ✅ Implemented |
| Override → Reject Button | Allows a human to override the agent decision and set status to Rejected. Prompts for optional reason. | Phase 2 | ✅ Implemented |
| Score Colour Coding | Alignment ≥ 60 and completeness ≥ 50 shown in green; below threshold shown in red. | Phase 2 | ✅ Implemented |
| Review Metadata | Displays: review count, timestamp, model used, LLM response time, and total token usage. | Phase 2 | ✅ Implemented |

---

## 10. Dashboard

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Summary Stats Cards | Top-level cards showing: Total Requests, New, In Progress, Done. | Phase 1 | ✅ Implemented |
| Status Breakdown | Breakdown of request counts by status (only non-zero shown). | Phase 1 | ✅ Implemented |
| Type Breakdown | Breakdown of request counts by type (Bug/Feature/Enhancement/Question). | Phase 1 | ✅ Implemented |
| Priority Breakdown | Breakdown of request counts by priority. | Phase 1 | ✅ Implemented |
| Recent Requests Table | Last 10 requests with links to detail pages. Shows ID, title, status badge, priority badge, and created date. | Phase 1 | ✅ Implemented |
| Agent Stats Section | Product Owner Agent stats: total reviews, approved/clarify/rejected counts, average alignment score, average completeness score, total tokens used, average response time. Shown only when reviews exist. | Phase 2 | ✅ Implemented |
| Dashboard API | `GET /api/dashboard` — returns all stats, breakdowns, and recent requests. | Phase 1 | ✅ Implemented |

---

## 11. Admin Settings

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Project Management Table | Lists all projects (active and inactive) with: display name, GitHub repo link, description, request count, active toggle, and rename button. Inactive projects shown at reduced opacity. | Phase 1 | ✅ Implemented |
| Sync from GitHub | Button that fetches all repositories from the authenticated GitHub account and upserts them into the database. Shows success message with repo count. | Phase 1 | ✅ Implemented |
| Active/Inactive Toggle | Toggle switch per project to enable or disable it for request submission. | Phase 1 | ✅ Implemented |
| Project Rename | Rename button with prompt dialog to update the display name. | Phase 1 | ✅ Implemented |
| Agent Configuration Display | Read-only table showing current agent settings: enabled/disabled badge, model name, polling interval, max reviews per request, and temperature. | Phase 2 | ✅ Implemented |
| Agent Config Update UI | Editable form on Admin Settings page: toggle for enabled/disabled, inputs for polling interval, max reviews, temperature, daily/monthly token budgets, with Save button. Runtime changes persist until API restart. | Phase 2 | ✅ Implemented |
| Token Budget Dashboard | Admin Settings page shows daily and monthly token usage cards with budget limits, review counts, and exceeded warnings (red border + message). | Phase 2 | ✅ Implemented |
| Re-Review Button | Button on request detail page (next to override buttons) to queue a fresh agent re-review. Confirmation dialog before triggering. | Phase 2 | ✅ Implemented |

---

## 12. Navigation & UI Shell

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| React Router SPA | Client-side routing with routes: `/` (Requests), `/new` (Submit), `/requests/:id` (Detail), `/dashboard`, `/admin`. | Phase 1 | ✅ Implemented |
| Navbar | Fixed navigation bar with: brand logo (⚡ AI Dev Pipeline), nav links (Requests, Dashboard, Admin, + New), user name display, and Sign In/Sign Out button. | Phase 1 | ✅ Implemented |
| Active Nav Links | `NavLink` components highlight the current route. | Phase 1 | ✅ Implemented |
| Login Prompt Page | Landing page for unauthenticated users with product description and Sign In button. | Phase 1 | ✅ Implemented |
| Error Banners | Consistent error banner styling across all pages. | Phase 1 | ✅ Implemented |
| Loading States | Loading indicators on all data-fetching pages and operations. | Phase 1 | ✅ Implemented |

---

## 13. Data Model Entities

| Entity | Key Fields | Purpose | Phase |
|--------|-----------|---------|-------|
| **DevRequest** | Title, Description, RequestType, Priority, Status, SubmittedBy, SubmittedByEmail, ProjectId, GitHubIssueNumber, GitHubIssueUrl, LastAgentReviewAt, AgentReviewCount | Core request entity — bug reports, features, enhancements, questions | Phase 1 + Phase 2 extensions |
| **RequestComment** | Author, Content, IsAgentComment, AgentReviewId | Human and agent comments on requests | Phase 1 + Phase 2 extensions |
| **Project** | GitHubOwner, GitHubRepo, DisplayName, Description, FullName, IsActive, LastSyncedAt | GitHub repository mapping for multi-project support | Phase 1 |
| **Attachment** | FileName, ContentType, FileSizeBytes, StoredPath, UploadedBy | File attachments (screenshots, documents) linked to requests | Phase 1 |
| **AgentReview** | AgentType, Decision, Reasoning, AlignmentScore, CompletenessScore, SalesAlignmentScore, SuggestedPriority, Tags, PromptTokens, CompletionTokens, ModelUsed, DurationMs | Full audit trail of every agent review action | Phase 2 |

---

## 14. Infrastructure & Configuration

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| SQLite Database | EF Core with SQLite provider. Auto-migration on startup via `db.Database.Migrate()`. | Phase 1 | ✅ Implemented |
| EF Core Migrations | 5 migrations: InitialCreate, AddProjects, AddAttachments, AddProductOwnerAgent, plus snapshot. | Phase 1–2 | ✅ Implemented |
| String Enum Serialization | All enums stored as strings in the database and serialized as strings in JSON via `JsonStringEnumConverter`. | Phase 1 | ✅ Implemented |
| CORS Configuration | Configurable allowed origins from `AllowedOrigins` or `Cors:AllowedOrigins` in appsettings. Defaults to `http://localhost:5173`. | Phase 1 | ✅ Implemented |
| OpenAPI / Swagger | `app.MapOpenApi()` in development mode for API documentation. | Phase 1 | ✅ Implemented |
| HTTPS Enforcement | `app.UseHttpsRedirection()` middleware enabled. Launch profile set to HTTPS (`https://localhost:7251`). | Phase 1 | ✅ Implemented |
| Static File Uploads | Uploaded files stored in `uploads/` directory relative to the API process working directory. | Phase 1 | ✅ Implemented |
| Conditional Service Registration | GitHub, LLM, and Agent services only registered when `GitHub:PersonalAccessToken` is configured — graceful degradation otherwise. | Phase 1–2 | ✅ Implemented |
| Vite + React 19 Frontend | TypeScript, hot-reload dev server, production build targeting Static Web Apps. | Phase 1 | ✅ Implemented |
| Environment Config | `VITE_API_URL` for frontend API base URL. `appsettings.json` / User Secrets for backend configuration. | Phase 1 | ✅ Implemented |

---

## 15. Planned (Not Yet Implemented)

| Feature | Description | Phase | Status |
|---------|-------------|-------|--------|
| Architect Agent | AI agent that reads the codebase and proposes solutions with impact analysis for approved (Triaged) requests. | Phase 3 | 🔲 Planned |
| Planning Agent | Creates feature branches, assigns implementation agents, monitors progress, coordinates merges, and triggers batch UAT deployments. | Phase 4 | 🔲 Planned |
| CI/CD Pipeline | GitHub Actions for automated build, test, and deployment to Azure. | Phase 1 | 🔲 Planned |
| Azure Deployment | App Service (API) + Static Web Apps (frontend) hosting. | Phase 1 | 🔲 Planned |
| Role-Based Access Control | Tester vs. admin roles enforced from Entra ID claims. | Phase 1 | 🔲 Planned |
| README / Documentation | Setup instructions, Entra ID guide, GitHub token guide, local dev, and deployment guide. | Phase 1 | 🔲 Planned |
| SignalR Real-Time Updates | Push-based notifications instead of polling for status changes. | Future | 🔲 Planned |
| Azure SQL Upgrade | Migration from SQLite to Azure SQL for production scale. | Future | 🔲 Planned |

---

## Summary

| Category | Implemented | Planned |
|----------|------------|---------|
| Authentication & Identity | 5 | 1 |
| Request Management | 21 | 0 |
| Comments | 6 | 0 |
| File Attachments | 9 | 0 |
| GitHub Integration | 8 | 0 |
| Project Management | 8 | 0 |
| Product Owner Agent | 33 | 0 |
| Agent API & Override | 8 | 0 |
| Agent Review Panel (UI) | 8 | 0 |
| Dashboard | 7 | 0 |
| Admin Settings | 7 | 0 |
| Navigation & UI | 6 | 0 |
| Infrastructure | 10 | 0 |
| Future Phases | 0 | 6 |
| **Total** | **140** | **6** |
