using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using OllamaSharp;
using OpenAI;

namespace ShopAssist.Core;

/// <summary>
/// Builds the IChatClient (our "connection to the LLM") from configuration.
/// Think of it like a database connection factory: the rest of the app
/// never knows which provider is behind the interface.
/// </summary>
public static class ChatClientFactory
{
    public static IChatClient Create(IConfiguration config)
    {
        var provider = config["Ai:Provider"] ?? "Ollama";

        return provider.ToLowerInvariant() switch
        {
            "ollama" => CreateOllama(config),
            "githubmodels" => CreateGitHubModels(config),
            _ => throw new InvalidOperationException(
                $"Unknown Ai:Provider '{provider}'. Use 'Ollama' or 'GitHubModels'.")
        };
    }
    private static IChatClient CreateOllama(IConfiguration config)
    {
        var endpoint = config["Ai:Ollama:Endpoint"] ?? "http://localhost:11434";
        var model = config["Ai:Ollama:Model"] ?? "llama3.2:3b";

        // OllamaApiClient implements IChatClient
        return new OllamaApiClient(new Uri(endpoint), model);
    }
    private static IChatClient CreateGitHubModels(IConfiguration config)
    {
        var token = config["GITHUB_TOKEN"]
            ?? throw new InvalidOperationException(
                "Set the GITHUB_TOKEN environment variable (token needs 'Models: read').");
        var model = config["Ai:GitHubModels:Model"] ?? "openai/gpt-4o-mini";

        // GitHub Models speaks the OpenAI protocol, so the OpenAI SDK works with a custom endpoint.
        var client = new OpenAIClient(
            new ApiKeyCredential(token),
            new OpenAIClientOptions { Endpoint = new Uri("https://models.github.ai/inference") });

        return client.GetChatClient(model).AsIChatClient();
    }
    public static IEmbeddingGenerator<string, Embedding<float>> CreateEmbedder(IConfiguration config)
    {
        var endpoint = config["Ai:Ollama:Endpoint"] ?? "http://localhost:11434";
        var model = config["Ai:Ollama:EmbeddingModel"] ?? "nomic-embed-text";
        return new OllamaApiClient(new Uri(endpoint), model);
    }
}