using System.ClientModel;
using System.Collections.Concurrent;
using OpenAI;
using OpenAI.Chat;

namespace AIDev.Api.Services;

/// <summary>
/// Shared factory for creating OpenAI ChatClient instances.
/// Supports per-agent model overrides via CreateChatClient(modelName).
/// </summary>
public interface ILlmClientFactory
{
    /// <summary>Creates a ChatClient for the default model.</summary>
    ChatClient CreateChatClient();

    /// <summary>Creates a ChatClient for a specific model (cached).</summary>
    ChatClient CreateChatClient(string modelName);

    /// <summary>The default model name from configuration.</summary>
    string ModelName { get; }
}

public class LlmClientFactory : ILlmClientFactory
{
    private readonly OpenAIClient _openAiClient;
    private readonly ConcurrentDictionary<string, ChatClient> _clients = new();

    public string ModelName { get; }

    public LlmClientFactory(IConfiguration configuration)
    {
        var endpoint = configuration["GitHubModels:Endpoint"]
            ?? "https://models.inference.ai.azure.com";
        ModelName = configuration["GitHubModels:ModelName"] ?? "gpt-4o-mini";

        var apiKey = configuration["GitHub:PersonalAccessToken"]
            ?? throw new InvalidOperationException(
                "GitHub:PersonalAccessToken is required for LLM access via GitHub Models.");

        _openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint)
            });
    }

    public ChatClient CreateChatClient() => CreateChatClient(ModelName);

    public ChatClient CreateChatClient(string modelName) =>
        _clients.GetOrAdd(modelName, name => _openAiClient.GetChatClient(name));
}
