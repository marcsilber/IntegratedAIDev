using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AIDev.Api.Models;
using Octokit;

namespace AIDev.Api.Services;

public interface IGitHubService
{
    Task<(int issueNumber, string issueUrl)> CreateIssueAsync(DevRequest request, string owner, string repo);
    Task UpdateIssueAsync(DevRequest request, string owner, string repo);
    Task<List<Octokit.Repository>> GetRepositoriesAsync();
    Task AddAgentLabelsAsync(string owner, string repo, int issueNumber, AgentDecision decision);
    Task AddLabelAsync(string owner, string repo, int issueNumber, string label, string color);
    Task PostAgentCommentAsync(string owner, string repo, int issueNumber, string commentBody);
    Task<Octokit.TreeResponse> GetTreeRecursiveAsync(string owner, string repo, string branch = "main");
    Task<string?> GetFileContentAsync(string owner, string repo, string path, string branch = "main");
    Task AssignCopilotAgentAsync(string owner, string repo, int issueNumber, string customInstructions, string baseBranch = "main", string model = "");
    Task<PullRequest?> FindPrByIssueAndAuthorAsync(string owner, string repo, int issueNumber, string author = "Copilot");
    Task<PullRequest?> GetPullRequestAsync(string owner, string repo, int prNumber);
    Task RemoveLabelAsync(string owner, string repo, int issueNumber, string label);
    /// <summary>Delete a branch (typically after PR merge).</summary>
    Task<bool> DeleteBranchAsync(string owner, string repo, string branchRef);
    /// <summary>Find the most recent workflow run triggered after a given time.</summary>
    Task<(long runId, string status, string conclusion)?> GetLatestWorkflowRunAsync(string owner, string repo, string branch, DateTime afterUtc);
    /// <summary>Get the combined diff of a pull request.</summary>
    Task<string?> GetPrDiffAsync(string owner, string repo, int prNumber);
    /// <summary>Submit an approving review on a pull request.</summary>
    Task<bool> ApprovePullRequestAsync(string owner, string repo, int prNumber, string body);
    /// <summary>Merge a pull request using squash strategy.</summary>
    Task<bool> MergePullRequestAsync(string owner, string repo, int prNumber, string commitMessage);
    /// <summary>Post a review requesting changes on a pull request.</summary>
    Task<bool> RequestChangesOnPullRequestAsync(string owner, string repo, int prNumber, string body);
    /// <summary>Mark a draft PR as ready for review.</summary>
    Task<bool> MarkPrReadyForReviewAsync(string owner, string repo, int prNumber);
    /// <summary>Update a PR branch by merging the base branch into it. Returns true if successful, false if conflicts exist.</summary>
    Task<bool> UpdatePrBranchAsync(string owner, string repo, int prNumber);
    /// <summary>Check how many commits the PR branch is behind the base branch.</summary>
    Task<int> GetBehindByCountAsync(string owner, string repo, string baseBranch, string headBranch);
}

public class GitHubService : IGitHubService
{
    private readonly GitHubClient _client;
    private readonly string _token;
    private readonly ILogger<GitHubService> _logger;

    public GitHubService(IConfiguration configuration, ILogger<GitHubService> logger)
    {
        _logger = logger;

        _token = configuration["GitHub:PersonalAccessToken"]
            ?? throw new InvalidOperationException("GitHub:PersonalAccessToken is not configured.");

        _client = new GitHubClient(new Octokit.ProductHeaderValue("AIDev-Pipeline"))
        {
            Credentials = new Credentials(_token)
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

    public async Task AddAgentLabelsAsync(string owner, string repo, int issueNumber, AgentDecision decision)
    {
        var label = decision switch
        {
            AgentDecision.Approve => "agent:approved",
            AgentDecision.Reject => "agent:rejected",
            AgentDecision.Clarify => "agent:needs-info",
            _ => "agent:reviewed"
        };

        try
        {
            // Ensure the label exists (create if not)
            try
            {
                await _client.Issue.Labels.Get(owner, repo, label);
            }
            catch (NotFoundException)
            {
                var color = decision switch
                {
                    AgentDecision.Approve => "10b981",
                    AgentDecision.Reject => "ef4444",
                    AgentDecision.Clarify => "f59e0b",
                    _ => "6366f1"
                };
                await _client.Issue.Labels.Create(owner, repo, new NewLabel(label, color));
            }

            // Remove any existing agent: labels first
            var existingLabels = await _client.Issue.Labels.GetAllForIssue(owner, repo, issueNumber);
            foreach (var existing in existingLabels.Where(l => l.Name.StartsWith("agent:")))
            {
                try { await _client.Issue.Labels.RemoveFromIssue(owner, repo, issueNumber, existing.Name); }
                catch { /* ignore if already removed */ }
            }

            await _client.Issue.Labels.AddToIssue(owner, repo, issueNumber, new[] { label });
            _logger.LogInformation("Added label '{Label}' to GitHub Issue #{IssueNumber}", label, issueNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add agent label to GitHub Issue #{IssueNumber}", issueNumber);
        }
    }

    public async Task PostAgentCommentAsync(string owner, string repo, int issueNumber, string commentBody)
    {
        try
        {
            await _client.Issue.Comment.Create(owner, repo, issueNumber, commentBody);
            _logger.LogInformation("Posted agent comment to GitHub Issue #{IssueNumber}", issueNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to post agent comment to GitHub Issue #{IssueNumber}", issueNumber);
        }
    }

    public async Task AddLabelAsync(string owner, string repo, int issueNumber, string label, string color)
    {
        try
        {
            try { await _client.Issue.Labels.Get(owner, repo, label); }
            catch (NotFoundException)
            {
                await _client.Issue.Labels.Create(owner, repo, new NewLabel(label, color));
            }
            await _client.Issue.Labels.AddToIssue(owner, repo, issueNumber, new[] { label });
            _logger.LogInformation("Added label '{Label}' to GitHub Issue #{IssueNumber}", label, issueNumber);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add label '{Label}' to GitHub Issue #{IssueNumber}", label, issueNumber);
        }
    }

    public async Task<TreeResponse> GetTreeRecursiveAsync(string owner, string repo, string branch = "main")
    {
        try
        {
            var tree = await _client.Git.Tree.GetRecursive(owner, repo, branch);
            _logger.LogInformation("Fetched repository tree for {Owner}/{Repo} ({Count} items)", owner, repo, tree.Tree.Count);
            return tree;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get repository tree for {Owner}/{Repo}", owner, repo);
            throw;
        }
    }

    public async Task<string?> GetFileContentAsync(string owner, string repo, string path, string branch = "main")
    {
        try
        {
            var contents = await _client.Repository.Content.GetAllContentsByRef(owner, repo, path, branch);
            if (contents.Count > 0 && contents[0].Type == ContentType.File)
            {
                return contents[0].Content;
            }
            return null;
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("File not found: {Owner}/{Repo}/{Path}", owner, repo, path);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get file content for {Owner}/{Repo}/{Path}", owner, repo, path);
            return null;
        }
    }

    public async Task AssignCopilotAgentAsync(string owner, string repo, int issueNumber, string customInstructions, string baseBranch = "main", string model = "")
    {
        try
        {
            // The Copilot coding agent assignment uses the REST API directly
            // because Octokit.net doesn't natively support agent_assignment
            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            httpClient.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
            httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("AIDev-Pipeline");

            var payload = new
            {
                assignees = new[] { "copilot-swe-agent[bot]" },
                agent_assignment = new
                {
                    target_repo = $"{owner}/{repo}",
                    base_branch = baseBranch,
                    custom_instructions = customInstructions,
                    custom_agent = "",
                    model = model
                }
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await httpClient.PostAsync(
                $"https://api.github.com/repos/{owner}/{repo}/issues/{issueNumber}/assignees",
                content);

            response.EnsureSuccessStatusCode();

            _logger.LogInformation(
                "Assigned copilot-swe-agent[bot] to Issue #{IssueNumber} in {Owner}/{Repo}",
                issueNumber, owner, repo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to assign Copilot agent to Issue #{IssueNumber} in {Owner}/{Repo}",
                issueNumber, owner, repo);
            throw;
        }
    }

    public async Task<PullRequest?> FindPrByIssueAndAuthorAsync(string owner, string repo, int issueNumber, string author = "Copilot")
    {
        try
        {
            var prs = await _client.PullRequest.GetAllForRepository(owner, repo,
                new PullRequestRequest { State = ItemStateFilter.Open });

            _logger.LogInformation("FindPR: Found {Count} open PRs in {Owner}/{Repo}. Looking for issue #{IssueNumber} by author '{Author}'",
                prs.Count, owner, repo, issueNumber, author);

            // Primary match: author + issue reference in body/title
            var match = prs.FirstOrDefault(pr =>
                pr.User.Login.Equals(author, StringComparison.OrdinalIgnoreCase)
                && (pr.Body?.Contains($"#{issueNumber}") == true
                    || pr.Title?.Contains($"#{issueNumber}") == true));

            // Fallback: match by copilot branch prefix + issue reference (author name may change)
            if (match == null)
            {
                match = prs.FirstOrDefault(pr =>
                    (pr.Head?.Ref?.StartsWith("copilot/", StringComparison.OrdinalIgnoreCase) == true)
                    && (pr.Body?.Contains($"#{issueNumber}") == true
                        || pr.Title?.Contains($"#{issueNumber}") == true));

                if (match != null)
                {
                    _logger.LogInformation("FindPR: Matched PR #{PrNumber} via copilot branch fallback (author='{ActualAuthor}', branch='{Branch}')",
                        match.Number, match.User.Login, match.Head?.Ref);
                }
            }

            if (match != null)
            {
                _logger.LogInformation("Found PR #{PrNumber} by {Author} for Issue #{IssueNumber}",
                    match.Number, match.User.Login, issueNumber);
            }
            else
            {
                // Log available PRs for debugging
                foreach (var pr in prs)
                {
                    _logger.LogDebug("FindPR: Available PR #{PrNumber} by '{Author}' branch='{Branch}' title='{Title}'",
                        pr.Number, pr.User.Login, pr.Head?.Ref, pr.Title);
                }
            }

            return match;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find PR for Issue #{IssueNumber} by {Author}", issueNumber, author);
            return null;
        }
    }

    public async Task<PullRequest?> GetPullRequestAsync(string owner, string repo, int prNumber)
    {
        try
        {
            var pr = await _client.PullRequest.Get(owner, repo, prNumber);
            return pr;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
            return null;
        }
    }

    public async Task RemoveLabelAsync(string owner, string repo, int issueNumber, string label)
    {
        try
        {
            await _client.Issue.Labels.RemoveFromIssue(owner, repo, issueNumber, label);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to remove label '{Label}' from Issue #{IssueNumber}", label, issueNumber);
        }
    }

    public async Task<bool> DeleteBranchAsync(string owner, string repo, string branchRef)
    {
        try
        {
            // GitHub API: DELETE /repos/{owner}/{repo}/git/refs/heads/{branch}
            await _client.Git.Reference.Delete(owner, repo, $"heads/{branchRef}");
            _logger.LogInformation("Deleted branch '{Branch}' in {Owner}/{Repo}", branchRef, owner, repo);
            return true;
        }
        catch (NotFoundException)
        {
            _logger.LogWarning("Branch '{Branch}' not found in {Owner}/{Repo} — may already be deleted", branchRef, owner, repo);
            return true; // Already gone, treat as success
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete branch '{Branch}' in {Owner}/{Repo}", branchRef, owner, repo);
            return false;
        }
    }

    public async Task<(long runId, string status, string conclusion)?> GetLatestWorkflowRunAsync(
        string owner, string repo, string branch, DateTime afterUtc)
    {
        try
        {
            var runs = await _client.Actions.Workflows.Runs.List(owner, repo,
                new Octokit.WorkflowRunsRequest { Branch = branch });

            var recent = runs.WorkflowRuns
                .Where(r => r.CreatedAt.UtcDateTime >= afterUtc)
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefault();

            if (recent == null) return null;

            return (recent.Id, recent.Status.StringValue ?? "unknown", recent.Conclusion?.StringValue ?? "");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query workflow runs for {Owner}/{Repo} branch {Branch}", owner, repo, branch);
            return null;
        }
    }

    public async Task<string?> GetPrDiffAsync(string owner, string repo, int prNumber)
    {
        try
        {
            // Octokit doesn't have a built-in diff method, use REST API
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github.v3.diff"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AIDev-Pipeline", "1.0"));

            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}";
            var response = await http.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var diff = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("Retrieved diff for PR #{PrNumber} ({Length} chars)", prNumber, diff.Length);
            return diff;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get diff for PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
            return null;
        }
    }

    public async Task<bool> MarkPrReadyForReviewAsync(string owner, string repo, int prNumber)
    {
        try
        {
            // GitHub GraphQL API is needed to convert draft → ready (REST doesn't support this)
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AIDev-Pipeline", "1.0"));

            // First get the PR node ID
            var pr = await _client.PullRequest.Get(owner, repo, prNumber);
            var nodeId = pr.NodeId;

            var graphqlBody = JsonSerializer.Serialize(new
            {
                query = "mutation($id: ID!) { markPullRequestReadyForReview(input: { pullRequestId: $id }) { pullRequest { isDraft } } }",
                variables = new { id = nodeId }
            });

            var content = new StringContent(graphqlBody, Encoding.UTF8, "application/json");
            var response = await http.PostAsync("https://api.github.com/graphql", content);
            response.EnsureSuccessStatusCode();

            _logger.LogInformation("Marked PR #{PrNumber} as ready for review", prNumber);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark PR #{PrNumber} as ready for review", prNumber);
            return false;
        }
    }

    public async Task<bool> ApprovePullRequestAsync(string owner, string repo, int prNumber, string body)
    {
        try
        {
            var review = new PullRequestReviewCreate()
            {
                Body = body,
                Event = PullRequestReviewEvent.Approve
            };
            await _client.PullRequest.Review.Create(owner, repo, prNumber, review);
            _logger.LogInformation("Approved PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to approve PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
            return false;
        }
    }

    public async Task<bool> RequestChangesOnPullRequestAsync(string owner, string repo, int prNumber, string body)
    {
        try
        {
            var review = new PullRequestReviewCreate()
            {
                Body = body,
                Event = PullRequestReviewEvent.RequestChanges
            };
            await _client.PullRequest.Review.Create(owner, repo, prNumber, review);
            _logger.LogInformation("Requested changes on PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to request changes on PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
            return false;
        }
    }

    public async Task<int> GetBehindByCountAsync(string owner, string repo, string baseBranch, string headBranch)
    {
        try
        {
            var comparison = await _client.Repository.Commit.Compare(owner, repo, headBranch, baseBranch);
            // comparison.AheadBy = how many commits base is ahead of head = how far behind head is
            return comparison.AheadBy;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to compare branches {Base}..{Head} in {Owner}/{Repo}", baseBranch, headBranch, owner, repo);
            return -1; // Signal error
        }
    }

    public async Task<bool> UpdatePrBranchAsync(string owner, string repo, int prNumber)
    {
        try
        {
            // Use the GitHub REST API: PUT /repos/{owner}/{repo}/pulls/{pull_number}/update-branch
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("AIDev-Pipeline", "1.0"));
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var url = $"https://api.github.com/repos/{owner}/{repo}/pulls/{prNumber}/update-branch";
            var body = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await http.PutAsync(url, body);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Updated PR #{PrNumber} branch with latest base branch in {Owner}/{Repo}", prNumber, owner, repo);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Failed to update PR #{PrNumber} branch: {Status} — {Body}", prNumber, response.StatusCode, responseBody);

            // 422 typically means merge conflicts
            if ((int)response.StatusCode == 422)
            {
                _logger.LogWarning("PR #{PrNumber} has merge conflicts with base branch", prNumber);
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update PR #{PrNumber} branch in {Owner}/{Repo}", prNumber, owner, repo);
            return false;
        }
    }

    public async Task<bool> MergePullRequestAsync(string owner, string repo, int prNumber, string commitMessage)
    {
        try
        {
            var merge = new MergePullRequest
            {
                CommitTitle = commitMessage,
                MergeMethod = PullRequestMergeMethod.Squash
            };
            var result = await _client.PullRequest.Merge(owner, repo, prNumber, merge);

            if (result.Merged)
            {
                _logger.LogInformation("Merged PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
                return true;
            }
            else
            {
                _logger.LogWarning("PR #{PrNumber} merge returned non-merged state: {Message}", prNumber, result.Message);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to merge PR #{PrNumber} in {Owner}/{Repo}", prNumber, owner, repo);
            return false;
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

    public Task AddAgentLabelsAsync(string owner, string repo, int issueNumber, AgentDecision decision)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping agent labels.");
        return Task.CompletedTask;
    }

    public Task PostAgentCommentAsync(string owner, string repo, int issueNumber, string commentBody)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping agent comment.");
        return Task.CompletedTask;
    }

    public Task AddLabelAsync(string owner, string repo, int issueNumber, string label, string color)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping label.");
        return Task.CompletedTask;
    }

    public Task<Octokit.TreeResponse> GetTreeRecursiveAsync(string owner, string repo, string branch = "main")
    {
        _logger.LogWarning("GitHub integration is not configured. Returning empty tree.");
        throw new InvalidOperationException("GitHub integration is not configured.");
    }

    public Task<string?> GetFileContentAsync(string owner, string repo, string path, string branch = "main")
    {
        _logger.LogWarning("GitHub integration is not configured. Returning null file content.");
        return Task.FromResult<string?>(null);
    }

    public Task AssignCopilotAgentAsync(string owner, string repo, int issueNumber, string customInstructions, string baseBranch = "main", string model = "")
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping Copilot assignment.");
        return Task.CompletedTask;
    }

    public Task<PullRequest?> FindPrByIssueAndAuthorAsync(string owner, string repo, int issueNumber, string author = "Copilot")
    {
        _logger.LogWarning("GitHub integration is not configured. Returning null PR.");
        return Task.FromResult<PullRequest?>(null);
    }

    public Task<PullRequest?> GetPullRequestAsync(string owner, string repo, int prNumber)
    {
        _logger.LogWarning("GitHub integration is not configured. Returning null PR.");
        return Task.FromResult<PullRequest?>(null);
    }

    public Task RemoveLabelAsync(string owner, string repo, int issueNumber, string label)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping label removal.");
        return Task.CompletedTask;
    }

    public Task<bool> DeleteBranchAsync(string owner, string repo, string branchRef)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping branch deletion.");
        return Task.FromResult(false);
    }

    public Task<(long runId, string status, string conclusion)?> GetLatestWorkflowRunAsync(
        string owner, string repo, string branch, DateTime afterUtc)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping workflow run query.");
        return Task.FromResult<(long runId, string status, string conclusion)?>(null);
    }

    public Task<string?> GetPrDiffAsync(string owner, string repo, int prNumber)
    {
        _logger.LogWarning("GitHub integration is not configured. Returning null diff.");
        return Task.FromResult<string?>(null);
    }

    public Task<bool> ApprovePullRequestAsync(string owner, string repo, int prNumber, string body)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping PR approval.");
        return Task.FromResult(false);
    }

    public Task<bool> RequestChangesOnPullRequestAsync(string owner, string repo, int prNumber, string body)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping change request.");
        return Task.FromResult(false);
    }

    public Task<bool> MergePullRequestAsync(string owner, string repo, int prNumber, string commitMessage)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping PR merge.");
        return Task.FromResult(false);
    }

    public Task<bool> MarkPrReadyForReviewAsync(string owner, string repo, int prNumber)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping mark ready.");
        return Task.FromResult(false);
    }

    public Task<bool> UpdatePrBranchAsync(string owner, string repo, int prNumber)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping branch update.");
        return Task.FromResult(false);
    }

    public Task<int> GetBehindByCountAsync(string owner, string repo, string baseBranch, string headBranch)
    {
        _logger.LogWarning("GitHub integration is not configured. Skipping branch comparison.");
        return Task.FromResult(0);
    }
}
