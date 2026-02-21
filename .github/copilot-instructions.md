# Copilot Instructions for IntegratedAIDev

## Project Structure
- Backend: `src/AIDev.Api/AIDev.Api/` — .NET 10 Web API
- Frontend: `src/AIDev.Web/` — React 19 + TypeScript + Vite 7
- Database: SQLite via EF Core (auto-migrated on startup)

## Architecture
- Controller → Service → EF Core (AppDbContext)
- Background services: `ProductOwnerAgentService`, `ArchitectAgentService`, `ImplementationTriggerService`, `PrMonitorService`
- LLM integration: Azure.AI.OpenAI v2.1.0 via GitHub Models endpoint
- Auth: Microsoft Entra ID (MSAL.js frontend, Microsoft.Identity.Web backend)

## Backend Conventions
- Use nullable reference types (`<Nullable>enable</Nullable>`)
- Follow existing controller/service patterns in the codebase
- Add XML doc comments on public members
- Use `record` types for DTOs
- Enums use string conversion in EF Core (`.HasConversion<string>()`)
- Configuration via `IConfiguration` (appsettings.json)
- Services registered in `Program.cs`
- Background services extend `BackgroundService` using `IServiceScopeFactory` for scoped DbContext access

## Frontend Conventions
- Functional components with TypeScript interfaces
- API calls in `src/services/api.ts` using Axios with MSAL interceptor
- Components in `src/components/`
- Inline styles (no CSS modules or Tailwind)
- **ALWAYS-DARK THEME**: The app uses a forced dark theme. All CSS variables are defined in `src/AIDev.Web/src/App.css` `:root`. Do NOT use light-mode defaults or `prefers-color-scheme` media queries.
- **CSS Variables**: Use `var(--text)` (#E5E7EB), `var(--bg)` (#0B0F14), `var(--surface)` (#13181F), `var(--border)` (#2D3748), `var(--primary)` (#00D1B2), `var(--text-muted)` (#9CA3AF), `var(--danger)` (#ef4444), `var(--success)` (#22c55e), `var(--warning)` (#FFB020). Avoid hardcoded hex colours in inline styles where a variable exists.
- **CSS Cascade**: `index.css` loads before `App.css`. Do NOT define theme variables (--text, --bg, --border, --shadow) in `index.css` — they belong in `App.css` only.
- **Text Color**: All text must be readable on dark backgrounds. Default text is silver/light gray. Never use dark text colors (#000, #333, #666, etc.) unless on an explicitly light-colored badge or button.

## Testing
- Backend: Run `dotnet build` to verify compilation
- Frontend: Run `npx tsc --noEmit` to verify TypeScript compilation
- Run `dotnet test` if test project exists
- Run `npx tsc -b` for full build verification

## Deployment & Pipeline
- **Deployment Mode**: `Auto` (default) — PRs auto-merge after code review, deploy on push. `Staged` — Code Review approves but does NOT merge; PRs accumulate until a human clicks Deploy in the admin panel.
- **Auto-Retry**: Failed GitHub Actions deployments are automatically retried up to `MaxDeployRetries` (default 3). First tries `rerun-failed-jobs`, then falls back to `workflow_dispatch`.
- **Deploy Endpoints**: `GET /api/orchestrator/staged` (list staged PRs), `POST /api/orchestrator/deploy` (merge all staged PRs), `POST /api/orchestrator/deploy/trigger-workflows` (manual redeploy), `POST /api/orchestrator/deploy/retry/{requestId}` (retry specific deployment), `GET /api/orchestrator/deploy/status` (mode + recent workflow runs).
- **Config**: `PipelineOrchestrator:DeploymentMode` and `PipelineOrchestrator:MaxDeployRetries` in appsettings.json, editable from admin panel.
- **GitHub Actions**: `deploy-api.yml` (Azure App Service) and `deploy-web.yml` (Azure Static Web Apps), both have `push` + `workflow_dispatch` triggers.

## Data Model
- `DevRequest` is the core entity, linked to `Project`, `RequestComment`, `AgentReview`, `ArchitectReview`, and `Attachment`
- Status flow: New → Triaged → ArchitectReview → Approved → InProgress → Done
- `DeploymentRetryCount` on DevRequest tracks auto-retry attempts for failed deployments
- Copilot fields on DevRequest track implementation session state

## Important Rules
- Do NOT modify files outside the solution scope unless fixing a direct dependency
- Do NOT introduce new dependencies unless specified in the solution
- Ensure all existing functionality continues to work
- Write clear, concise commit messages referencing the issue number
- Add EF Core migrations if data model changes are specified (`dotnet ef migrations add <name>`)
- When fixing CSS/styling issues: audit ALL stylesheets and ALL components for the same problem pattern, not just the ones explicitly mentioned
- Identify and fix ROOT CAUSES, not just symptoms. If text is invisible, find out WHY (variable conflict? wrong specificity? missing style?) and fix the source
