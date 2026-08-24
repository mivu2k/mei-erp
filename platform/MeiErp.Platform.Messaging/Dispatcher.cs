using MeiErp.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace MeiErp.Platform.Messaging;

public sealed class OutboxDispatcher(
    IServiceScopeFactory scopes, IClock clock, ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int MaxAttempts = 5;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("Integration outbox dispatcher started.");
        while (!stoppingToken.IsCancellationRequested)
        {
            try { await DispatchOnceAsync(stoppingToken); }
            catch (Exception ex) { logger.LogError(ex, "Integration outbox sweep failed."); }
            await Task.Delay(TimeSpan.FromSeconds(20), stoppingToken);
        }
    }

    public async Task<int> DispatchOnceAsync(CancellationToken ct = default)
    {
        using var scope = scopes.CreateScope();
        var sources = scope.ServiceProvider.GetServices<IOutboxSource>().ToList();
        var handlers = scope.ServiceProvider.GetServices<IIntegrationEventConsumer>()
            .ToDictionary(h => h.EventType, StringComparer.OrdinalIgnoreCase);
        var delivered = 0;
        foreach (var source in sources)
        foreach (var message in await source.PendingAsync(50, ct))
        {
            try
            {
                if (!handlers.TryGetValue(message.EventType, out var handler))
                    throw new InvalidOperationException($"No handler is registered for '{message.EventType}'.");
                var result = await handler.HandleAsync(message.Payload, message.CausedByUserId, ct);
                if (result.Failed) throw new InvalidOperationException(result.Error);
                await source.MarkDispatchedAsync(message.Id, clock.UtcNow, ct);
                delivered++;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Outbox {Source}/{Id} failed.", source.Name, message.Id);
                await source.MarkFailedAsync(message.Id, ex.Message, clock.UtcNow, MaxAttempts, ct);
            }
        }
        return delivered;
    }
}

public sealed class OutboxManagementService(IEnumerable<IOutboxSource> sources) : IOutboxManagementService
{
    public async Task<IReadOnlyList<PendingOutboxMessage>> DeadLettersAsync(CancellationToken ct = default)
    {
        var rows = new List<PendingOutboxMessage>();
        foreach (var source in sources) rows.AddRange(await source.DeadLettersAsync(ct));
        return rows.OrderByDescending(r => r.DeadLetteredUtc).ToList();
    }

    public Task RetryAsync(string source, long id, CancellationToken ct = default) =>
        (sources.FirstOrDefault(s => s.Name.Equals(source, StringComparison.OrdinalIgnoreCase))
         ?? throw new InvalidOperationException("Outbox source not found.")).RetryAsync(id, ct);
}
