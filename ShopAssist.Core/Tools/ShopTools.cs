using Microsoft.Extensions.AI;
using ShopAssist.Core.Store;
using System.ComponentModel;

namespace ShopAssist.Core.Tools;


public sealed record OrderStatusResult(
                                bool Found,
                                string Message,
                                string? Status = null,
                                string? TrackingNumber = null,
                                string? DeliveredOn = null);

public sealed record ReturnPolicy(int WindowDays, bool MustBeUnused);


public sealed class ShopTools(IStoreRepository store)
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
        Console.WriteLine("[tool] GetReturnPolicy called");   // proof the tool ran
        return new ReturnPolicy(WindowDays: 30, MustBeUnused: true);
    }


    // Convert our C# methods into AI tools the model can see and call.
    public IReadOnlyList<AIFunction> AsAIFunctions() =>
        [
            AIFunctionFactory.Create(GetOrderStatus),
            AIFunctionFactory.Create(SearchProducts),
            AIFunctionFactory.Create(FuzzySearchProducts)
        ];

}