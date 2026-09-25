namespace ImportErp.Application;

/// <summary>
/// A durable domain event. Delivery is deliberately at-least-once: EventId is
/// stable so every consumer can deduplicate its own work.
/// </summary>
public sealed record OutboxEvent(
    Guid EventId,
    string EventType,
    string AggregateType,
    Guid AggregateId,
    string PayloadJson,
    DateTimeOffset OccurredAt,
    int AttemptCount);

public sealed record OutboxLease(OutboxEvent Event, string Owner, DateTimeOffset ExpiresAt);

public interface IOutboxStore
{
    Task<IReadOnlyList<OutboxLease>> ClaimAsync(string owner, int maximum, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkPublishedAsync(Guid eventId, string owner, DateTimeOffset publishedAt, CancellationToken cancellationToken);
    Task ScheduleRetryAsync(Guid eventId, string owner, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken);
    Task DeadLetterAsync(Guid eventId, string owner, DateTimeOffset failedAt, string error, CancellationToken cancellationToken);
}

public enum InboxAcquireResult { Acquired, AlreadyCompleted, Busy }

/// <summary>
/// The inbox is scoped to a consumer. It prevents a completed local handler
/// from executing again when the outbox is redelivered.
/// </summary>
public interface IConsumerInbox
{
    Task<InboxAcquireResult> TryAcquireAsync(string consumerName, Guid eventId, string owner,
        TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken);
    Task MarkCompletedAsync(string consumerName, Guid eventId, string owner, DateTimeOffset completedAt,
        CancellationToken cancellationToken);
    Task AbandonAsync(string consumerName, Guid eventId, string owner, string error, CancellationToken cancellationToken);
}

/// <summary>Implementations must use a stable name and make external side effects idempotent by EventId.</summary>
public interface IOutboxConsumer
{
    string Name { get; }
    Task ConsumeAsync(OutboxEvent message, CancellationToken cancellationToken);
}

public abstract class OutboxConsumerException(string message, bool isTransient, Exception? innerException = null)
    : Exception(message, innerException)
{
    public bool IsTransient { get; } = isTransient;
}

public sealed class TransientOutboxConsumerException(string message, Exception? innerException = null)
    : OutboxConsumerException(message, true, innerException);

public sealed class PermanentOutboxConsumerException(string message, Exception? innerException = null)
    : OutboxConsumerException(message, false, innerException);

public sealed record OutboxDispatchResult(int Delivered, int Retried, int DeadLettered, int Deferred);

public sealed class OutboxDispatcher(
    IOutboxStore outbox,
    IConsumerInbox inbox,
    IEnumerable<IOutboxConsumer> registeredConsumers)
{
    private readonly IReadOnlyList<IOutboxConsumer> _consumers = registeredConsumers.ToArray();

    public async Task<OutboxDispatchResult> DispatchOnceAsync(string owner, int maximum, TimeSpan leaseDuration,
        int maxAttempts, DateTimeOffset now, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(owner);
        if (maximum < 1) throw new ArgumentOutOfRangeException(nameof(maximum));
        if (leaseDuration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(leaseDuration));
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));

        // A configured transport/consumer is required before a message may be
        // acknowledged. This keeps undefined integrations pending, not lost.
        if (_consumers.Count == 0) return new OutboxDispatchResult(0, 0, 0, 0);

        var leases = await outbox.ClaimAsync(owner, maximum, leaseDuration, now, cancellationToken);
        var delivered = 0;
        var retried = 0;
        var deadLettered = 0;
        var deferred = 0;
        foreach (var lease in leases)
        {
            var outcome = await DeliverAsync(lease, leaseDuration, maxAttempts, now, cancellationToken);
            switch (outcome)
            {
                case DeliveryOutcome.Delivered: delivered++; break;
                case DeliveryOutcome.Retried: retried++; break;
                case DeliveryOutcome.DeadLettered: deadLettered++; break;
                default: deferred++; break;
            }
        }

        return new OutboxDispatchResult(delivered, retried, deadLettered, deferred);
    }

    private async Task<DeliveryOutcome> DeliverAsync(OutboxLease lease, TimeSpan leaseDuration, int maxAttempts,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        foreach (var consumer in _consumers)
        {
            var acquired = await inbox.TryAcquireAsync(consumer.Name, lease.Event.EventId, lease.Owner,
                leaseDuration, now, cancellationToken);
            if (acquired == InboxAcquireResult.AlreadyCompleted) continue;
            if (acquired == InboxAcquireResult.Busy)
            {
                await outbox.ScheduleRetryAsync(lease.Event.EventId, lease.Owner, now.AddSeconds(5),
                    $"Consumer '{consumer.Name}' still owns a valid inbox lease.", cancellationToken);
                return DeliveryOutcome.Deferred;
            }

            try
            {
                await consumer.ConsumeAsync(lease.Event, cancellationToken);
                await inbox.MarkCompletedAsync(consumer.Name, lease.Event.EventId, lease.Owner, now, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await inbox.AbandonAsync(consumer.Name, lease.Event.EventId, lease.Owner, "Dispatcher cancellation.", CancellationToken.None);
                await outbox.ScheduleRetryAsync(lease.Event.EventId, lease.Owner, now, "Dispatcher cancellation.", CancellationToken.None);
                throw;
            }
            catch (Exception exception)
            {
                var transient = exception is not OutboxConsumerException typed || typed.IsTransient;
                var error = Describe(exception);
                await inbox.AbandonAsync(consumer.Name, lease.Event.EventId, lease.Owner, error, cancellationToken);
                if (!transient || lease.Event.AttemptCount >= maxAttempts)
                {
                    await outbox.DeadLetterAsync(lease.Event.EventId, lease.Owner, now, error, cancellationToken);
                    return DeliveryOutcome.DeadLettered;
                }

                await outbox.ScheduleRetryAsync(lease.Event.EventId, lease.Owner,
                    now.Add(Backoff(lease.Event.AttemptCount)), error, cancellationToken);
                return DeliveryOutcome.Retried;
            }
        }

        await outbox.MarkPublishedAsync(lease.Event.EventId, lease.Owner, now, cancellationToken);
        return DeliveryOutcome.Delivered;
    }

    private static TimeSpan Backoff(int attempt) => TimeSpan.FromSeconds(Math.Min(300, 2 * Math.Pow(2, Math.Min(8, attempt - 1))));
    private static string Describe(Exception exception)
    {
        var description = $"{exception.GetType().Name}: {exception.Message}";
        return description.Length <= 2000 ? description : description[..2000];
    }
    private enum DeliveryOutcome { Delivered, Retried, DeadLettered, Deferred }
}
