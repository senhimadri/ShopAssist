using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ShopAssist.Core.Models;
using System.Text.Json;

namespace ShopAssist.Core;

public sealed class IntentClassifier(IChatClient chat)
{
    private const string SystemPrompt = """
        You are an intent classifier for Northwind Gear, an outdoor equipment store.
        Read the customer message and return ONLY a JSON object, no prose, no markdown.

        Allowed intent values: OrderStatus, Return, Refund, ProductQuestion,
        Complaint, GeneralChat, Unknown.

        Rules:
        - If the message is not about the store, use GeneralChat.
        - Use Unknown only when you truly cannot tell.
        - Set NeedsHumanHandoff to true if the customer is angry, threatens legal action,
          or reports an injury or safety problem.
        - OrderNumber is the digits only, or null if none is mentioned.
        - Confidence is a number from 0 to 1.

        JSON shape:
        {"Intent": "...", "Confidence": 0.0, "OrderNumber": null, "NeedsHumanHandoff": false, "Summary": "..."}

        Examples:
        Customer: "Where is my order 1042?"
        {"Intent": "OrderStatus", "Confidence": 0.95, "OrderNumber": "1042", "NeedsHumanHandoff": false, "Summary": "Customer asks for status of order 1042."}

        Customer: "This tent ripped on day one and I'm furious. I want my money back or I'm calling my lawyer."
        {"Intent": "Refund", "Confidence": 0.9, "OrderNumber": null, "NeedsHumanHandoff": true, "Summary": "Angry customer demands refund for a torn tent and threatens legal action."}
        """;

    public async Task<CustomerIntent> ClassifyIntentAsync(string message, CancellationToken cancellationToken = default)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, $"Customer: \"{message}\"")
        };

        var options = new ChatOptions
        {
            Temperature = 1.5f,
            MaxOutputTokens = 200
        };

        var response = await chat.GetResponseAsync(messages, options, cancellationToken);

        var json = ExtractJson(response.Text);

        var intent = JsonSerializer.Deserialize<CustomerIntent>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

        return intent ?? throw new InvalidOperationException("Model returned empty JSON.");

    }

    private static string ExtractJson(string text)
    {
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
            throw new FormatException($"No JSON object found in model reply: {text}");
        return text[start..(end + 1)];
    }


}
