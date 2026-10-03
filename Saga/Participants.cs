using Microsoft.EntityFrameworkCore;
using SAGAPATTERN.Data;
using SAGAPATTERN.Domain;
using SAGAPATTERN.Messaging;

namespace SAGAPATTERN.Saga;

/// <summary>Local transactions of each participant service. All handlers are idempotent.</summary>
public class Participants(SagaDbContext db, IMessageBus bus)
{
    public async Task HandleInventoryAsync(SagaCommand c)
    {
        var existing = await db.Reservations.FindAsync(c.SagaId);
        if (c.Name == Commands.ReserveInventory)
        {
            if (existing is not null) { await Reply(c, Steps.Inventory, true); return; }
            var product = await db.Products.FindAsync(c.Sku);
            if (product is null) { await Reply(c, Steps.Inventory, false, $"Unknown SKU {c.Sku}"); return; }
            if (product.Stock < c.Quantity) { await Reply(c, Steps.Inventory, false, $"Insufficient stock ({product.Stock} left)"); return; }
            product.Stock -= c.Quantity;
            db.Reservations.Add(new InventoryReservation { OrderId = c.SagaId, Sku = c.Sku, Quantity = c.Quantity });
            await db.SaveChangesAsync();
            await Reply(c, Steps.Inventory, true);
        }
        else
        {
            if (existing is { Released: false })
            {
                (await db.Products.FindAsync(existing.Sku))!.Stock += existing.Quantity;
                existing.Released = true;
                await db.SaveChangesAsync();
            }
            await Reply(c, Steps.InventoryRelease, true);
        }
    }

    public async Task HandlePaymentAsync(SagaCommand c)
    {
        var existing = await db.Payments.FindAsync(c.SagaId);
        if (c.Name == Commands.ChargePayment)
        {
            if (existing is null)
            {
                if (c.Amount > 1000) { await Reply(c, Steps.Payment, false, "Card declined (amount over 1000)"); return; }
                db.Payments.Add(new Payment { OrderId = c.SagaId, Amount = c.Amount });
                await db.SaveChangesAsync();
            }
            await Reply(c, Steps.Payment, true);
        }
        else
        {
            if (existing is { Refunded: false })
            {
                existing.Refunded = true;
                await db.SaveChangesAsync();
            }
            await Reply(c, Steps.PaymentRefund, true);
        }
    }

    public async Task HandleShippingAsync(SagaCommand c)
    {
        if (c.Address.Contains("FAIL", StringComparison.OrdinalIgnoreCase))
        {
            await Reply(c, Steps.Shipping, false, "Address not serviceable");
            return;
        }
        if (await db.Shipments.FindAsync(c.SagaId) is null)
        {
            db.Shipments.Add(new Shipment { OrderId = c.SagaId, Address = c.Address, TrackingNumber = $"TRK-{Guid.NewGuid():N}"[..16] });
            await db.SaveChangesAsync();
        }
        await Reply(c, Steps.Shipping, true);
    }

    private Task Reply(SagaCommand c, string step, bool ok, string? reason = null) =>
        bus.SendAsync(Queues.Replies, Json.Serialize(new SagaReply(c.SagaId, step, ok, reason)));
}
