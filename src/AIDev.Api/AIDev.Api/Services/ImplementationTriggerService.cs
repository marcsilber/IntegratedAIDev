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
            .Include(r => r.Attachments)
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

        // Stage image attachments to a prep branch so the Copilot agent can access them
        var effectiveBaseBranch = BaseBranch;
        var hasAttachments = request.Attachments?.Any(a => IsImageAttachment(a)) == true;
        if (hasAttachments)
        {
            var prepBranch = $"attachments/request-{request.Id}";
            var branchSha = await _gitHubService.CreateBranchAsync(owner, repo, prepBranch, BaseBranch);
            if (branchSha != null)
            {
                var files = new Dictionary<string, byte[]>();
                foreach (var attachment in request.Attachments!.Where(a => IsImageAttachment(a)))
                {
                    var fullPath = Path.Combine(Directory.GetCurrentDirectory(), attachment.StoredPath);
                    if (File.Exists(fullPath))
                    {
                        var fileBytes = await File.ReadAllBytesAsync(fullPath, ct);
                        var repoPath = $"_temp-attachments/{request.Id}/{attachment.FileName}";
                        files[repoPath] = fileBytes;
                        _logger.LogInformation(
                            "Staging attachment '{FileName}' ({Size} bytes) for request #{Id}",
                            attachment.FileName, fileBytes.Length, request.Id);
                    }
                    else
                    {
                        _logger.LogWarning("Attachment file not found on disk: {Path}", fullPath);
                    }
                }

                if (files.Count > 0)
                {
                    var committed = await _gitHubService.CommitFilesAsync(
                        owner, repo, prepBranch, files,
                        $"Stage {files.Count} attachment(s) for request #{request.Id}");

                    if (committed)
                    {
                        effectiveBaseBranch = prepBranch;
                        instructions += BuildAttachmentInstructions(request.Attachments!, request.Id);
                        _logger.LogInformation(
                            "Staged {Count} attachment(s) on branch '{Branch}' for request #{Id}",
                            files.Count, prepBranch, request.Id);
                    }
                    else
                    {
                        // Clean up the empty prep branch on failure
                        await _gitHubService.DeleteBranchAsync(owner, repo, prepBranch);
                        _logger.LogWarning("Failed to commit attachments — falling back to {Base}", BaseBranch);
                    }
                }
                else
                {
                    // No files actually read — clean up empty branch
                    await _gitHubService.DeleteBranchAsync(owner, repo, prepBranch);
                }
            }
            else
            {
                _logger.LogWarning("Failed to create prep branch for attachments — falling back to {Base}", BaseBranch);
            }
        }

        _logger.LogInformation(
            "Triggering Copilot for request #{Id} (Issue #{Issue}) — {Summary}",
            request.Id, request.GitHubIssueNumber, approvedReview.SolutionSummary);

        // Assign the issue to copilot-swe-agent[bot]
        await _gitHubService.AssignCopilotAgentAsync(
            owner, repo, request.GitHubIssueNumber!.Value, instructions, effectiveBaseBranch, Model);

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
        sb.AppendLine("- If a `_temp-attachments/` folder exists in the repo, clean it up: move needed assets to their final location and delete the temp folder");

        return sb.ToString();
    }

    private static string BuildAttachmentInstructions(ICollection<Attachment> attachments, int requestId)
    {
        var sb = new StringBuilder();
        sb.AppendLine();
        sb.AppendLine("## Image Attachments");
        sb.AppendLine();
        sb.AppendLine($"Image files for this request have been placed in `_temp-attachments/{requestId}/` in the repository.");
        sb.AppendLine("These are the actual image assets needed for the implementation.");
        sb.AppendLine();
        sb.AppendLine("**Files available:**");
        foreach (var attachment in attachments.Where(a => IsImageAttachment(a)))
        {
            sb.AppendLine($"- `_temp-attachments/{requestId}/{attachment.FileName}` ({attachment.ContentType}, {attachment.FileSizeBytes:N0} bytes)");
        }
        sb.AppendLine();
        sb.AppendLine("**Instructions:**");
        sb.AppendLine("1. If an image should be part of the project (logo, icon, asset), **copy or move** it to the correct location (e.g., `src/AIDev.Web/public/` or `src/AIDev.Web/src/assets/`)");
        sb.AppendLine("2. Update any code references to point to the new file location");
        sb.AppendLine($"3. **Delete the `_temp-attachments/` folder** when done — it must not remain in the final PR");
        sb.AppendLine();
        return sb.ToString();
    }

    private static bool IsImageAttachment(Attachment attachment)
    {
        return attachment.ContentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
    }
}
