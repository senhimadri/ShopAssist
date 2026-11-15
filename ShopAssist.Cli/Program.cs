using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using ShopAssist.Core;
using System.Globalization;
using System.Text;


var config = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: false)
    .AddEnvironmentVariables()   // env vars override the file, e.g. Ai__Temperature=1.5
    .Build();


IChatClient chat = ChatClientFactory.Create(config);

var classifier = new IntentClassifier(chat);


var temperature = float.Parse(config["Ai:Temperature"] ?? "0.7", CultureInfo.InvariantCulture);

var options = new ChatOptions
{
    Temperature = temperature,
    MaxOutputTokens = 300
};

const string systemPrompt = """
            ROLE: ShopAssist, support assistant for Northwind Gear (outdoor equipment).

            GOALS:
            - Answer clearly, politely, and briefly (3 sentences or fewer).
            - Be honest about what you cannot do.

            LIMITS:
            - You cannot look up orders, process returns, or issue refunds yet.
            - Never invent order numbers, tracking details, prices, or policies.
            - If a customer needs an action you cannot take, say so and offer to connect them with a human agent.

            STYLE:
            - Warm, plain English. No jargon.
            """;

var history = new List<ChatMessage>
{
    new(ChatRole.System, systemPrompt)
};

var useMemory = true;


Console.WriteLine("Welcome to ShopAssist! Type your question and press Enter. Type 'exit' to quit.");

while (true)
{
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write("You: ");
    Console.ResetColor();

    var input = Console.ReadLine();

    if( string.IsNullOrWhiteSpace(input)) 
        continue;
    if (input == "/exit") 
        break;

    if (input=="/reset")
    {
        history.RemoveRange(1, history.Count - 1);
        Console.WriteLine("(history cleared)");
        continue;
    }
    if (input == "/memory")
    {
        useMemory = !useMemory;
        Console.WriteLine($"(memory is now {(useMemory ? "ON" : "OFF")})");
        continue;
    }

    history.Add(new ChatMessage(ChatRole.User, input));

    var toSend = useMemory
        ? history
        : new List<ChatMessage> { history[0], history[^1] };

    Console.ForegroundColor = ConsoleColor.Green;

    Console.Write("ShopAssist: ");

    var reply = new StringBuilder();

    UsageDetails? usage = null;

    try
    {
        await foreach (var update in chat.GetStreamingResponseAsync(toSend, options))
        {
            Console.Write(update.Text);
            reply.Append(update.Text);

            foreach (var u in update.Contents.OfType<UsageContent>())
                usage = u.Details;
        }

    }
    catch (Exception ex)
    {
        Console.ResetColor();
        Console.WriteLine($"\n[error] {ex.GetType().Name}: {ex.Message}");
        history.RemoveAt(history.Count - 1);   // drop the unanswered question
        continue;
    }

    Console.ResetColor();
    Console.WriteLine();

    history.Add(new ChatMessage(ChatRole.Assistant, reply.ToString()));

    try
    {
        var intent = await classifier.ClassifyIntentAsync(input);
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[intent] {intent.Intent} ({intent.Confidence:P0}) order={intent.OrderNumber ?? "-"} handoff={intent.NeedsHumanHandoff}");
        Console.WriteLine($"[summary] {intent.Summary}");
        Console.ResetColor();


    }
    catch (Exception ex )
    {
        Console.ForegroundColor = ConsoleColor.DarkYellow;
        Console.WriteLine($"[intent failed] {ex.GetType().Name}: {ex.Message}");
        Console.ResetColor();
    }

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine(usage is null
        ? $"[tokens: not reported] messages in history: {history.Count}"
        : $"[tokens in: {usage.InputTokenCount}, out: {usage.OutputTokenCount}] messages in history: {history.Count}");
    Console.ResetColor();
}