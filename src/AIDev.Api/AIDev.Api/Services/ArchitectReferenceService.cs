using AIDev.Api.Data;
using AIDev.Api.Models.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace AIDev.Api.Services;

/// <summary>
/// Provides structured architect reference data including database schema,
/// high-level architecture overview, and key design decisions.
/// </summary>
public interface IArchitectReferenceService
{
    /// <summary>Builds the complete architect reference data from EF Core metadata and static content.</summary>
    ArchitectReferenceDto GetReferenceData(AppDbContext db);
}

/// <summary>
/// Aggregates architectural reference information for both human and AI consumption.
/// Database schema is extracted live from the EF Core model; architecture overview
/// and design decisions are maintained as static content.
/// </summary>
public class ArchitectReferenceService : IArchitectReferenceService
{
    private readonly ILogger<ArchitectReferenceService> _logger;

    public ArchitectReferenceService(ILogger<ArchitectReferenceService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public ArchitectReferenceDto GetReferenceData(AppDbContext db)
    {
        var schema = ExtractDatabaseSchema(db);
        var architecture = BuildArchitectureOverview();
        var decisions = BuildDesignDecisions();

        _logger.LogInformation(
            "Architect reference data built: {TableCount} tables, {DecisionCount} decisions",
            schema.Count, decisions.Count);

        return new ArchitectReferenceDto
        {
            DatabaseSchema = schema,
            ArchitectureOverview = architecture,
            DesignDecisions = decisions
        };
    }

    private static List<TableSchemaDto> ExtractDatabaseSchema(AppDbContext db)
    {
        var model = db.Model;
        var tables = new List<TableSchemaDto>();

        foreach (var entityType in model.GetEntityTypes())
        {
            var tableName = entityType.GetTableName() ?? entityType.ClrType.Name;

            var columns = new List<ColumnSchemaDto>();
            foreach (var property in entityType.GetProperties())
            {
                var column = new ColumnSchemaDto
                {
                    Name = property.GetColumnName() ?? property.Name,
                    DataType = property.GetColumnType() ?? property.ClrType.Name,
                    IsNullable = property.IsNullable,
                    IsPrimaryKey = property.IsPrimaryKey(),
                    IsForeignKey = property.IsForeignKey(),
                    MaxLength = property.GetMaxLength()
                };
                columns.Add(column);
            }

            var relationships = new List<RelationshipDto>();
            foreach (var fk in entityType.GetForeignKeys())
            {
                relationships.Add(new RelationshipDto
                {
                    FromTable = tableName,
                    FromColumns = fk.Properties.Select(p => p.GetColumnName() ?? p.Name).ToList(),
                    ToTable = fk.PrincipalEntityType.GetTableName() ?? fk.PrincipalEntityType.ClrType.Name,
                    ToColumns = fk.PrincipalKey.Properties.Select(p => p.GetColumnName() ?? p.Name).ToList(),
                    DeleteBehavior = fk.DeleteBehavior.ToString()
                });
            }

            tables.Add(new TableSchemaDto
            {
                TableName = tableName,
                EntityName = entityType.ClrType.Name,
                Columns = columns,
                Relationships = relationships
            });
        }

        return tables.OrderBy(t => t.TableName).ToList();
    }

    private static ArchitectureOverviewDto BuildArchitectureOverview()
    {
        return new ArchitectureOverviewDto
        {
            SystemName = "IntegratedAIDev — AI Dev Pipeline",
            Description = "A full-stack application that automates software development workflows using AI agents. "
                + "The system manages development requests through a pipeline of AI-powered stages: "
                + "Product Owner review, Architect analysis, Copilot implementation, Code Review, and Deployment.",
            Components = new List<ComponentDto>
            {
                new()
                {
                    Name = "React Frontend (SPA)",
                    Technology = "React 19 + TypeScript + Vite 7",
                    Description = "Single-page application providing the user interface for managing requests, "
                        + "reviewing architect proposals, monitoring dashboards, and admin settings.",
                    Interactions = new List<string>
                    {
                        "Communicates with Backend API via Axios HTTP client",
                        "Authenticates via MSAL.js (Microsoft Entra ID)"
                    }
                },
                new()
                {
                    Name = "Backend API",
                    Technology = ".NET 10 Web API",
                    Description = "RESTful API handling all business logic, data persistence, and AI agent orchestration. "
                        + "Controllers delegate to services which interact with EF Core and external APIs.",
                    Interactions = new List<string>
                    {
                        "Serves React frontend via CORS-enabled endpoints",
                        "Authenticates requests via Microsoft Identity Web (Entra ID)",
                        "Persists data to SQLite via Entity Framework Core",
                        "Calls GitHub API for issue/PR management",
                        "Calls Azure OpenAI (GitHub Models) for LLM inference"
                    }
                },
                new()
                {
                    Name = "SQLite Database",
                    Technology = "SQLite via EF Core",
                    Description = "Local file-based database storing all application data including requests, "
                        + "projects, reviews, comments, and system configuration. Auto-migrated on startup.",
                    Interactions = new List<string>
                    {
                        "Accessed exclusively through AppDbContext (EF Core)"
                    }
                },
                new()
                {
                    Name = "AI Agent Services",
                    Technology = "BackgroundService (.NET Hosted Services)",
                    Description = "Background services that poll for work and invoke LLM APIs: "
                        + "ProductOwnerAgentService (triages new requests), "
                        + "ArchitectAgentService (proposes solutions), "
                        + "ImplementationTriggerService (assigns Copilot), "
                        + "CodeReviewAgentService (reviews PRs), "
                        + "PrMonitorService (tracks PR status), "
                        + "PipelineOrchestratorService (manages deployments).",
                    Interactions = new List<string>
                    {
                        "Uses IServiceScopeFactory for scoped DbContext access",
                        "Calls LLM services (Azure OpenAI via GitHub Models)",
                        "Interacts with GitHub API for issues, PRs, and deployments"
                    }
                },
                new()
                {
                    Name = "GitHub Integration",
                    Technology = "Octokit / GitHub REST API",
                    Description = "Manages GitHub issues, labels, comments, pull requests, and workflow runs. "
                        + "Used by agents for issue tracking and deployment orchestration.",
                    Interactions = new List<string>
                    {
                        "Creates/updates GitHub issues and labels",
                        "Posts agent comments on issues",
                        "Monitors and triggers GitHub Actions workflows"
                    }
                }
            },
            DataFlow = new List<string>
            {
                "1. User submits a request via the React frontend",
                "2. Request is stored in SQLite and a GitHub issue is created",
                "3. ProductOwnerAgentService triages the request (LLM review)",
                "4. ArchitectAgentService proposes a solution (LLM analysis + codebase scan)",
                "5. Human approves/rejects/revises the architect proposal",
                "6. ImplementationTriggerService assigns Copilot to implement the approved solution",
                "7. PrMonitorService tracks the Copilot PR status",
                "8. CodeReviewAgentService reviews the PR (LLM-powered code review)",
                "9. PipelineOrchestratorService manages deployment via GitHub Actions"
            }
        };
    }

    private static List<DesignDecisionDto> BuildDesignDecisions()
    {
        return new List<DesignDecisionDto>
        {
            new()
            {
                Title = "SQLite for Data Storage",
                Rationale = "SQLite provides a zero-configuration, file-based database suitable for the current "
                    + "single-instance deployment model. EF Core migrations handle schema evolution automatically.",
                Implications = "Single-writer limitation; not suitable for horizontal scaling without migration to PostgreSQL/SQL Server."
            },
            new()
            {
                Title = "Background Services for AI Agents",
                Rationale = "Using .NET BackgroundService with polling loops keeps the architecture simple and avoids "
                    + "the complexity of message queues. Each agent independently polls for work at configurable intervals.",
                Implications = "Agents share the same process; a crash in one agent could affect others. "
                    + "Token budgets and polling intervals provide rate limiting."
            },
            new()
            {
                Title = "Microsoft Entra ID (Azure AD) Authentication",
                Rationale = "Leverages existing Microsoft identity infrastructure for secure SSO. "
                    + "MSAL.js on the frontend and Microsoft.Identity.Web on the backend provide seamless integration.",
                Implications = "Requires Azure AD tenant configuration. DevOps endpoints use API key auth for automation access."
            },
            new()
            {
                Title = "Inline Styles with Dark Theme",
                Rationale = "The frontend uses inline styles with CSS variables for a consistent dark theme. "
                    + "This avoids build-time CSS tooling complexity (no CSS modules or Tailwind).",
                Implications = "Style reuse requires copying variable references; no automatic class-based style sharing."
            },
            new()
            {
                Title = "Controller → Service → EF Core Pattern",
                Rationale = "Standard layered architecture separating HTTP concerns (controllers) from business logic (services) "
                    + "and data access (EF Core). Promotes testability and maintainability.",
                Implications = "All data access goes through AppDbContext; direct SQL is only used via the DevOps query endpoint."
            },
            new()
            {
                Title = "GitHub Models Endpoint for LLM Access",
                Rationale = "Azure.AI.OpenAI SDK v2.1.0 connects to the GitHub Models endpoint, providing access to GPT-4o "
                    + "and other models with token budget tracking and configurable rate limits.",
                Implications = "Dependent on GitHub Models availability and rate limits. Token budgets enforce cost control."
            },
            new()
            {
                Title = "Staged vs Auto Deployment Modes",
                Rationale = "DeploymentMode enum allows switching between automatic PR merge+deploy and staged mode "
                    + "where PRs accumulate until a human triggers deployment from the admin panel.",
                Implications = "Staged mode requires manual intervention; Auto mode may deploy untested combinations of changes."
            }
        };
    }
}
