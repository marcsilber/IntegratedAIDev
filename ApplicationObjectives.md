# AI Dev Pipeline — Application Objectives

## Product Purpose

An AI-assisted iterative development platform that transforms the way software teams handle bug reports, feature requests, and enhancements. The platform replaces serial, single-agent workflows with a parallel, multi-agent pipeline — enabling testers to submit structured requests that flow through AI-driven triage, architecture, planning, and implementation with human oversight at key decision points.

## Core Objectives

### 1. Accelerate the Feedback Loop

**Goal:** Reduce the time from issue discovery to deployed fix by an order of magnitude.

- Enable multiple testers to submit structured requests simultaneously
- Automate triage, solution design, and implementation via AI agents
- Batch deployments to avoid deploy-per-fix overhead
- Provide real-time visibility into request progress for all stakeholders

### 2. Structured Request Intake

**Goal:** Replace ad-hoc, inconsistent bug reports with a standardised intake process.

- Capture all relevant context at submission: steps to reproduce, expected vs actual behaviour, priority, screenshots/attachments
- Link every request to a target project/repository automatically
- Sync requests to GitHub Issues for traceability
- Support multiple request types: Bug, Feature, Enhancement, Question

### 3. AI-Driven Triage & Quality Gate (Product Owner Agent)

**Goal:** Ensure every request aligns with product objectives before development begins.

- Automatically review requests against the product's stated objectives (this document)
- Validate alignment with the product sales pack and roadmap
- Request clarification from submitters when detail is insufficient
- Approve, reject, or defer requests with transparent reasoning
- Reduce human product-owner bottleneck on routine triage decisions

### 4. AI-Assisted Solution Design (Architect Agent)

**Goal:** Produce consistent, well-reasoned technical solutions before any code is written.

- Analyse the current codebase in the context of each approved request
- Propose solution approaches with file-level impact analysis
- Identify data migration, breaking change, and dependency risks
- Present solutions for human review and approval before implementation
- Maintain architectural consistency across parallel work streams

### 5. Parallel Implementation at Scale (Planning Agent)

**Goal:** Enable multiple requests to be developed in parallel without conflicts.

- Create isolated feature branches for each approved solution
- Assign implementation agents to branches
- Monitor progress, test results, and build status
- Coordinate merges and resolve conflicts
- Batch-deploy to UAT when a set of changes is ready

### 6. Human-in-the-Loop Oversight

**Goal:** Keep humans in control of all consequential decisions while automating the routine.

- Human approval required before implementation begins (post-architecture review)
- Testers can challenge, question, or redirect AI agent recommendations
- Dashboard provides full visibility into pipeline status and agent decisions
- All agent reasoning is transparent and logged as comments on requests/issues

### 7. Multi-Project Support

**Goal:** Serve as a central intake platform for multiple repositories and products.

- Support multiple GitHub repositories under a single platform instance
- Project selection at request submission time
- Per-project configuration (repo, labels, active status)
- Admin interface for managing project portfolio

## Non-Functional Objectives

### Security & Identity
- Microsoft Entra ID (Azure AD) for authentication — no local user/password management
- JWT Bearer token validation on all API endpoints
- Role-based access (tester, admin) derived from Entra ID claims
- Secrets managed via Azure Key Vault / User Secrets — never in source control

### Reliability & Deployability
- CI/CD via GitHub Actions — every push to main auto-deploys
- Azure-hosted (App Service for API, Static Web Apps for frontend)
- SQLite for simplicity now, upgradeable to Azure SQL for production scale
- Graceful degradation when external services (GitHub API) are unavailable

### Developer Experience
- AI pair-programming workflow: test → report → fix → deploy → retest
- Local development with hot-reload (Vite dev server + dotnet watch)
- Minimal manual steps to get from idea to deployed feature

### Observability
- Dashboard with request progress, status breakdowns, and activity feeds
- UAT release schedule and deployment tracking
- User activity and submission trends

## Success Criteria

| Metric | Target |
|--------|--------|
| Time from bug report to UAT-deployed fix | < 1 hour (currently hours-to-days) |
| Parallel work streams | 3+ simultaneous branches |
| Triage automation rate | 80%+ requests triaged without human intervention |
| Solution proposal quality | 90%+ architect proposals accepted on first review |
| Deploy frequency | Batched UAT deploys 2-3x per day instead of per-fix |
| Tester throughput | Testers never blocked waiting for fixes to submit more requests |

## Guiding Principles

1. **Automate the routine, escalate the exceptional.** AI handles triage, architecture boilerplate, branch management. Humans decide on product direction and approve solutions.
2. **Transparency over black-box.** Every AI decision is logged with reasoning. No silent rejections or unexplained status changes.
3. **Speed through parallelism.** The bottleneck should never be "waiting for the current fix to finish." Multiple agents work multiple branches simultaneously.
4. **Structure enables speed.** Well-structured request intake leads to faster, more accurate AI triage and solution design.
5. **Incremental delivery.** The platform itself is built in phases, each delivering immediate value while laying groundwork for the next.
