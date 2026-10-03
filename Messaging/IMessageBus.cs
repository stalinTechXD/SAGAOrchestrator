using System.Threading.Channels;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;

namespace SAGAPATTERN.Messaging;

public interface IMessageBus : IAsyncDisposable
{
    Task SendAsync(string queue, string body, CancellationToken ct = default);
    void Register(string queue, Func<string, Task> handler);
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
}

/// <summary>Azure Service Bus implementation. Queues are created automatically if missing.</summary>
public sealed class ServiceBusMessageBus(string connectionString, ILogger<ServiceBusMessageBus> log) : IMessageBus
{
    private readonly ServiceBusClient _client = new(connectionString);
    private readonly ServiceBusAdministrationClient _admin = new(connectionString);
    private readonly Dictionary<string, Func<string, Task>> _handlers = new();
    private readonly List<ServiceBusProcessor> _processors = [];

    public Task SendAsync(string queue, string body, CancellationToken ct = default)
    {
        var sender = _client.CreateSender(queue);
        return sender.SendMessageAsync(new ServiceBusMessage(body) { ContentType = "application/json" }, ct);
    }

    public void Register(string queue, Func<string, Task> handler) => _handlers[queue] = handler;

    public async Task StartAsync(CancellationToken ct)
    {
        await EnsureQueuesAsync(ct);
        foreach (var (queue, handler) in _handlers)
        {
            var p = _client.CreateProcessor(queue, new ServiceBusProcessorOptions { MaxConcurrentCalls = 4, MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(5) });
            p.ProcessMessageAsync += async a => await handler(a.Message.Body.ToString());
            p.ProcessErrorAsync += a => { log.LogError(a.Exception, "Service Bus error on {Queue}", queue); return Task.CompletedTask; };
            await p.StartProcessingAsync(ct);
            _processors.Add(p);
        }
    }

    private async Task EnsureQueuesAsync(CancellationToken ct)
    {
        foreach (var queue in Queues.All)
        {
            if (!await _admin.QueueExistsAsync(queue, ct))
            {
                await _admin.CreateQueueAsync(queue, ct);
                log.LogInformation("Created Service Bus queue {Queue}", queue);
            }
        }
    }

    public async Task StopAsync()
    {
        foreach (var p in _processors) await p.StopProcessingAsync();
    }

    public async ValueTask DisposeAsync() => await _client.DisposeAsync();
}

/// <summary>Local fallback used when no Service Bus connection string is configured.</summary>
public sealed class InMemoryMessageBus(ILogger<InMemoryMessageBus> log) : IMessageBus
{
    private readonly Dictionary<string, Channel<string>> _queues = Queues.All.ToDictionary(q => q, _ => Channel.CreateUnbounded<string>());
    private readonly Dictionary<string, Func<string, Task>> _handlers = new();
    private readonly List<Task> _loops = [];
    private CancellationTokenSource _cts = new();

    public Task SendAsync(string queue, string body, CancellationToken ct = default) =>
        _queues[queue].Writer.WriteAsync(body, ct).AsTask();

    public void Register(string queue, Func<string, Task> handler) => _handlers[queue] = handler;

    public Task StartAsync(CancellationToken ct)
    {
        foreach (var (queue, handler) in _handlers)
        {
            _loops.Add(Task.Run(async () =>
            {
                await foreach (var msg in _queues[queue].Reader.ReadAllAsync(_cts.Token))
                {
                    try { await handler(msg); }
                    catch (Exception ex) { log.LogError(ex, "Handler failed on {Queue}", queue); }
                }
            }, _cts.Token));
        }
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        await _cts.CancelAsync();
        try { await Task.WhenAll(_loops); } catch (OperationCanceledException) { }
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
