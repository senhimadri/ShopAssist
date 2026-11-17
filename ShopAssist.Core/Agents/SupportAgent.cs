using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ShopAssist.Core.Agents;

public class SupportAgent
{
    private readonly IChatClient _chat;
    private readonly IReadOnlyDictionary<string, AIFunction> _toolsByName;
    private readonly ChatOptions _options;

    public SupportAgent(IChatClient chat, IReadOnlyList<AIFunction> tools)
    {
        _chat = chat;
        _toolsByName = tools.ToDictionary(t => t.Name);
        _options =  new ChatOptions
        {
            Temperature = 0.3f,
            MaxOutputTokens = 400,
            Tools = tools.Cast<AITool>().ToList()
        };
    }

    /// <summary>
    /// Answers the latest user message. The history list is owned by the agent
    /// from here on: it appends tool requests, tool results and the final reply.
    /// </summary>
    /// 

    public async Task<string> AnswerMessageAsync(List<ChatMessage> history,
                                                    Action<string>? trace = null, 
                                                    CancellationToken cancellationToken = default)
    {
        var first = await _chat.GetResponseAsync(history, _options, cancellationToken);

        history.AddRange(first.Messages);


        var calls = first.Messages
                        .SelectMany(m=>m.Contents)
                        .OfType<FunctionCallContent>()
                        .ToList();

        if (calls.Count == 0)
            return first.Text;

        foreach (var call in calls)
        {   
            var output = await RunToolAsync(call, cancellationToken);

            trace?.Invoke($"[tool] {call.Name}({JsonSerializer.Serialize(call.Arguments)}) -> {JsonSerializer.Serialize(output)}");

            history.Add(new ChatMessage(ChatRole.Tool,
                        [new FunctionResultContent(call.CallId, output)]));
        }

        var second = await _chat.GetResponseAsync(history, _options, cancellationToken);

        history.AddRange(second.Messages);

        if (second.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().Any())
            return "(The model wants another tool step. Multi-step loops are built on Day 4.)";

        return second.Text;
    }

    private async Task<object?> RunToolAsync(FunctionCallContent call, CancellationToken ct)
    {
        if (!_toolsByName.TryGetValue(call.Name, out var tool))
            return new { error = $"Unknown tool '{call.Name}'." };

        try
        {
            return await tool.InvokeAsync(new AIFunctionArguments(call.Arguments), ct);
        }
        catch (Exception ex)
        {
            // Return the error to the model instead of crashing. It can then correct itself.
            return new { error = ex.Message };
        }
    }
}
