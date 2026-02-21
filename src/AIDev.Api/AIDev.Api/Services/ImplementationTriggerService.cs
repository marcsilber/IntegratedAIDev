using System.Text;
using System.Text.Json;
using AIDev.Api.Data;
using AIDev.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AIDev.Api.Services;

/// <summary>
/// Background service that polls for approved requests and assigns them to
/// GitHub Copilot Coding Agent for implementation.
/// </summary>
public class ImplementationTriggerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<ImplementationTriggerService> _logger;
    private readonly IConfiguration _configuration;

    private int PollingIntervalSeconds => int.Parse(_configuration["CopilotImplementation:PollingIntervalSeconds"] ?? "60");
    private bool IsEnabled => bool.Parse(_configuration["CopilotImplementation:Enabled"] ?? "true");
    private bool AutoTriggerOnApproval => bool.Parse(_configuration["CopilotImplementation:AutoTriggerOnApproval"] ?? "true");
    private int MaxConcurrentSessions => int.Parse(_configuration["CopilotImplementation:MaxConcurrentSessions"] ?? "3");
    private string BaseBranch => _configuration["CopilotImplementation:BaseBranch"] ?? "main";
    private string Model => _configuration["CopilotImplementation:Model"] ?? "";
    private int MaxRetries => int.Parse(_configuration["CopilotImplementation:MaxRetries"] ?? "2");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ImplementationTriggerService(
        IServiceScopeFactory scopeFactory,
        IGitHubService gitHubService,
        ILogger<ImplementationTriggerService> logger,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _gitHubService = gitHubService;
        _logger = logger;
        _configuration = configuration;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "ImplementationTriggerService started. Enabled={Enabled}, AutoTrigger={AutoTrigger}, Interval={Interval}s",
            IsEnabled, AutoTriggerOnApproval, PollingIntervalSeconds);

        // Give the application time to start up
        await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (IsEnabled && AutoTriggerOnApproval)
            {
                try
                {
                    await ProcessApprovedRequestsAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Error in ImplementationTriggerService polling cycle");
                }
            }

            await Task.Delay(TimeSpan.FromSeconds(PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ImplementationTriggerService stopped");
    }

    private async Task ProcessApprovedRequestsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Check how many active sessions exist
        var activeSessions = await db.DevRequests
            .CountAsync(r => r.CopilotStatus == CopilotImplementationStatus.Pending
                         || r.CopilotStatus == CopilotImplementationStatus.Working, ct);

        if (activeSessions >= MaxConcurrentSessions)
        {
            _logger.LogDebug("Max concurrent Copilot sessions ({Max}) reached — skipping", MaxConcurrentSessions);
            return;
        }

        var slotsAvailable = MaxConcurrentSessions - activeSessions;

        // Find approved requests that haven't been assigned to Copilot yet
        var candidates = await db.DevRequests
            .Include(r => r.ArchitectReviews)
            .Include(r => r.Project)
            .Where(r => r.Status == RequestStatus.Approved
                     && r.CopilotSessionId == null
                     && r.GitHubIssueNumber != null)
            .OrderBy(r => r.UpdatedAt)
            .Take(slotsAvailable)
            .ToListAsync(ct);

        if (candidates.Count == 0) return;

        _logger.LogInformation("Found {Count} approved request(s) to trigger Copilot for", candidates.Count);

        foreach (var request in candidates)
        {
            try
            {
                await TriggerCopilotCodingAgent(request, db, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to trigger Copilot for request #{Id}", request.Id);
            }
        }
    }

    /// <summary>
    /// Triggers the Copilot Coding Agent for a specific request.
    /// Can be called from the controller for manual triggers.
    /// </summary>
    public async Task TriggerCopilotCodingAgent(DevRequest request, AppDbContext db, CancellationToken ct)
    {
        var approvedReview = request.ArchitectReviews
            .Where(r => r.Decision == ArchitectDecision.Approved)
            .OrderByDescending(r => r.CreatedAt)
            .FirstOrDefault();

        if (approvedReview == null)
        {
            _logger.LogWarning("Request #{Id} has no approved architect review — skipping", request.Id);
            return;
        }

        var owner = request.Project?.GitHubOwner ?? _configuration["GitHub:Owner"] ?? "";
        var repo = request.Project?.GitHubRepo ?? _configuration["GitHub:Repo"] ?? "IntegratedAIDev";

        // Build custom instructions from the architect's approved solution
        var instructions = BuildCustomInstructions(approvedReview);

        _logger.LogInformation(
            "Triggering Copilot for request #{Id} (Issue #{Issue}) — {Summary}",
            request.Id, request.GitHubIssueNumber, approvedReview.SolutionSummary);

        // Assign the issue to copilot-swe-agent[bot]
        await _gitHubService.AssignCopilotAgentAsync(
            owner, repo, request.GitHubIssueNumber!.Value, instructions, BaseBranch, Model);

        // Update request state
        request.Status = RequestStatus.InProgress;
        request.CopilotSessionId = $"session-{request.Id}-{DateTime.UtcNow:yyyyMMddHHmmss}";
        request.CopilotStatus = CopilotImplementationStatus.Pending;
        request.CopilotTriggeredAt = DateTime.UtcNow;
        request.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        // Add label and comment to GitHub Issue
        await _gitHubService.AddLabelAsync(owner, repo, request.GitHubIssueNumber!.Value,
            "copilot:implementing", "7c3aed");

        await _gitHubService.PostAgentCommentAsync(owner, repo, request.GitHubIssueNumber!.Value,
            "🤖 **Implementation triggered.** Copilot is working on the approved solution.\n\n" +
            $"**Session:** `{request.CopilotSessionId}`\n" +
            $"**Triggered at:** {request.CopilotTriggeredAt:yyyy-MM-dd HH:mm:ss UTC}");

        _logger.LogInformation("Successfully triggered Copilot for request #{Id}", request.Id);
    }

    internal string BuildCustomInstructions(ArchitectReview review)
    {
        var sb = new StringBuilder();

        sb.AppendLine("## Approved Solution");
        sb.AppendLine();
        sb.AppendLine($"**Approach:** {review.SolutionSummary}");
        sb.AppendLine();
        sb.AppendLine(review.Approach);
        sb.AppendLine();

        // Deserialize solution JSON for structured data
        ArchitectSolutionResult? solution = null;
        try
        {
            solution = JsonSerializer.Deserialize<ArchitectSolutionResult>(review.SolutionJson, JsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize SolutionJson for review #{Id}", review.Id);
        }

        if (solution != null)
        {
            // Impacted files
            if (solution.ImpactedFiles.Count > 0)
            {
                sb.AppendLine("## Files to Modify");
                foreach (var file in solution.ImpactedFiles)
                {
                    sb.AppendLine($"- `{file.Path}` — {file.Description} ({file.Action}, ~{file.EstimatedLinesChanged} lines)");
                }
                sb.AppendLine();
            }

            // New files
            if (solution.NewFiles.Count > 0)
            {
                sb.AppendLine("## New Files to Create");
                foreach (var file in solution.NewFiles)
                {
                    sb.AppendLine($"- `{file.Path}` — {file.Description} (~{file.EstimatedLines} lines)");
                }
                sb.AppendLine();
            }

            // Data migration
            if (solution.DataMigration.Required)
            {
                sb.AppendLine("## Data Migration");
                if (!string.IsNullOrEmpty(solution.DataMigration.Description))
                    sb.AppendLine(solution.DataMigration.Description);
                foreach (var step in solution.DataMigration.Steps)
                {
                    sb.AppendLine($"- {step}");
                }
                sb.AppendLine();
            }

            // Breaking changes
            if (solution.BreakingChanges.Count > 0)
            {
                sb.AppendLine("## Breaking Changes");
                foreach (var change in solution.BreakingChanges)
                {
                    sb.AppendLine($"- ⚠️ {change}");
                }
                sb.AppendLine();
            }

            // Implementation order
            if (solution.ImplementationOrder.Count > 0)
            {
                sb.AppendLine("## Implementation Order");
                foreach (var step in solution.ImplementationOrder)
                {
                    sb.AppendLine(step);
                }
                sb.AppendLine();
            }

            // Dependency changes
            if (solution.DependencyChanges.Count > 0)
            {
                sb.AppendLine("## Dependency Changes");
                foreach (var dep in solution.DependencyChanges)
                {
                    sb.AppendLine($"- **{dep.Package}** ({dep.Action}): v{dep.Version} — {dep.Reason}");
                }
                sb.AppendLine();
            }

            // Risks and considerations
            if (solution.Risks.Count > 0)
            {
                sb.AppendLine("## Risks & Considerations");
                foreach (var risk in solution.Risks)
                {
                    sb.AppendLine($"- ⚠️ [{risk.Severity}] {risk.Description} — Mitigation: {risk.Mitigation}");
                }
                sb.AppendLine();
            }

            // Testing notes
            if (!string.IsNullOrEmpty(solution.TestingNotes))
            {
                sb.AppendLine("## Testing Requirements");
                sb.AppendLine(solution.TestingNotes);
                sb.AppendLine();
            }
        }

        sb.AppendLine("## Important");
        sb.AppendLine("- Follow existing code patterns and conventions in the repository");
        sb.AppendLine("- Run all existing tests and ensure they pass");
        sb.AppendLine("- Add tests for new functionality");
        sb.AppendLine("- Do not modify files outside the scope listed above unless absolutely necessary");
        sb.AppendLine("- Use nullable reference types");
        sb.AppendLine("- Follow existing controller/service patterns");
        sb.AppendLine("- Add XML doc comments on public members");
        sb.AppendLine("- Use record types for DTOs");

        return sb.ToString();
    }
}
