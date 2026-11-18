using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ShopAssist.Core.Agents;

public sealed record AgentRunResult(string Reply, int Steps, int ToolCalls, bool HitLimit);

public class SupportAgent
{
    private readonly IChatClient _chat;
    private readonly IReadOnlyDictionary<string, AIFunction> _toolsByName;
    private readonly ChatOptions _options;

    public int MaxSteps { get; set; } = 6;
    public int MaxToolCalls { get; set; } = 10;

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
    /// The agent loop: think, act, observe, repeat until the model answers in plain text
    /// or a safety limit is reached. The history list is owned by the agent from here on.
    /// </summary>
    /// 
    public async Task<AgentRunResult> RunAsync(List<ChatMessage> history,Action<string>? trace = null, CancellationToken cancellationToken = default)
    {
        int steps = 0;
        int toolCalls = 0;

        while(true)
        {
            // Think : ask the model what to do next, given everything it has seen so far.

            if (steps >= MaxSteps)
                return new AgentRunResult(Reply: "(The agent hit the maximum number of steps.)", Steps: steps, ToolCalls: toolCalls, HitLimit: true);

            steps++;

            var response = await _chat.GetResponseAsync(history, _options, cancellationToken);

            // Act : if the model wants to call a tool, do it and add the result to the history.
            history.AddRange(response.Messages);

            var calls = response.Messages
                                .SelectMany(m => m.Contents)
                                .OfType<FunctionCallContent>()
                                .ToList();

            if (calls.Count == 0)
                return new AgentRunResult(Reply: response.Text, Steps: steps, ToolCalls: toolCalls, HitLimit: false);

            foreach (var call in calls)
            {
                if (toolCalls >= MaxToolCalls)
                {
                    trace?.Invoke("[limit] tool-call budget exhausted");
                    return new AgentRunResult(Reply: "(The agent hit the maximum number of tool calls.)", Steps: steps, ToolCalls: toolCalls, HitLimit: true);
                }

                toolCalls++;

                var output = await RunToolAsync(call, cancellationToken);
                trace?.Invoke($"[step {steps}] {call.Name}({JsonSerializer.Serialize(call.Arguments)}) -> {JsonSerializer.Serialize(output)}");

                history.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(call.CallId, output)]));
            }

            // Loop again: the model now sees the results and decides what to do next.
        }
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
