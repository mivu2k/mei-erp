using System.Text.Json;
using MeiErp.Platform.Kernel;
using MeiErp.Platform.Persistence;

namespace MeiErp.Platform.Messaging;

public static class OutboxWriter
{
    public static OutboxMessage Add<T>(this ModuleDbContext db, string eventType, T payload,
        IClock clock, string? causedByUserId = null)
    {
        var message = new OutboxMessage
        {
            EventType = eventType,
            Payload = JsonSerializer.Serialize(payload),
            OccurredUtc = clock.UtcNow,
            CausedByUserId = causedByUserId
        };
        db.Outbox.Add(message);
        return message;
    }
}

public sealed record PendingOutboxMessage(
    string Source, long Id, string EventType, string Payload, int Attempts,
    DateTime OccurredUtc, string? CausedByUserId, string? LastError, DateTime? DeadLetteredUtc);

public interface IOutboxSource
{
    string Name { get; }
    Task<IReadOnlyList<PendingOutboxMessage>> PendingAsync(int take, CancellationToken ct = default);
    Task MarkDispatchedAsync(long id, DateTime utcNow, CancellationToken ct = default);
    Task MarkFailedAsync(long id, string error, DateTime utcNow, int maxAttempts, CancellationToken ct = default);
    Task<IReadOnlyList<PendingOutboxMessage>> DeadLettersAsync(CancellationToken ct = default);
    Task RetryAsync(long id, CancellationToken ct = default);
}

public interface IIntegrationEventConsumer
{
    string EventType { get; }
    Task<Result> HandleAsync(string payload, string? causedByUserId, CancellationToken ct = default);
}

public interface IOutboxManagementService
{
    Task<IReadOnlyList<PendingOutboxMessage>> DeadLettersAsync(CancellationToken ct = default);
    Task RetryAsync(string source, long id, CancellationToken ct = default);
}
