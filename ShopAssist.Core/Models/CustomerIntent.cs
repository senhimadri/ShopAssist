namespace ShopAssist.Core.Models;

public enum IntentType
{
    OrderStatus,
    Return,
    Refund,
    ProductQuestion,
    Complaint,
    GeneralChat,
    Shipping,
    Unknown
}

public sealed record CustomerIntent(
    IntentType Intent,
    double Confidence,
    string? OrderNumber,
    bool NeedsHumanHandoff,
    string Summary
    );
