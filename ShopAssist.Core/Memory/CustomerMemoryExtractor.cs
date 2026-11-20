using Microsoft.Extensions.AI;
using System.Text.Json;

namespace ShopAssist.Core.Memory;


public sealed record ExtractedFact(string Key, string Value);

public sealed class CustomerMemoryExtractor(IChatClient chat)
{
    private const string SystemPrompt = """
        You extract DURABLE customer preferences and facts from a support message.
        Durable means it stays true across visits: delivery address, preferred contact method,
        preferred size, a standing request.
        NOT durable: moods, one-time questions, order numbers (these belong to orders, not memory).
        Never extract payment details, passwords, or ID numbers.

        Return ONLY a JSON array, no prose. Each item: {"Key": "...", "Value": "..."}.
        Use snake_case keys such as preferred_contact, delivery_address, shoe_size.
        Return [] if there is nothing durable.

        Examples:
        Message: "Please always email me, I never check my phone."
        [{"Key": "preferred_contact", "Value": "email"}]

        Message: "Where is order 1042?"
        []
        """;

    public async Task<IReadOnlyList<ExtractedFact>> ExtractAsync(string message, CancellationToken ct = default)
    {
        var response = await chat.GetResponseAsync(
            [new ChatMessage(ChatRole.System, SystemPrompt), new ChatMessage(ChatRole.User, message)],
            new ChatOptions { Temperature = 0f, MaxOutputTokens = 200 }, ct);

        var text = response.Text;
        var start = text.IndexOf('[');
        var end = text.LastIndexOf(']');
        if (start < 0 || end <= start) return [];

        return JsonSerializer.Deserialize<List<ExtractedFact>>(
            text[start..(end + 1)],
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? [];
    }
}
