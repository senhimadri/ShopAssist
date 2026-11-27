using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ShopAssist.Core;
using ShopAssist.Core.Agents;
using ShopAssist.Core.Knowledge;
using ShopAssist.Core.Mcp;
using ShopAssist.Core.Memory;
using ShopAssist.Core.Planning;
using ShopAssist.Core.Tools;

var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .AddEnvironmentVariables()
                .Build();

IChatClient chat = ChatClientFactory.Create(config);

var classifier = new IntentClassifier(chat);
var extractor = new CustomerMemoryExtractor(chat);
var compactor = new ConversationCompactor(chat);
var planner = new Planner(chat);

using var memory = new CustomerMemoryStore("shopassist.db");

// Hard-coded for the course. Day 12 replaces this with real authentication.
const string customerId = "cust-001";

// Policy knowledge: build the index once per session.
var embedder = ChatClientFactory.CreateEmbedder(config);
var policyIndex = new PolicyIndex(embedder);
var policyText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Knowledge", "policies.md"));
await policyIndex.BuildAsync(PolicyChunker.Chunk(policyText));

// Orders and products belong to the MCP server (a separate process owned by another team).
// The server project is found relative to the CLI's build output folder.
var ordersServerProject = Path.GetFullPath(
    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "ShopAssist.OrdersMcp", "ShopAssist.OrdersMcp.csproj"));
Console.WriteLine($"[mcp] starting orders server: {ordersServerProject}");

var (mcpClient, mcpTools) = await McpToolLoader.ConnectAsync(ordersServerProject);
await using var _mcp = mcpClient;

// Tools owned by ShopAssist itself: policy search and customer memory.
var localTools = new ShopTools(memory, customerId, policyIndex).AsAIFunctions();
var tools = mcpTools.Concat(localTools).ToList();

var agent = new SupportAgent(chat, tools);

// Load remembered facts at session start and place them in the system prompt.
var rememberedFacts = memory.Recall(customerId);
var factsText = rememberedFacts.Count == 0 ? "No remembered facts for this customer yet."
                : string.Join("\n", rememberedFacts.Select(f => $"- {f.Key}: {f.Value}"));

var systemPrompt = $"""
    ROLE: ShopAssist, support assistant for Northwind Gear (outdoor equipment).

    GOALS:
    - Answer clearly, politely, and briefly (4 sentences or fewer).
    - For order or product facts, call a tool. Never guess order details or prices.
    - For policy questions (returns, refunds, warranty, shipping), call SearchPolicies first.
    - Base policy answers ONLY on the results of SearchPolicies. Never answer policy questions from general knowledge.
    - When you use a policy, end the answer with the source in this form: (Source: <Document>, <Section>).
    - If SearchPolicies finds nothing relevant, say you don't have that information and offer to connect the customer with a human agent. Do not guess.
    - Use the remembered customer facts below when they are relevant.

    REMEMBERED FACTS:
    {factsText}

    LIMITS:
    - You cannot process returns or refunds yet. Explain the policy and offer to connect them with a human agent.
    - Never invent order numbers, prices, dates or policy details.
    """;

var history = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };

Console.WriteLine("ShopAssist ready. Try: 'Where is order 1042?' or 'Can I return a tent?'");
Console.WriteLine("Commands: /reset  /exit");

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("\nYou: ");
    Console.ResetColor();

    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input)) continue;
    if (input == "/exit") break;

    if (input == "/reset")
    {
        history.RemoveRange(1, history.Count - 1);
        Console.WriteLine("(history cleared)");
        continue;
    }

    // Checkpoint: if anything fails, we roll the conversation back to here.
    var checkpoint = history.Count;
    history.Add(new ChatMessage(ChatRole.User, input));

    try
    {
        // Long-term memory: pull durable facts out of what the customer just said.
        try
        {
            foreach (var fact in await extractor.ExtractAsync(input))
            {
                memory.Remember(customerId, fact.Key, fact.Value);
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine($"[remembered] {fact.Key} = {fact.Value}");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"[memory extract skipped] {ex.Message}");
            Console.ResetColor();
        }

        // Intent: a cheap separate call, shown for learning.
        var intent = await classifier.ClassifyIntentAsync(input);
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[intent] {intent.Intent} ({intent.Confidence:P0}) order={intent.OrderNumber ?? "-"}");
        Console.ResetColor();

        // Short-term memory: keep the desk small.
        await compactor.CompactIfNeededAsync(history);

        // Planning: write the steps down for multi-step requests only.
        var plan = await planner.PlanAsync(input);

        if (plan.NeedsPlan)
        {
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"[plan] {plan.Goal}");

            foreach (var s in plan.Steps)
                Console.WriteLine($"       {s.Number}. {s.Goal}");
            Console.ResetColor();

            SupportAgent.AttachPlan(history, plan);
        }

        var result = await agent.RunAsync(history, trace: msg =>
        {
            var color = msg.Contains("SearchPolicies") ? ConsoleColor.Yellow : ConsoleColor.DarkMagenta;
            Console.ForegroundColor = color;
            Console.WriteLine(msg);
            Console.ResetColor();
        });

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"ShopAssist: {result.Reply}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine($"[steps: {result.Steps}, tool calls: {result.ToolCalls}, limit hit: {result.HitLimit}, messages in history: {history.Count}]");
        Console.ResetColor();
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"[error] {ex.GetType().Name}: {ex.Message}");
        Console.ResetColor();
        history.RemoveRange(checkpoint, history.Count - checkpoint);
    }
}