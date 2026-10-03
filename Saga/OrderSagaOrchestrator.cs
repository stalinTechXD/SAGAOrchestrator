using Microsoft.EntityFrameworkCore;
using SAGAPATTERN.Data;
using SAGAPATTERN.Domain;
using SAGAPATTERN.Messaging;

namespace SAGAPATTERN.Saga;

/// <summary>
/// Orchestration-based saga: Inventory -> Payment -> Shipping, with reverse compensation on failure.
/// </summary>
public class OrderSagaOrchestrator(SagaDbContext db, IMessageBus bus)
{
    public async Task StartAsync(Order order)
    {
        db.Orders.Add(order);
        Log(order.Id, "Saga started. Requesting inventory reservation.");
        await db.SaveChangesAsync();
        await SendAsync(Queues.Inventory, Commands.ReserveInventory, order);
    }

    public async Task HandleReplyAsync(SagaReply r)
    {
        var order = await db.Orders.FirstOrDefaultAsync(o => o.Id == r.SagaId);
        if (order is null) return;

        switch (r.Step, r.Success)
        {
            case (Steps.Inventory, true):
                Move(order, SagaState.PaymentPending, "Inventory reserved. Charging payment.");
                await Save(order, Queues.Payment, Commands.ChargePayment);
                break;
            case (Steps.Inventory, false):
                Cancel(order, r.Reason, "Inventory reservation failed. Nothing to compensate.");
                await db.SaveChangesAsync();
                break;
            case (Steps.Payment, true):
                Move(order, SagaState.ShippingPending, "Payment charged. Creating shipment.");
                await Save(order, Queues.Shipping, Commands.CreateShipment);
                break;
            case (Steps.Payment, false):
                order.FailureReason = r.Reason;
                Move(order, SagaState.ReleasingInventory, $"Payment failed ({r.Reason}). Compensating: release inventory.");
                await Save(order, Queues.Inventory, Commands.ReleaseInventory);
                break;
            case (Steps.Shipping, true):
                order.Status = OrderStatus.Completed;
                Move(order, SagaState.Completed, "Shipment created. Saga completed.");
                await db.SaveChangesAsync();
                break;
            case (Steps.Shipping, false):
                order.FailureReason = r.Reason;
                Move(order, SagaState.RefundingPayment, $"Shipping failed ({r.Reason}). Compensating: refund payment.");
                await Save(order, Queues.Payment, Commands.RefundPayment);
                break;
            case (Steps.PaymentRefund, _):
                Move(order, SagaState.ReleasingInventory, "Payment refunded. Compensating: release inventory.");
                await Save(order, Queues.Inventory, Commands.ReleaseInventory);
                break;
            case (Steps.InventoryRelease, _):
                Cancel(order, order.FailureReason, "Inventory released. Saga compensated.");
                await db.SaveChangesAsync();
                break;
        }
    }

    private void Move(Order o, string state, string message)
    {
        o.SagaState = state;
        Log(o.Id, message);
    }

    private void Cancel(Order o, string? reason, string message)
    {
        o.Status = OrderStatus.Cancelled;
        o.FailureReason ??= reason;
        Move(o, SagaState.Compensated, message);
    }

    private async Task Save(Order o, string queue, string command)
    {
        await db.SaveChangesAsync();
        await SendAsync(queue, command, o);
    }

    private Task SendAsync(string queue, string command, Order o) =>
        bus.SendAsync(queue, Json.Serialize(new SagaCommand(o.Id, command, o.Sku, o.Quantity, o.Amount, o.Address)));

    private void Log(Guid id, string message) => db.SagaLog.Add(new SagaLogEntry { SagaId = id, Message = message });
}
