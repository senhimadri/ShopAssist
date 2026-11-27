using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ShopAssist.Core.Planning;

/// <summary>One step in a plan. Dependencies name the steps that must succeed first.</summary>
public sealed record PlanStep(int Number, string Goal, IReadOnlyList<int> DependsOn);

/// <summary>The full plan the planner returns. Our code reads this, not free text.</summary>
public sealed record Plan(string Goal, IReadOnlyList<PlanStep> Steps, bool NeedsPlan);

public sealed class Planner(IChatClient chat)
{
    private const string SystemPrompt = """
        You plan how a support assistant should handle a customer request.
        The assistant has these tools: GetOrderStatus, EstimateDelivery, SearchProducts,
        SearchPolicies, RememberCustomerFact.

        Decide if the request needs several steps. A single question needs no plan.
        If it needs a plan, list the steps in order. Each step names a goal in plain words,
        and lists the step numbers it depends on (use [] if none).

        Return ONLY a JSON object, no prose:
        {"Goal": "...", "NeedsPlan": true, "Steps": [{"Number": 1, "Goal": "...", "DependsOn": []}]}

        Examples:
        Request: "Where is order 1042?"
        {"Goal": "Order status", "NeedsPlan": false, "Steps": []}

        Request: "Check order 1042, and if it's eligible for return, start one."
        {"Goal": "Return order 1042 if eligible", "NeedsPlan": true, "Steps": [
          {"Number": 1, "Goal": "Get status of order 1042", "DependsOn": []},
          {"Number": 2, "Goal": "Check the return window and conditions in the policy", "DependsOn": [1]},
          {"Number": 3, "Goal": "Decide if the order is eligible and explain the result", "DependsOn": [1, 2]}]}
        """;

    public async Task<Plan> PlanAsync(string request, CancellationToken cancellationToken = default)
    {
        var response = await chat.GetResponseAsync(
                [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, request)],
                new ChatOptions { Temperature = 0f, MaxOutputTokens = 400 }, cancellationToken);

        var text = response.Text;
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            return new Plan(request, [], NeedsPlan: false);

        var plan = JsonSerializer.Deserialize<Plan>(
           text[start..(end + 1)],
           new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        return plan ?? new Plan(request, [], NeedsPlan: false);
    }

    public async Task<Plan> ReplanAsync(string request, string failedStep, string observation, CancellationToken cancellationToken = default)
    {
        var prompt = $"""
            Original request: {request}
            A step failed: {failedStep}
            Observation: {observation}
            Make a new short plan that handles this. If the request cannot be completed, return NeedsPlan false and a Steps list with a single step that asks the customer to check the information.
            """;

        return await PlanAsync(prompt, cancellationToken);
    }
}
