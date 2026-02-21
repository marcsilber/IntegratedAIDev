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

## Testing
- Backend: Run `dotnet build` to verify compilation
- Frontend: Run `npx tsc --noEmit` to verify TypeScript compilation
- Run `dotnet test` if test project exists
- Run `npx tsc -b` for full build verification

## Data Model
- `DevRequest` is the core entity, linked to `Project`, `RequestComment`, `AgentReview`, `ArchitectReview`, and `Attachment`
- Status flow: New → Triaged → ArchitectReview → Approved → InProgress → Done
- Copilot fields on DevRequest track implementation session state

## Important Rules
- Do NOT modify files outside the solution scope unless fixing a direct dependency
- Do NOT introduce new dependencies unless specified in the solution
- Ensure all existing functionality continues to work
- Write clear, concise commit messages referencing the issue number
- Add EF Core migrations if data model changes are specified (`dotnet ef migrations add <name>`)
