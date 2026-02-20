namespace AIDev.Api.Services;

/// <summary>
/// Loads and caches reference documents used as context in the Product Owner Agent's system prompt.
/// Prefers compact LLM-optimized .context files when available, falling back to human-readable .md files.
/// </summary>
public interface IReferenceDocumentService
{
    string GetSystemPromptContext();
    int GetContextCharCount();
    void Reload();
}

public class ReferenceDocumentService : IReferenceDocumentService
{
    private readonly ILogger<ReferenceDocumentService> _logger;
    private readonly string _docsPath;
    private readonly int _maxContextChars;
    private string? _cachedContext;
    private readonly object _lock = new();

    // Ordered by priority: Objectives (highest) → SalesPack → Features (lowest).
    // For each logical doc, prefer the .context (LLM-optimized) over .md (human-readable).
    private static readonly (string ContextFile, string FallbackFile)[] ReferenceFiles =
    {
        ("ApplicationObjectives.context", "ApplicationObjectives.md"),
        ("ApplicationSalesPack.context",  "ApplicationSalesPack.md"),
        ("ApplicationFeatures.context",   "ApplicationFeatures.md"),
    };

    public ReferenceDocumentService(IConfiguration configuration, IWebHostEnvironment env, ILogger<ReferenceDocumentService> logger)
    {
        _logger = logger;
        var configuredPath = configuration["ProductOwnerAgent:ReferenceDocsPath"] ?? "./";
        // Resolve relative paths against the content root so they work regardless of CWD
        _docsPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(env.ContentRootPath, configuredPath);
        // Budget for reference doc context (chars). ~4 chars per token.
        // Default 20 000 chars ≈ 5 000 tokens — fits GitHub Models free tier (8K input token limit).
        // Increase to 60 000+ once paid GitHub Models usage is enabled.
        _maxContextChars = int.Parse(configuration["ProductOwnerAgent:MaxContextChars"] ?? "20000");
    }

    public string GetSystemPromptContext()
    {
        if (_cachedContext != null)
            return _cachedContext;

        lock (_lock)
        {
            if (_cachedContext != null)
                return _cachedContext;

            _cachedContext = BuildContext();
            return _cachedContext;
        }
    }

    public int GetContextCharCount() => (GetSystemPromptContext()).Length;

    public void Reload()
    {
        lock (_lock)
        {
            _cachedContext = null;
        }
        _logger.LogInformation("Reference document cache cleared — will reload on next access");
    }

    private string BuildContext()
    {
        // Load all documents, preferring .context (compact) over .md (verbose)
        var docs = new List<(string FileName, string Content)>();
        foreach (var (contextFile, fallbackFile) in ReferenceFiles)
        {
            var contextPath = Path.Combine(_docsPath, contextFile);
            var fallbackPath = Path.Combine(_docsPath, fallbackFile);

            if (File.Exists(contextPath))
            {
                var content = File.ReadAllText(contextPath);
                docs.Add((contextFile, content));
                _logger.LogInformation("Loaded LLM-optimized context: {FileName} ({Length} chars)", contextFile, content.Length);
            }
            else if (File.Exists(fallbackPath))
            {
                var content = File.ReadAllText(fallbackPath);
                docs.Add((fallbackFile, content));
                _logger.LogInformation("Loaded fallback document: {FileName} ({Length} chars) — generate .context file for better token efficiency", fallbackFile, content.Length);
            }
            else
            {
                _logger.LogWarning("Reference document not found: neither {Context} nor {Fallback}", contextPath, fallbackPath);
            }
        }

        if (docs.Count == 0)
        {
            _logger.LogWarning("No reference documents found in {DocsPath}", _docsPath);
            return "No reference documents available. Use your best judgment to evaluate the request.";
        }

        // Fit documents within the character budget.
        // Priority order matches ReferenceFiles: Objectives > SalesPack > Features.
        var totalChars = docs.Sum(d => d.Content.Length + d.FileName.Length + 10);
        if (totalChars <= _maxContextChars)
        {
            _logger.LogInformation("Reference context fits within budget ({Total} / {Budget} chars)", totalChars, _maxContextChars);
            return string.Join("\n\n", docs.Select(d => $"=== {d.FileName} ===\n{d.Content}"));
        }

        // Truncate from the last (lowest priority) document first
        _logger.LogWarning("Reference context ({Total} chars) exceeds budget ({Budget} chars) — truncating", totalChars, _maxContextChars);
        var remaining = _maxContextChars;
        var parts = new List<string>();
        foreach (var (fileName, content) in docs)
        {
            var header = $"=== {fileName} ===";
            var overhead = header.Length + 3; // newline + separators
            var available = remaining - overhead;
            if (available <= 200)
            {
                _logger.LogWarning("Skipping {FileName} — insufficient budget remaining", fileName);
                break;
            }
            if (content.Length <= available)
            {
                parts.Add($"{header}\n{content}");
                remaining -= content.Length + overhead;
            }
            else
            {
                var truncated = content[..available] + $"\n\n[... truncated — {content.Length - available} chars omitted ...]";
                parts.Add($"{header}\n{truncated}");
                _logger.LogInformation("Truncated {FileName} from {Original} to {Truncated} chars", fileName, content.Length, available);
                remaining = 0;
            }
        }

        return string.Join("\n\n", parts);
    }
}
