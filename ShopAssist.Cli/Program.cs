using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ShopAssist.Core;
using ShopAssist.Core.Agents;
using ShopAssist.Core.Store;
using ShopAssist.Core.Tools;


var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()   // env vars override the file, e.g. Ai__Temperature=1.5
    .Build();


IChatClient chat = ChatClientFactory.Create(config);
var classifier = new IntentClassifier(chat);

var store = new InMemoryStoreRepository();
var agent = new SupportAgent(chat, new ShopTools(store).AsAIFunctions());

const string systemPrompt = """
    ROLE: ShopAssist, support assistant for Northwind Gear (outdoor equipment).

    GOALS:
    - Answer clearly, politely, and briefly (3 sentences or fewer).
    - For order or product facts, call a tool. Never guess order details or prices.
    - If a tool returns nothing, say so and suggest the customer double-check the number.

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
        var intent = await classifier.ClassifyIntentAsync(input);
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[intent] {intent.Intent} ({intent.Confidence:P0}) order={intent.OrderNumber ?? "-"}");
        Console.ResetColor();

        var result = await agent.RunAsync(history, trace: msg =>
        {
            Console.ForegroundColor = ConsoleColor.DarkMagenta;
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