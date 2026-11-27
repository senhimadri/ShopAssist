using FuzzySharp;

namespace ShopAssist.Core.Store;

public interface IStoreRepository
{
    Order? FindOrder(string orderNumber);
    IReadOnlyList<Product> SearchProducts(string query);

    IReadOnlyList<Product> SearchProductsByFuzzySearch(string query);
}

public sealed class InMemoryStoreRepository : IStoreRepository
{
    public DateOnly Today { get; } = new(2026, 11, 16);
    private readonly Dictionary<string, Order> _orders = new()
    {
        ["1042"] = new Order(OrderNumber: "1042", Status: OrderStatus.Shipped, PlacedOn: new DateOnly(2026, 11, 12),
            TrackingNumber: "NW-7741-0042", DeliveredOn: new DateOnly(2026,11,16), Lines: [new OrderLine("Trailhawk 2P Tent", 1, 249m)]),
        ["2291"] = new Order(OrderNumber: "2291", Status: OrderStatus.Processing, PlacedOn: new DateOnly(2026, 11, 15),
            TrackingNumber: null, DeliveredOn: null, Lines: [new OrderLine("Frostline 20°F Sleeping Bag", 1, 180m)]),
        ["3300"] = new Order(OrderNumber: "3300", Status: OrderStatus.Delivered, PlacedOn: new DateOnly(2026, 11, 6),
            TrackingNumber: "NW-7702-1187", DeliveredOn: new DateOnly(2026, 11, 10),
            Lines: [new OrderLine("Summit Carbon Trekking Poles (pair)", 1, 89m)]),
    };
    private readonly List<Product> _products =
    [
        new(Sku: "TENT-2P", Name: "Trailhawk 2P Tent", Price: 249m, InStock: true),
        new(Sku: "POLE-CF", Name: "Summit Carbon Trekking Poles (pair)", Price: 89m, InStock: true),
        new(Sku: "STOVE-MINI", Name: "Ember Mini Camp Stove", Price: 45m, InStock: false),
        new(Sku: "BAG-20", Name: "Frostline 20°F Sleeping Bag", Price: 180m, InStock: true),
    ];

    public Order? FindOrder(string orderNumber)
    {
        var order = _orders.GetValueOrDefault(orderNumber);
        return order;
    }

    public IReadOnlyList<Product> SearchProducts(string query)
    {
        var results = _products.Where(p => p.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();
        return results;
    }

    public IReadOnlyList<Product> SearchProductsByFuzzySearch(string query)
    {
        var results = _products
            .Select(p => new { Product = p, Score = Fuzz.PartialRatio(query.ToLowerInvariant(), p.Name.ToLowerInvariant()) })
            .Where(x => x.Score >= 70) // Filter out low similarity scores
            .OrderByDescending(x => x.Score)
            .Select(x => x.Product)
            .ToList();
        return results;
    }
}