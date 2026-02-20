# Local Rebuild & Start

## Prerequisites

- .NET 10 SDK
- Node.js (v18+)
- Azure CLI (for Entra ID commands only)
- `dotnet ef` tools: `dotnet tool install --global dotnet-ef`
- User secrets already configured (GitHub PAT, etc.)

## Quick Start (two terminals)

### Terminal 1 — Backend API (https://localhost:7251)

```powershell
# Kill any leftover API process
Stop-Process -Name "AIDev.Api" -Force -ErrorAction SilentlyContinue

# Build and run with HTTPS profile
cd c:\Users\MarcSilberbauer\source\repos\AIDev\src\AIDev.Api\AIDev.Api
dotnet run --launch-profile https
```

The API will be available at **https://localhost:7251** (and http://localhost:5019).
EF Core migrations run automatically on startup — no manual `dotnet ef database update` needed.

### Terminal 2 — Frontend (http://localhost:5173)

```powershell
cd c:\Users\MarcSilberbauer\source\repos\AIDev\src\AIDev.Web
npm run dev
```

The frontend will be available at **http://localhost:5173**.
It connects to the API URL defined in `.env` (`VITE_API_URL=https://localhost:7251`).

## Rebuild Only (no run)

```powershell
# Backend
cd c:\Users\MarcSilberbauer\source\repos\AIDev\src\AIDev.Api\AIDev.Api
dotnet build

# Frontend type-check
cd c:\Users\MarcSilberbauer\source\repos\AIDev\src\AIDev.Web
npx tsc --noEmit
```

## EF Core Migrations

```powershell
cd c:\Users\MarcSilberbauer\source\repos\AIDev\src\AIDev.Api\AIDev.Api

# Add a new migration
dotnet ef migrations add <MigrationName>

# Apply manually (normally auto-applied on startup)
dotnet ef database update
```

## Common Issues

| Problem | Fix |
|---|---|
| `Unable to copy file ... AIDev.Api.exe` | Old process is still running. Run `Stop-Process -Name "AIDev.Api" -Force` first. |
| Port 5173 in use | Kill the old Vite process: `Get-NetTCPConnection -LocalPort 5173 -ErrorAction SilentlyContinue \| ForEach-Object { Stop-Process -Id $_.OwningProcess -Force }` |
| API starts on port 5019 only | You forgot `--launch-profile https`. The default profile is `http` (port 5019 only). |
| Auth redirect fails | Entra ID SPA redirect URIs must include `http://localhost:5173`. Check with: `az ad app show --id 16417242-11f1-4548-add4-c631568df68a --query "spa.redirectUris"` |

## Key URLs

| Service | Local URL |
|---|---|
| API (HTTPS) | https://localhost:7251 |
| API (HTTP) | http://localhost:5019 |
| Frontend | http://localhost:5173 |
| SQLite DB | `src/AIDev.Api/AIDev.Api/aidev.db` |

## Project Structure

```
AIDev/
├── src/
│   ├── AIDev.Api/AIDev.Api/   # .NET 10 Web API
│   │   ├── Controllers/
│   │   ├── Models/
│   │   ├── Services/
│   │   ├── Data/
│   │   └── Migrations/
│   └── AIDev.Web/              # React + Vite + TypeScript
│       ├── src/
│       │   ├── components/
│       │   ├── services/api.ts
│       │   └── auth/authConfig.ts
│       ├── .env                # VITE_API_URL for local
│       └── .env.production     # VITE_API_URL for Azure
└── LocalRebuildAndStart.md
```
