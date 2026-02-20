using AIDev.Api.Models;
using Octokit;

namespace AIDev.Api.Services;

public interface IGitHubService
{
    Task<(int issueNumber, string issueUrl)> CreateIssueAsync(DevRequest request);
    Task UpdateIssueAsync(DevRequest request);
}

public class GitHubService : IGitHubService
{
    private readonly GitHubClient _client;
    private readonly string _owner;
    private readonly string _repo;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IConfiguration configuration, ILogger<GitHubService> logger)
    {
        _logger = logger;

        var token = configuration["GitHub:PersonalAccessToken"]
            ?? throw new InvalidOperationException("GitHub:PersonalAccessToken is not configured.");
        _owner = configuration["GitHub:Owner"]
            ?? throw new InvalidOperationException("GitHub:Owner is not configured.");
        _repo = configuration["GitHub:Repo"]
            ?? throw new InvalidOperationException("GitHub:Repo is not configured.");

        _client = new GitHubClient(new ProductHeaderValue("AIDev-Pipeline"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<(int issueNumber, string issueUrl)> CreateIssueAsync(DevRequest request)
    {
        var body = FormatIssueBody(request);
        var newIssue = new NewIssue(request.Title)
        {
            Body = body
        };

        // Add labels
        newIssue.Labels.Add(request.RequestType.ToString().ToLower());
        newIssue.Labels.Add($"priority:{request.Priority.ToString().ToLower()}");

        try
        {
            var issue = await _client.Issue.Create(_owner, _repo, newIssue);
            _logger.LogInformation("Created GitHub Issue #{IssueNumber} for request {RequestId}", issue.Number, request.Id);
            return (issue.Number, issue.HtmlUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create GitHub Issue for request {RequestId}", request.Id);
            throw;
        }
    }

    public async Task UpdateIssueAsync(DevRequest request)
    {
        if (request.GitHubIssueNumber == null) return;

        var issueUpdate = new IssueUpdate
        {
            Title = request.Title,
            Body = FormatIssueBody(request)
        };

        // Map status to GitHub issue state
        if (request.Status == RequestStatus.Done || request.Status == RequestStatus.Rejected)
        {
            issueUpdate.State = ItemState.Closed;
        }

        try
        {
            await _client.Issue.Update(_owner, _repo, request.GitHubIssueNumber.Value, issueUpdate);
            _logger.LogInformation("Updated GitHub Issue #{IssueNumber} for request {RequestId}",
                request.GitHubIssueNumber, request.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update GitHub Issue #{IssueNumber} for request {RequestId}",
                request.GitHubIssueNumber, request.Id);
            throw;
        }
    }

    private static string FormatIssueBody(DevRequest request)
    {
        var body = $"""
            ## {request.RequestType}: {request.Title}

            **Priority:** {request.Priority}
            **Status:** {request.Status}
            **Submitted by:** {request.SubmittedBy} ({request.SubmittedByEmail})
            **Created:** {request.CreatedAt:yyyy-MM-dd HH:mm UTC}

            ---

            ### Description
            {request.Description}
            """;

        if (!string.IsNullOrWhiteSpace(request.StepsToReproduce))
        {
            body += $"""

                ### Steps to Reproduce
                {request.StepsToReproduce}
                """;
        }

        if (!string.IsNullOrWhiteSpace(request.ExpectedBehavior))
        {
            body += $"""

                ### Expected Behavior
                {request.ExpectedBehavior}
                """;
        }

        if (!string.IsNullOrWhiteSpace(request.ActualBehavior))
        {
            body += $"""

                ### Actual Behavior
                {request.ActualBehavior}
                """;
        }

        body += $"""

            ---
            *Created by AIDev Pipeline — Request #{request.Id}*
            """;

        return body;
    }
}

/// <summary>
/// A no-op GitHub service used when GitHub integration is not configured.
/// </summary>
public class NullGitHubService : IGitHubService
{
    private readonly ILogger<NullGitHubService> _logger;

    public NullGitHubService(ILogger<NullGitHubService> logger)
    {
        _logger = logger;
    }

    public Task<(int issueNumber, string issueUrl)> CreateIssueAsync(DevRequest request)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping Issue creation for request {RequestId}.", request.Id);
        return Task.FromResult((0, string.Empty));
    }

    public Task UpdateIssueAsync(DevRequest request)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping Issue update for request {RequestId}.", request.Id);
        return Task.CompletedTask;
    }
}
