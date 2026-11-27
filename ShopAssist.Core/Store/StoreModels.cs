namespace ShopAssist.Core.Store;

public enum OrderStatus { Processing, Shipped, Delivered, Cancelled }

public sealed record OrderLine(
        string ProductName,
        int Quantity,
        decimal UnitPrice);

public sealed record Order(
        string OrderNumber,
        OrderStatus Status,
        DateOnly PlacedOn,
        string? TrackingNumber,
        DateOnly? DeliveredOn,
        IReadOnlyList<OrderLine> Lines);
public sealed record Product(string Sku, string Name, decimal Price, bool InStock);