using SAGAPATTERN.Messaging;

namespace SAGAPATTERN.Saga;

/// <summary>Wires queues to handlers and runs the message bus.</summary>
public class SagaHost(IMessageBus bus, IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken ct)
    {
        bus.Register(Queues.Inventory, b => Run<Participants>(p => p.HandleInventoryAsync(Json.Deserialize<SagaCommand>(b))));
        bus.Register(Queues.Payment, b => Run<Participants>(p => p.HandlePaymentAsync(Json.Deserialize<SagaCommand>(b))));
        bus.Register(Queues.Shipping, b => Run<Participants>(p => p.HandleShippingAsync(Json.Deserialize<SagaCommand>(b))));
        bus.Register(Queues.Replies, b => Run<OrderSagaOrchestrator>(o => o.HandleReplyAsync(Json.Deserialize<SagaReply>(b))));
        await bus.StartAsync(ct);
    }

    public Task StopAsync(CancellationToken ct) => bus.StopAsync();

    private async Task Run<T>(Func<T, Task> action) where T : notnull
    {
        using var scope = scopes.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<T>());
    }
}
