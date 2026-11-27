using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.ComponentModel;

var builder = Host.CreateApplicationBuilder(args);

// Logs must go to stderr. stdout is the MCP wire, so any stray print breaks the protocol.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton<OrderData>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<StoreTools>();

await builder.Build().RunAsync();

// ---- Data owned by the "orders team" ----

public sealed class OrderData
{
    public Dictionary<string, Order> Orders { get; } = new()
    {
        ["1042"] = new Order("1042", "Shipped", "2026-11-12", "NW-7741-0042", null),
        ["2291"] = new Order("2291", "Processing", "2026-11-15", null, null),
        ["3300"] = new Order("3300", "Delivered", "2026-11-06", "NW-7702-1187", "2026-11-10"),
    };

    public List<Product> Products { get; } =
    [
        new("TENT-2P", "Trailhawk 2P Tent", 249m, true),
        new("POLE-CF", "Summit Carbon Trekking Poles (pair)", 89m, true),
        new("STOVE-MINI", "Ember Mini Camp Stove", 45m, false),
        new("BAG-20", "Frostline 20°F Sleeping Bag", 180m, true),
    ];
}

public sealed record Order(string OrderNumber, string Status, string PlacedOn, string? TrackingNumber, string? DeliveredOn);
public sealed record Product(string Sku, string Name, decimal Price, bool InStock);

// ---- The tools this server exposes ----

[McpServerToolType]
public sealed class StoreTools(OrderData data)
{
    [McpServerTool, Description("Look up the status, tracking number and delivery date of a customer's order by its order number.")]
    public string GetOrderStatus(
        [Description("The order number, digits only, for example 1042")] string orderNumber)
    {
        var cleaned = new string(orderNumber.Where(char.IsDigit).ToArray());

        if (!data.Orders.TryGetValue(cleaned, out var order))
            return $"No order found with number {cleaned}.";

        return $"Order {order.OrderNumber}: status {order.Status}, placed {order.PlacedOn}, " +
               $"tracking {order.TrackingNumber ?? "none yet"}, delivered {order.DeliveredOn ?? "not yet"}.";
    }

    [McpServerTool, Description("Search the product catalog by name or keyword. Returns matching products with price and stock status.")]
    public string SearchProducts(
        [Description("Words to search for, for example 'tent'")] string query)
    {
        var hits = data.Products
            .Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Name} ({p.Sku}): ${p.Price}, {(p.InStock ? "in stock" : "out of stock")}")
            .ToList();

        return hits.Count == 0 ? "No products matched." : string.Join("\n", hits);
    }
}