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


var temperature = float.Parse(config["Ai:Temperature"] ?? "0.7", CultureInfo.InvariantCulture);

var options = new ChatOptions
{
    Temperature = temperature,
    MaxOutputTokens = 300
};

const string systemPrompt = """
                    You are ShopAssist, a friendly customer support assistant for an online store
                    called Northwind Gear (outdoor equipment). Keep answers to 3 sentences or fewer.
                    You do not have access to order data yet. If a customer asks about an order,
                    politely say you cannot look it up yet. Never invent order details.
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

    Console.ForegroundColor = ConsoleColor.DarkGray;
    Console.WriteLine(usage is null
        ? $"[tokens: not reported] messages in history: {history.Count}"
        : $"[tokens in: {usage.InputTokenCount}, out: {usage.OutputTokenCount}] messages in history: {history.Count}");
    Console.ResetColor();
}