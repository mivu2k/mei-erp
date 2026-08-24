using MeiErp.Platform.Kernel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace MeiErp.Platform.Messaging.Tests;

public sealed class DispatcherTests
{
    private static readonly FixedClock Clock = new(new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Successful_delivery_marks_the_message_once()
    {
        var source = new FakeSource();
        var consumer = new FakeConsumer(Result.Success());
        var dispatcher = Build(source, consumer);
        Assert.Equal(1, await dispatcher.DispatchOnceAsync());
        Assert.True(source.Dispatched); Assert.Equal(1, consumer.Calls);
        Assert.Equal(0, await dispatcher.DispatchOnceAsync());
    }

    [Fact]
    public async Task Five_failures_dead_letter_the_message()
    {
        var source = new FakeSource();
        var dispatcher = Build(source, new FakeConsumer(Result.Fail("Account mapping missing.")));
        for (var i = 0; i < OutboxDispatcher.MaxAttempts; i++) await dispatcher.DispatchOnceAsync();
        Assert.True(source.DeadLettered); Assert.Equal(5, source.Attempts);
        Assert.Contains("mapping", source.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, await dispatcher.DispatchOnceAsync());
    }

    [Fact]
    public async Task Unknown_event_is_retained_for_review()
    {
        var source = new FakeSource(eventType: "unknown.event");
        var dispatcher = Build(source, new FakeConsumer(Result.Success()));
        await dispatcher.DispatchOnceAsync();
        Assert.False(source.Dispatched); Assert.Equal(1, source.Attempts);
        Assert.Contains("No handler", source.Error, StringComparison.Ordinal);
    }

    private static OutboxDispatcher Build(FakeSource source, FakeConsumer consumer)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOutboxSource>(source);
        services.AddSingleton<IIntegrationEventConsumer>(consumer);
        var provider = services.BuildServiceProvider();
        return new(provider.GetRequiredService<IServiceScopeFactory>(), Clock,
            NullLogger<OutboxDispatcher>.Instance);
    }

    private sealed class FakeConsumer(Result result) : IIntegrationEventConsumer
    {
        public string EventType => "test.event";
        public int Calls { get; private set; }
        public Task<Result> HandleAsync(string payload, string? causedByUserId, CancellationToken ct = default)
        { Calls++; return Task.FromResult(result); }
    }

    private sealed class FakeSource(string eventType = "test.event") : IOutboxSource
    {
        public string Name => "fake";
        public bool Dispatched { get; private set; }
        public bool DeadLettered { get; private set; }
        public int Attempts { get; private set; }
        public string Error { get; private set; } = "";
        private PendingOutboxMessage Row => new(Name, 1, eventType, "{}", Attempts,
            Clock.UtcNow, null, Error, DeadLettered ? Clock.UtcNow : null);
        public Task<IReadOnlyList<PendingOutboxMessage>> PendingAsync(int take, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingOutboxMessage>>(Dispatched || DeadLettered ? [] : [Row]);
        public Task MarkDispatchedAsync(long id, DateTime utcNow, CancellationToken ct = default)
        { Dispatched = true; return Task.CompletedTask; }
        public Task MarkFailedAsync(long id, string error, DateTime utcNow, int maxAttempts, CancellationToken ct = default)
        { Attempts++; Error = error; DeadLettered = Attempts >= maxAttempts; return Task.CompletedTask; }
        public Task<IReadOnlyList<PendingOutboxMessage>> DeadLettersAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<PendingOutboxMessage>>(DeadLettered ? [Row] : []);
        public Task RetryAsync(long id, CancellationToken ct = default)
        { Attempts = 0; Error = ""; DeadLettered = false; return Task.CompletedTask; }
    }
}
