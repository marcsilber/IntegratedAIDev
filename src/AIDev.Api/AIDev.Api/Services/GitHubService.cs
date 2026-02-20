using AIDev.Api.Models;
using Octokit;

namespace AIDev.Api.Services;

public interface IGitHubService
{
    Task<(int issueNumber, string issueUrl)> CreateIssueAsync(DevRequest request, string owner, string repo);
    Task UpdateIssueAsync(DevRequest request, string owner, string repo);
    Task<List<Octokit.Repository>> GetRepositoriesAsync();
}

public class GitHubService : IGitHubService
{
    private readonly GitHubClient _client;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IConfiguration configuration, ILogger<GitHubService> logger)
    {
        _logger = logger;

        var token = configuration["GitHub:PersonalAccessToken"]
            ?? throw new InvalidOperationException("GitHub:PersonalAccessToken is not configured.");

        _client = new GitHubClient(new ProductHeaderValue("AIDev-Pipeline"))
        {
            Credentials = new Credentials(token)
        };
    }

    public async Task<List<Octokit.Repository>> GetRepositoriesAsync()
    {
        try
        {
            var repos = await _client.Repository.GetAllForCurrent(new RepositoryRequest
            {
                Sort = RepositorySort.Updated,
                Direction = SortDirection.Descending
            });
            return repos.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch GitHub repositories");
            throw;
        }
    }

    public async Task<(int issueNumber, string issueUrl)> CreateIssueAsync(DevRequest request, string owner, string repo)
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
            var issue = await _client.Issue.Create(owner, repo, newIssue);
            _logger.LogInformation("Created GitHub Issue #{IssueNumber} for request {RequestId}", issue.Number, request.Id);
            return (issue.Number, issue.HtmlUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create GitHub Issue for request {RequestId}", request.Id);
            throw;
        }
    }

    public async Task UpdateIssueAsync(DevRequest request, string owner, string repo)
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
            await _client.Issue.Update(owner, repo, request.GitHubIssueNumber.Value, issueUpdate);
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

    public Task<(int issueNumber, string issueUrl)> CreateIssueAsync(DevRequest request, string owner, string repo)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping Issue creation for request {RequestId}.", request.Id);
        return Task.FromResult((0, string.Empty));
    }

    public Task UpdateIssueAsync(DevRequest request, string owner, string repo)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping Issue update for request {RequestId}.", request.Id);
        return Task.CompletedTask;
    }

    public Task<List<Octokit.Repository>> GetRepositoriesAsync()
    {
        _logger.LogWarning("GitHub integration is not configured. Returning empty repo list.");
        return Task.FromResult(new List<Octokit.Repository>());
    }
}
