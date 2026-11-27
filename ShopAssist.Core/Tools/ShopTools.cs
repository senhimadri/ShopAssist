using Microsoft.Extensions.AI;
using ShopAssist.Core.Knowledge;
using ShopAssist.Core.Memory;
using System.ComponentModel;

namespace ShopAssist.Core.Tools;

public sealed record PolicySearchResult(bool Found, string Message, IReadOnlyList<PolicyCitation> Sources);
public sealed record PolicyCitation(string Id, string Document, string Section, string Text, float Score);

public sealed class ShopTools(CustomerMemoryStore memory, string customerId, PolicyIndex policies)
{
    [Description("Save a durable customer preference or fact for future visits, such as preferred contact method or delivery address. Do not use for order questions.")]
    public string RememberCustomerFact(
        [Description("Short snake_case name, for example preferred_contact")] string key,
        [Description("The fact value, for example 'email'")] string value)
    {
        memory.Remember(customerId, key, value);
        return $"Saved: {key} = {value}";
    }

    [Description("Search the Northwind Gear returns, warranty and shipping policies. Use for any question about policy, returns, refunds, warranty, or delivery rules. Returns the most relevant policy sections with their source.")]
    public async Task<PolicySearchResult> SearchPolicies(
    [Description("The customer's question or the policy topic, in plain words")] string question)
    {
        const float MinScore = 0.55f;   // relevance gate, tuned on the Day 6 demo
        var hits = await policies.SearchAsync(question, topK: 3);
        var relevant = hits.Where(h => h.Score >= MinScore).ToList();

        if (relevant.Count == 0)
            return new PolicySearchResult(false,
                "No policy section matched this question closely enough. Say you don't know and offer to connect the customer with a human agent.",
                []);

        var citations = relevant
            .Select(h => new PolicyCitation(h.Chunk.Id, h.Chunk.Title, h.Chunk.Section, h.Chunk.Text, h.Score))
            .ToList();

        return new PolicySearchResult(true, "Relevant policy sections found. Cite the section names in your answer.", citations);
    }

    // Convert our C# methods into AI tools the model can see and call.
    public IReadOnlyList<AIFunction> AsAIFunctions() =>
        [
            AIFunctionFactory.Create(RememberCustomerFact),
            AIFunctionFactory.Create(SearchPolicies)
        ];
}