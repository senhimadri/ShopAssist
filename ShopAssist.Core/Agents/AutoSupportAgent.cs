using Microsoft.Extensions.AI;

namespace ShopAssist.Core.Agents;

public static class AutoSupportAgent
{
    /// <summary>
    /// Wraps the chat client so that tool calls are executed automatically
    /// and the results are fed back until the model finishes. No loop code needed.
    /// </summary>
    public static IChatClient Create(IChatClient inner, IReadOnlyList<AIFunction> tools) =>
        new ChatClientBuilder(inner)
            .UseFunctionInvocation(configure: c => c.MaximumIterationsPerRequest = 6)
            .Build();


    public static ChatOptions Options(IReadOnlyList<AIFunction> tools) => new()
    {
        Temperature = 0.3f,
        MaxOutputTokens = 400,
        Tools = tools.Cast<AITool>().ToList()
    };
}
