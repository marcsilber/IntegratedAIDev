# AI Dev Pipeline — Sales Pack

## What Is It?

AI Dev Pipeline is an intelligent software development platform that turns bug reports and feature requests into deployed code — faster than any traditional workflow. It combines structured request intake with a pipeline of AI agents that triage, design, implement, and deploy changes in parallel, with human approval at every critical step.

**In short:** Your testers find issues. Our AI agents fix them. You stay in control.

---

## The Problem We Solve

Software teams today face a painful bottleneck: testers find bugs faster than developers can fix them. The typical cycle — discover a bug, write it up, wait for triage, wait for a developer, wait for code review, wait for deployment — takes days. Meanwhile, the backlog grows.

| Traditional Workflow | AI Dev Pipeline |
|---------------------|-----------------|
| Bug reported in chat/email/spreadsheet | Structured request via web form with context |
| Manual triage by product owner | AI agent triages against product objectives |
| One developer works one fix at a time | Multiple AI agents work parallel branches |
| Deploy after each fix (fragile) | Batched deployments to UAT (stable) |
| Hours to days per fix | Minutes to under an hour |

---

## Key Features

### Structured Request Intake
- **Web-based submission form** for bugs, features, enhancements, and questions
- **Rich context capture** — priority, steps to reproduce, expected vs actual behaviour, screenshots and file attachments
- **Multi-project support** — submit requests against any repository in your portfolio from a single interface
- **GitHub Issues sync** — every request automatically creates a linked GitHub Issue with labels and full detail

### AI Product Owner Agent
- **Automatic triage** — the AI reviews every incoming request against your product objectives and sales pack
- **Clarification requests** — when a report lacks detail, the agent asks the submitter targeted questions
- **Approval or rejection with reasoning** — transparent decisions logged on every request
- **Alignment validation** — ensures work stays on-roadmap and on-strategy

### AI Architect Agent
- **Codebase-aware solution design** — the agent reads your actual code and proposes a targeted solution
- **Impact analysis** — identifies affected files, data migrations, breaking changes, and dependency risks
- **Human review step** — architects and leads review the proposed solution before any code is written
- **Consistency at scale** — maintains architectural standards even across parallel work streams

### AI Planning & Implementation Agent
- **Automatic branch management** — creates isolated feature branches for each approved solution
- **Parallel implementation** — multiple AI coding agents work simultaneously on different branches
- **Progress monitoring** — tracks build status, test results, and completion across all active branches
- **Merge coordination** — batches completed work and resolves conflicts before merging
- **UAT deployment triggers** — deploys a cohesive set of changes to UAT rather than one-at-a-time

### Real-Time Dashboard
- **Request pipeline view** — see every request from submission through deployment
- **Status breakdowns** — by type, priority, and current stage
- **UAT release schedule** — upcoming batched deployments and their contents
- **User activity** — submission trends, resolution times, agent performance

### Enterprise-Grade Security
- **Microsoft Entra ID (Azure AD)** authentication — no passwords to manage
- **JWT Bearer token** validation on every API call
- **Role-based access** — testers, admins, and reviewers with appropriate permissions
- **Secrets in Azure Key Vault** — credentials never stored in source control

### CI/CD Built In
- **GitHub Actions** pipelines deploy automatically on every push to main
- **Azure-hosted** — App Service for the API, Static Web Apps for the frontend
- **Zero-downtime deployments** — production-ready from day one

---

## Benefits

### For Testers
- **Never blocked.** Submit as many bugs and requests as you find — the pipeline handles the queue.
- **Structured forms** mean no more "can you add more detail?" back-and-forth.
- **Real-time visibility** into where your request is in the pipeline.
- **Paste screenshots and attach files** directly into the request — no separate uploads needed.

### For Product Owners
- **80%+ of triage automated.** The AI agent handles routine alignment checks so you focus on strategic decisions.
- **Every decision is transparent.** Agent reasoning is logged as comments — audit-ready.
- **Requests validated against objectives.** Nothing slips through that contradicts the product direction.

### For Architects & Tech Leads
- **Solution proposals before code.** Review impact analysis and approach before a single line is written.
- **Architectural consistency.** The AI maintains patterns even when 5 branches are active simultaneously.
- **Less context switching.** Review structured proposals instead of ad-hoc pull requests.

### For Development Teams
- **Parallel work streams.** 3+ branches active simultaneously instead of serial one-at-a-time.
- **Faster cycle time.** Bug report to deployed fix in under an hour, not days.
- **Batch deployments.** Fewer, more stable releases instead of fragile one-fix-at-a-time pushes.
- **Focus on hard problems.** AI handles routine fixes so developers tackle complex architecture and design.

### For Management
- **Measurable throughput.** Dashboard shows requests submitted, resolved, and deployed over time.
- **Predictable delivery.** Batched UAT releases on a visible schedule.
- **Reduced cost per fix.** AI agents handle the bulk of triage, design, and implementation work.
- **Scaleable.** Add more projects and testers without proportionally adding developers.

---

## How It Works

```
  Tester submits           AI Product Owner         AI Architect
  structured request  ──▶  triages & approves  ──▶  designs solution
        │                        │                       │
        │                  Asks for clarity          Impact analysis
        │                  if needed                 & file-level plan
        │                        │                       │
        ▼                        ▼                       ▼
  Request tracked          Status updated           Human reviews
  in dashboard             on dashboard             & approves
                                                         │
                                                         ▼
                                                   AI Planning Agent
                                                   creates branch ──▶ AI implements
                                                         │                  │
                                                   Monitors progress       │
                                                         │                  │
                                                         ▼                  ▼
                                                   Merges & batches ──▶ Deploy to UAT
```

---

## Technology Stack

| Component | Technology | Why |
|-----------|-----------|-----|
| Frontend | React 19, TypeScript, Vite | Fast, modern, type-safe UI |
| Backend API | .NET 10, C#, EF Core | Enterprise-grade, high-performance |
| Authentication | Microsoft Entra ID | Corporate SSO, zero password management |
| Source Control | GitHub + Octokit | Industry standard, full API integration |
| CI/CD | GitHub Actions | Automated build, test, and deploy |
| Hosting | Azure App Service + Static Web Apps | Scaleable, reliable, enterprise-ready |
| Database | SQLite → Azure SQL | Simple start, enterprise upgrade path |

---

## Deployment Options

| Option | Description |
|--------|-------------|
| **Azure Cloud** | Fully hosted on Azure App Service + Static Web Apps. CI/CD included. |
| **On-Premises** | Self-hosted .NET API + React static files behind your corporate firewall. |
| **Hybrid** | API on-prem with frontend on Azure Static Web Apps, or vice versa. |

---

## Pricing Model

| Tier | Includes |
|------|----------|
| **Starter** | Request intake + GitHub sync + dashboard. Up to 5 users, 1 project. |
| **Professional** | + AI Product Owner Agent + AI Architect Agent. Up to 25 users, 5 projects. |
| **Enterprise** | + AI Planning Agent + parallel implementation + batch deploy. Unlimited users and projects. |

*Contact us for custom pricing and enterprise licensing.*

---

## Getting Started

1. **Sign in** with your Microsoft Entra ID account
2. **Connect** your GitHub repositories
3. **Submit** your first request via the web form
4. **Watch** the AI pipeline triage, design, and implement your change
5. **Review and approve** at each human checkpoint
6. **Deploy** a batched release to UAT with one click

---

## Summary

AI Dev Pipeline eliminates the biggest bottleneck in software delivery: the gap between finding an issue and shipping the fix. By combining structured intake, AI-driven triage and design, parallel implementation, and batched deployment — all with human oversight — teams ship faster, ship more reliably, and never let their backlog grow out of control.

**Find it. Fix it. Ship it. Faster.**
