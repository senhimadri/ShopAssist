using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ShopAssist.Core;
using ShopAssist.Core.Agents;
using ShopAssist.Core.Knowledge;
using ShopAssist.Core.Memory;
using ShopAssist.Core.Store;
using ShopAssist.Core.Tools;


var config = new ConfigurationBuilder()
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .AddEnvironmentVariables()   // env vars override the file, e.g. Ai__Temperature=1.5
                    .Build();

IChatClient chat = ChatClientFactory.Create(config);

var classifier = new IntentClassifier(chat);
var extractor = new CustomerMemoryExtractor(chat);
var compactor = new ConversationCompactor(chat);

var store = new InMemoryStoreRepository();

using var memory = new CustomerMemoryStore("shopassist.db");

// Hard-coded for the course. Day 12 replaces this with real authentication.
const string customerId = "cust-001";

var embedder = ChatClientFactory.CreateEmbedder(config);

var policyIndex = new PolicyIndex(embedder);

var policyText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Knowledge", "policies.md"));

await policyIndex.BuildAsync(PolicyChunker.Chunk(policyText));

if (args.Contains("--check-gate"))
{
    foreach (var q in new[] { "Can I send my tent back?", "Do you price match?", "Is there a guarantee on the sleeping bag?" })
    {
        var hits = await policyIndex.SearchAsync(q, topK: 1);
        Console.WriteLine($"{hits[0].Score,6:F3}  {(hits[0].Score >= 0.55f ? "PASS" : "BLOCK")}  {q}");
    }
}

var tools = new ShopTools(store, memory, customerId, policyIndex).AsAIFunctions();

var agent = new SupportAgent(chat, tools);

// Load remembered facts at session start and place them in the system prompt.
var rememberedFacts = memory.Recall(customerId);

var factsText = rememberedFacts.Count == 0
    ? "No remembered facts for this customer yet."
    : string.Join("\n", rememberedFacts.Select(f => $"- {f.Key}: {f.Value}"));

var systemPrompt = $"""
    ROLE: ShopAssist, support assistant for Northwind Gear (outdoor equipment).

    GOALS:
    - Answer clearly, politely, and briefly (3 sentences or fewer).
    - For order or product facts, call a tool. Never guess order details or prices.
    - If a tool returns nothing, say so and suggest the customer double-check the number.
    - Use the remembered customer facts below when they are relevant. Do not mention them unless useful.

    REMEMBERED FACTS:
    {factsText}

    LIMITS:
    - You cannot process returns or refunds yet.
    - If a customer needs an action you cannot take, offer to connect them with a human agent.

    STYLE:
    - Warm, plain English. No jargon.
    """;

var history = new List<ChatMessage> { new(ChatRole.System, systemPrompt) };

Console.WriteLine("ShopAssist ready. Try: 'Where is order 1042?'   Commands: /reset  /exit");

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

        var intent = await classifier.ClassifyIntentAsync(input);
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[intent] {intent.Intent} ({intent.Confidence:P0}) order={intent.OrderNumber ?? "-"}");
        Console.ResetColor();

        await compactor.CompactIfNeededAsync(history);


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
        Console.WriteLine($"[steps: {result.Steps}, tool calls: {result.ToolCalls}, limit hit: {result.HitLimit}]");
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