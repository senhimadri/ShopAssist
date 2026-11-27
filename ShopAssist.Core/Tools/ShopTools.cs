using Microsoft.Extensions.AI;
using ShopAssist.Core.Memory;
using ShopAssist.Core.Store;
using System.ComponentModel;
using ShopAssist.Core.Knowledge;

namespace ShopAssist.Core.Tools;

public sealed record OrderStatusResult(bool Found,string Message,string? Status = null,string? TrackingNumber = null,string? DeliveredOn = null);
public sealed record ReturnPolicy(int WindowDays, bool MustBeUnused);
public sealed record DeliveryEstimate(string OrderNumber, string EstimatedDelivery, string Note);
public sealed record PolicySearchResult(bool Found,string Message,IReadOnlyList<PolicyCitation> Sources);
public sealed record PolicyCitation(string Id, string Document, string Section, string Text, float Score);


public sealed class ShopTools(IStoreRepository store, CustomerMemoryStore memory, string customerId, PolicyIndex policies)
{
    [Description("Look up the status, tracking number and delivery date of a customer's order by its order number.")]
    public OrderStatusResult GetOrderStatus(
        [Description("The order number, digits only, for example 1042")] string orderNumber)
    {
        var cleanedOrderNumber = new string(orderNumber.Where(char.IsDigit).ToArray());

        var order = store.FindOrder(cleanedOrderNumber);

        if (order is null)
            return new OrderStatusResult(
                Found: false,
                Message: $"No order found with number {cleanedOrderNumber}.");


        return new OrderStatusResult(
            Found: true,
            Message: $"Order {cleanedOrderNumber} is {order.Status}.",
            Status: order.Status.ToString(),
            TrackingNumber: order.TrackingNumber,
            DeliveredOn: order.DeliveredOn?.ToString("yyyy-MM-dd"));
    }


    [Description("Search the product catalog by name or keyword. Returns matching products with price and stock status.")]
    public IReadOnlyList<Product> SearchProducts(
        [Description("Words to search for, for example 'tent' or 'trekking poles'")] string query)
             => store.SearchProducts(query);

    [Description("Search the product catalog by name, keyword, or tag. Tolerates spelling mistakes, plurals, and partial words. Returns matching products with price and stock status, best matches first. Use this whenever the customer asks about a product, even if the wording is informal.")]
    public IReadOnlyList<Product> FuzzySearchProducts(
        [Description("Product words from the customer's message, in any spelling, for example 'tent', 'trekking poles', or 'hiking shelter'. Do not add words the customer did not say.")] string query)
         => store.SearchProductsByFuzzySearch(query);

    [Description("Get the shop's return policy: how many days a customer has to return an item, and whether the item must be unused. Call this for any question about returns, refunds, or exchanges.")]
    public ReturnPolicy GetReturnPolicy()
    {
        Console.WriteLine("[tool] GetReturnPolicy called"); 
        return new ReturnPolicy(WindowDays: 30, MustBeUnused: true);
    }

    [Description("Estimate the delivery date for an order that has shipped but not yet been delivered. Returns null if the order is not in transit.")]
    public DeliveryEstimate? EstimateDelivery(
        [Description("The order number, digits only, for example 1042")] string orderNumber)
    {
        var cleanedOrderNumber = new string(orderNumber.Where(char.IsDigit).ToArray());
        var order = store.FindOrder(cleanedOrderNumber);

        if (order is null || order.Status != OrderStatus.Shipped)
            return null;
        // For demonstration purposes, we'll just say that shipped orders take 5 days to arrive.
        var estimatedDelivery = order.PlacedOn.AddDays(4);

        return new DeliveryEstimate(
            OrderNumber: cleanedOrderNumber,
            EstimatedDelivery: estimatedDelivery.ToString("yyyy-MM-dd"),
            Note: "This is an estimate. Actual delivery may vary.");
    }

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
            AIFunctionFactory.Create(GetOrderStatus),
            AIFunctionFactory.Create(SearchProducts),
            AIFunctionFactory.Create(FuzzySearchProducts),
            AIFunctionFactory.Create(EstimateDelivery),
            AIFunctionFactory.Create(GetReturnPolicy),
            AIFunctionFactory.Create(RememberCustomerFact),
            AIFunctionFactory.Create(SearchPolicies)
        ];
}