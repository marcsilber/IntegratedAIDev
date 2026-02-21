using System.Collections.Concurrent;
using System.Text;

namespace AIDev.Api.Services;

/// <summary>
/// Reads and caches repository content from GitHub for the Architect Agent.
/// </summary>
public interface ICodebaseService
{
    /// <summary>
    /// Get a compact text representation of the repo file tree with estimated line counts.
    /// Cached per project for 15 minutes.
    /// </summary>
    Task<string> GetRepositoryMapAsync(string owner, string repo);

    /// <summary>
    /// Read the contents of specific files from the repository.
    /// Cached by file path.
    /// </summary>
    Task<Dictionary<string, string>> GetFileContentsAsync(
        string owner, string repo, IEnumerable<string> filePaths);

    /// <summary>
    /// Invalidate all caches for a repository.
    /// </summary>
    void InvalidateCache(string owner, string repo);
}

public class CodebaseService : ICodebaseService
{
    private readonly IGitHubService _github;
    private readonly ILogger<CodebaseService> _logger;

    // Cache: key = "owner/repo", value = (map, expiry)
    private readonly ConcurrentDictionary<string, (string Map, DateTime Expiry)> _mapCache = new();
    // Cache: key = "owner/repo/path", value = (content, expiry)
    private readonly ConcurrentDictionary<string, (string Content, DateTime Expiry)> _fileCache = new();

    private static readonly TimeSpan MapCacheDuration = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan FileCacheDuration = TimeSpan.FromMinutes(30);

    // File extensions to include in the repository map
    private static readonly HashSet<string> IncludedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".json", ".csproj",
        ".md", ".css", ".html", ".yaml", ".yml", ".razor", ".cshtml"
    };

    // Directory patterns to exclude
    private static readonly string[] ExcludedPrefixes =
    {
        "bin/", "obj/", "node_modules/", ".git/", "Migrations/",
        "wwwroot/lib/", ".vs/", ".vscode/", "dist/", "build/",
        "coverage/", "TestResults/", "packages/", "deploy/"
    };

    // File patterns to exclude
    private static readonly string[] ExcludedFilePatterns =
    {
        ".db", ".lock", ".min.js", ".min.css", ".map",
        ".Designer.cs", ".g.cs", ".AssemblyInfo.cs"
    };

    private readonly SemaphoreSlim _fetchThrottle = new(5, 5);

    public CodebaseService(IGitHubService github, ILogger<CodebaseService> logger)
    {
        _github = github;
        _logger = logger;
    }

    public async Task<string> GetRepositoryMapAsync(string owner, string repo)
    {
        var cacheKey = $"{owner}/{repo}";

        if (_mapCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
        {
            _logger.LogDebug("Repository map cache hit for {Key}", cacheKey);
            return cached.Map;
        }

        _logger.LogInformation("Building repository map for {Owner}/{Repo}", owner, repo);

        var tree = await _github.GetTreeRecursiveAsync(owner, repo);

        var sb = new StringBuilder();
        sb.AppendLine("PROJECT STRUCTURE:");

        var filteredItems = tree.Tree
            .Where(item => item.Type == Octokit.TreeType.Blob)
            .Where(item => IsIncludedFile(item.Path))
            .OrderBy(item => item.Path)
            .ToList();

        // Group by directory for a clean display
        string? currentDir = null;
        foreach (var item in filteredItems)
        {
            var dir = GetDirectory(item.Path);
            if (dir != currentDir)
            {
                currentDir = dir;
                if (!string.IsNullOrEmpty(dir))
                {
                    sb.AppendLine($"  {dir}/");
                }
            }

            var fileName = Path.GetFileName(item.Path);
            var estimatedLines = EstimateLineCount(item.Size);
            var prefix = string.IsNullOrEmpty(dir) ? "  " : "    ";
            sb.AppendLine($"{prefix}{fileName} ({estimatedLines} lines)");
        }

        var map = sb.ToString();
        _mapCache[cacheKey] = (map, DateTime.UtcNow.Add(MapCacheDuration));

        _logger.LogInformation("Repository map built for {Owner}/{Repo}: {FileCount} files",
            owner, repo, filteredItems.Count);

        return map;
    }

    public async Task<Dictionary<string, string>> GetFileContentsAsync(
        string owner, string repo, IEnumerable<string> filePaths)
    {
        var results = new ConcurrentDictionary<string, string>();
        var pathList = filePaths.ToList();

        _logger.LogInformation("Fetching {Count} files from {Owner}/{Repo}", pathList.Count, owner, repo);

        var tasks = pathList.Select(async path =>
        {
            var cacheKey = $"{owner}/{repo}/{path}";

            if (_fileCache.TryGetValue(cacheKey, out var cached) && cached.Expiry > DateTime.UtcNow)
            {
                results[path] = cached.Content;
                return;
            }

            await _fetchThrottle.WaitAsync();
            try
            {
                var content = await _github.GetFileContentAsync(owner, repo, path);
                if (content != null)
                {
                    results[path] = content;
                    _fileCache[cacheKey] = (content, DateTime.UtcNow.Add(FileCacheDuration));
                }
            }
            finally
            {
                _fetchThrottle.Release();
            }
        });

        await Task.WhenAll(tasks);

        _logger.LogInformation("Fetched {Count}/{Total} files from {Owner}/{Repo}",
            results.Count, pathList.Count, owner, repo);

        return new Dictionary<string, string>(results);
    }

    public void InvalidateCache(string owner, string repo)
    {
        var prefix = $"{owner}/{repo}";
        _mapCache.TryRemove(prefix, out _);

        var keysToRemove = _fileCache.Keys.Where(k => k.StartsWith(prefix + "/")).ToList();
        foreach (var key in keysToRemove)
        {
            _fileCache.TryRemove(key, out _);
        }

        _logger.LogInformation("Cache invalidated for {Owner}/{Repo}", owner, repo);
    }

    private static bool IsIncludedFile(string path)
    {
        // Check excluded directory prefixes
        foreach (var prefix in ExcludedPrefixes)
        {
            if (path.Contains(prefix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check excluded file patterns
        foreach (var pattern in ExcludedFilePatterns)
        {
            if (path.EndsWith(pattern, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check included extensions
        var ext = Path.GetExtension(path);
        return IncludedExtensions.Contains(ext);
    }

    private static string GetDirectory(string path)
    {
        var lastSlash = path.LastIndexOf('/');
        return lastSlash > 0 ? path[..lastSlash] : string.Empty;
    }

    private static int EstimateLineCount(int sizeBytes)
    {
        // Average line is ~40 bytes (including newline)
        return Math.Max(1, sizeBytes / 40);
    }
}
