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

## 6. Future Phases (Preview)

### Phase 2 — Product Owner Agent
- Triggered when a new Issue is created (via GitHub webhook or polling)
- LLM reviews the request against product objectives and sales pack
- Posts clarification questions as Issue comments
- Approves or rejects with reasoning
- Updates request status in the web app

### Phase 3 — Architect Agent
- Triggered on Product Owner approval
- Reads codebase from the target repo
- Proposes solution, lists impacted files, migration needs
- Posts solution as an Issue comment for human review

### Phase 4 — Planning Agent
- Creates feature branches from approved solutions
- Triggers implementation agents (via GitHub Actions / API)
- Monitors PR status and test results
- Batches merges and triggers UAT deployment

## 7. Tech Stack Summary

| Layer | Technology |
|-------|-----------|
| Frontend | React 18, TypeScript, Vite, MSAL.js |
| Backend | .NET 8, C#, EF Core, SQLite |
| Auth | Microsoft Entra ID (Azure AD) |
| GitHub | Octokit, GitHub REST API |
| CI/CD | GitHub Actions (future) |
| Hosting | Azure App Service (future) |
