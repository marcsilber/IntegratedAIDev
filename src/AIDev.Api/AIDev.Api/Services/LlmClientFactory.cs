using System.ClientModel;
using OpenAI;
using OpenAI.Chat;

namespace AIDev.Api.Services;

/// <summary>
/// Shared factory for creating OpenAI ChatClient instances.
/// Used by both the Product Owner Agent and Architect Agent.
/// </summary>
public interface ILlmClientFactory
{
    ChatClient CreateChatClient();
    string ModelName { get; }
}

public class LlmClientFactory : ILlmClientFactory
{
    private readonly ChatClient _client;

    public string ModelName { get; }

    public LlmClientFactory(IConfiguration configuration)
    {
        var endpoint = configuration["GitHubModels:Endpoint"]
            ?? "https://models.inference.ai.azure.com";
        ModelName = configuration["GitHubModels:ModelName"] ?? "gpt-4o-mini";

        var apiKey = configuration["GitHub:PersonalAccessToken"]
            ?? throw new InvalidOperationException(
                "GitHub:PersonalAccessToken is required for LLM access via GitHub Models.");

        var openAiClient = new OpenAIClient(
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(endpoint)
            });

        _client = openAiClient.GetChatClient(ModelName);
    }

    public ChatClient CreateChatClient() => _client;
}
