using Microsoft.Extensions.AI;

namespace ShopAssist.Core.Memory;

/// <summary>
/// Short-term memory: keeps the history within a size budget.
/// When the history grows too long, older turns are replaced by one summary message.
/// The most recent turns are always kept word for word.
/// </summary>
public sealed class ConversationCompactor(IChatClient chat, int maxMessages = 12, int keepRecent = 6)
{
    private const string SummaryPrefix = "Summary of earlier conversation (from memory, not verbatim): ";
    /// <summary>
    /// Compacts the history in place. Keeps the system prompt at index 0,
    /// then one summary message, then the most recent messages.
    /// </summary>
    public async Task CompactIfNeededAsync(List<ChatMessage> history, CancellationToken cancellationToken = default)
    {
        if (history.Count <= maxMessages) return;

        // Everything between the system prompt and the recent tail is "old".
        int tailStart = history.Count - keepRecent;

        var oldMessages = history.Skip(1).Take(tailStart - 1).ToList();

        if (oldMessages.Count == 0) return;

        var transcript = string.Join("\n", oldMessages
            .Where(m => m.Role == ChatRole.User || m.Role == ChatRole.Assistant)
            .Select(m => $"{m.Role}: {m.Text}"));

        var summaryPrompt = new List<ChatMessage>
        {
            new(ChatRole.System,"""
                Summarise this support conversation in at most 5 short bullet points.
                Keep order numbers, decisions, promises made, and anything the customer asked for.
                Drop greetings and small talk. Do not invent facts.
                """),
            new (ChatRole.User, transcript)
        };

        var summaryResponse = await chat.GetResponseAsync(summaryPrompt,
                            new ChatOptions { Temperature = 0f, MaxOutputTokens = 200 },
                            cancellationToken: cancellationToken);

        history.RemoveRange(1, tailStart - 1);

        history.Insert(1, new ChatMessage(ChatRole.System, SummaryPrefix + summaryResponse.Text));
    }
}
