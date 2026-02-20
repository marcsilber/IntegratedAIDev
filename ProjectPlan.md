# AI Dev Pipeline — Project Plan

## Phase 1: Structured Request Intake

### Progress Summary

| # | Task | Status | Notes |
|---|------|--------|-------|
| 1 | Define project structure & solution | ✅ Done | .slnx + folder structure created |
| 2 | Scaffold .NET 8 Web API backend | ✅ Done | .NET 10, EF Core, Octokit, CORS configured |
| 3 | Scaffold React frontend (Vite + TS) | ✅ Done | Vite + React 19 + TypeScript, MSAL, Axios, Router |
| 4 | Define request data model & API endpoints | ✅ Done | DevRequest, RequestComment, CRUD + Dashboard |
| 5 | Add Entra ID authentication | ✅ Done | App Registrations created via Azure CLI |
| 6 | Build request submission form | ✅ Done | RequestForm with validation, bug-specific fields |
| 7 | Build request list / dashboard view | ✅ Done | RequestList, RequestDetail, Dashboard components |
| 8 | Add GitHub Issues integration | ✅ Done | GitHubService + NullGitHubService fallback |
| 9 | Document setup & configuration steps | ✅ Done | |

### Detailed Task Breakdown

#### Task 1: Define Project Structure & Solution ✅
- [x] High-level design document created
- [x] Project plan created
- [x] .NET solution file (AIDev.slnx)
- [x] Folder structure for API and Web projects

#### Task 2: Scaffold .NET Web API Backend ✅
- [x] Create AIDev.Api project (webapi template, .NET 10)
- [x] Add NuGet packages: EF Core SQLite, EF Core Design, Octokit, Microsoft.Identity.Web
- [x] Configure Program.cs (CORS, auth placeholder, OpenAPI, EF Core)
- [x] Add appsettings.json with config placeholders (AzureAd, GitHub, CORS)

#### Task 3: Scaffold React Frontend (Vite + TS) ✅
- [x] Create AIDev.Web with Vite + React 19 + TypeScript
- [x] Add dependencies: @azure/msal-browser, @azure/msal-react, axios, react-router-dom
- [x] Configure .env with VITE_API_URL
- [x] Set up project structure (components, services, auth)

#### Task 4: Define Request Data Model & API Endpoints ✅
- [x] Create entity classes (DevRequest, RequestComment) with enums
- [x] Create AppDbContext with SQLite configuration (enum stored as string)
- [x] Create initial EF Core migration (InitialCreate)
- [x] Implement RequestsController (CRUD + comments + filtering)
- [x] Implement DashboardController (stats by status/type/priority)
- [x] Add DTOs (CreateRequestDto, UpdateRequestDto, RequestResponseDto, DashboardDto)

#### Task 5: Add Entra ID Authentication ✅
- [x] Created API App Registration (AIDev-API): `1d4f6501-5d39-470a-8b57-57c9fd328836`
- [x] Created Web App Registration (AIDev-Web): `16417242-11f1-4548-add4-c631568df68a`
- [x] Added `access_as_user` scope to API app
- [x] Added SPA redirect URI (`http://localhost:5173`) to Web app
- [x] Granted Web app permission to API scope
- [x] Configure MSAL.js in React (authConfig.ts) with real IDs
- [x] Add MsalProvider wrapper in main.tsx
- [x] Add login/logout UI with AuthenticatedTemplate/UnauthenticatedTemplate
- [x] Configure JWT Bearer auth in .NET API (Microsoft.Identity.Web)
- [x] Add [Authorize] to API controllers
- [x] Extract user claims (name, preferred_username) in controllers

#### Task 6: Build Request Submission Form ✅
- [x] Create RequestForm component with all fields
- [x] Add form validation (required fields, maxLength)
- [x] Implement API call to POST /api/requests
- [x] Add success/error feedback (navigate to detail on success)
- [x] Bug-specific fields (steps, expected, actual) shown conditionally
- [ ] File upload support (screenshots) — stretch/future

#### Task 7: Build Request List / Dashboard View ✅
- [x] Create RequestList component with search, status, and type filtering
- [x] Create RequestDetail component with status update + comments
- [x] Create Dashboard component with stats (by status, type, priority)
- [x] Add routing (React Router: /, /new, /requests/:id, /dashboard)
- [x] Add colored status badges and priority indicators

#### Task 8: Add GitHub Issues Integration ✅
- [x] Create GitHubService + NullGitHubService (graceful fallback) in API
- [x] Configure GitHub PAT via appsettings.json (GitHub:PersonalAccessToken)
- [x] Auto-create Issue on request submission (non-blocking on failure)
- [x] Sync Issue URL/number back to DevRequest
- [x] Map request type + priority to GitHub labels
- [x] Update/close Issues when request status changes

#### Task 9: Document Setup & Configuration
- [ ] README.md with setup instructions
- [ ] Entra ID App Registration guide
- [ ] GitHub token setup guide
- [ ] Local development instructions
- [ ] Deployment guide (Azure) — outline

---

### Configuration Required (User Action Items)

| Item | Status | Details |
|------|--------|---------|
| Entra ID App Registration | ✅ Done | API: `1d4f6501-5d39-470a-8b57-57c9fd328836`, Web: `16417242-11f1-4548-add4-c631568df68a` |
| GitHub PAT or App | ✅ Done | Stored in .NET User Secrets |
| GitHub Repo (IntegratedAIDev) | ✅ Done | Owner: marcsilber |

---

*Last updated: 2026-02-20*
