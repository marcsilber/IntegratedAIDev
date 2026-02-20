namespace AIDev.Api.Services;

/// <summary>
/// Loads and caches reference documents (ApplicationObjectives.md, ApplicationSalesPack.md,
/// ApplicationFeatures.md) used as context in the Product Owner Agent's system prompt.
/// </summary>
public interface IReferenceDocumentService
{
    string GetSystemPromptContext();
    void Reload();
}

public class ReferenceDocumentService : IReferenceDocumentService
{
    private readonly ILogger<ReferenceDocumentService> _logger;
    private readonly string _docsPath;
    private string? _cachedContext;
    private readonly object _lock = new();

    private static readonly string[] ReferenceFiles = 
    {
        "ApplicationObjectives.md",
        "ApplicationSalesPack.md",
        "ApplicationFeatures.md"
    };

    public ReferenceDocumentService(IConfiguration configuration, ILogger<ReferenceDocumentService> logger)
    {
        _logger = logger;
        _docsPath = configuration["ProductOwnerAgent:ReferenceDocsPath"] ?? "./";
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
        var parts = new List<string>();

        foreach (var fileName in ReferenceFiles)
        {
            var path = Path.Combine(_docsPath, fileName);
            if (File.Exists(path))
            {
                var content = File.ReadAllText(path);
                parts.Add($"=== {fileName} ===\n{content}");
                _logger.LogInformation("Loaded reference document: {FileName} ({Length} chars)", fileName, content.Length);
            }
            else
            {
                _logger.LogWarning("Reference document not found: {Path}", path);
            }
        }

        if (parts.Count == 0)
        {
            _logger.LogWarning("No reference documents found in {DocsPath}", _docsPath);
            return "No reference documents available. Use your best judgment to evaluate the request.";
        }

        return string.Join("\n\n", parts);
    }
}
