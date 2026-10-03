namespace SAGAPATTERN.Domain;

public static class OrderStatus
{
    public const string Pending = "Pending";
    public const string Completed = "Completed";
    public const string Cancelled = "Cancelled";
}

public static class SagaState
{
    public const string InventoryPending = "InventoryPending";
    public const string PaymentPending = "PaymentPending";
    public const string ShippingPending = "ShippingPending";
    public const string RefundingPayment = "RefundingPayment";
    public const string ReleasingInventory = "ReleasingInventory";
    public const string Completed = "Completed";
    public const string Compensated = "Compensated";
}

public class Order
{
    public Guid Id { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Amount { get; set; }
    public string Address { get; set; } = "";
    public string Status { get; set; } = OrderStatus.Pending;
    public string SagaState { get; set; } = Domain.SagaState.InventoryPending;
    public string? FailureReason { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

public class SagaLogEntry
{
    public int Id { get; set; }
    public Guid SagaId { get; set; }
    public string Message { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
}

public class Product
{
    public string Sku { get; set; } = "";
    public string Name { get; set; } = "";
    public int Stock { get; set; }
}

public class InventoryReservation
{
    public Guid OrderId { get; set; }
    public string Sku { get; set; } = "";
    public int Quantity { get; set; }
    public bool Released { get; set; }
}

public class Payment
{
    public Guid OrderId { get; set; }
    public decimal Amount { get; set; }
    public bool Refunded { get; set; }
}

public class Shipment
{
    public Guid OrderId { get; set; }
    public string Address { get; set; } = "";
    public string TrackingNumber { get; set; } = "";
}
