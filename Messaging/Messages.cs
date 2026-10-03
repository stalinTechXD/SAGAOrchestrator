using System.Text.Json;

namespace SAGAPATTERN.Messaging;

public static class Queues
{
    public const string Inventory = "inventory-commands";
    public const string Payment = "payment-commands";
    public const string Shipping = "shipping-commands";
    public const string Replies = "saga-replies";

    public static readonly string[] All = [Inventory, Payment, Shipping, Replies];
}

public static class Commands
{
    public const string ReserveInventory = nameof(ReserveInventory);
    public const string ReleaseInventory = nameof(ReleaseInventory);
    public const string ChargePayment = nameof(ChargePayment);
    public const string RefundPayment = nameof(RefundPayment);
    public const string CreateShipment = nameof(CreateShipment);
}

public static class Steps
{
    public const string Inventory = nameof(Inventory);
    public const string InventoryRelease = nameof(InventoryRelease);
    public const string Payment = nameof(Payment);
    public const string PaymentRefund = nameof(PaymentRefund);
    public const string Shipping = nameof(Shipping);
}

public record SagaCommand(Guid SagaId, string Name, string Sku, int Quantity, decimal Amount, string Address);

public record SagaReply(Guid SagaId, string Step, bool Success, string? Reason);

public static class Json
{
    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value);
    public static T Deserialize<T>(string body) => JsonSerializer.Deserialize<T>(body)!;
}
