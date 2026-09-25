using ImportErp.Application;
using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

/// <summary>SQLite implementation used by the local test profile. Production claims use PostgreSQL SKIP LOCKED.</summary>
public sealed class SqliteOutboxDispatchStore(IDbContextFactory<SqliteHistoricalDbContext> contextFactory) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxLease>> ClaimAsync(string owner, int maximum, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        var nowText = Timestamp(now);
        var expires = Timestamp(now.Add(leaseDuration));
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // SQLite is the single-node local profile. PostgreSQL is the production queue and uses SKIP LOCKED.
        var candidates = (await db.OutboxMessages.Where(row => row.PublishedAt == null && row.DeadLetteredAt == null)
                .OrderBy(row => row.OccurredAt).ThenBy(row => row.EventId).ToListAsync(cancellationToken))
            .Where(row => IsDue(row.NextAttemptAt, now) && IsDue(row.LeaseExpiresAt, now))
            .Take(maximum).ToArray();
        foreach (var row in candidates)
        {
            row.LeaseOwner = owner;
            row.LeaseExpiresAt = expires;
            row.AttemptCount++;
        }
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return candidates.Select(row => new OutboxLease(ToEvent(row), owner, now.Add(leaseDuration))).ToArray();
    }

    public Task MarkPublishedAsync(Guid eventId, string owner, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(eventId, owner, row =>
        {
            row.PublishedAt = Timestamp(publishedAt); row.NextAttemptAt = null; row.LastError = null;
            row.LeaseOwner = null; row.LeaseExpiresAt = null;
        }, cancellationToken);

    public Task ScheduleRetryAsync(Guid eventId, string owner, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(eventId, owner, row =>
        {
            row.NextAttemptAt = Timestamp(nextAttemptAt); row.LastError = Trim(error);
            row.LeaseOwner = null; row.LeaseExpiresAt = null;
        }, cancellationToken);

    public Task DeadLetterAsync(Guid eventId, string owner, DateTimeOffset failedAt, string error, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(eventId, owner, row =>
        {
            row.DeadLetteredAt = Timestamp(failedAt); row.LastError = Trim(error);
            row.LeaseOwner = null; row.LeaseExpiresAt = null;
        }, cancellationToken);

    private async Task UpdateOwnedAsync(Guid eventId, string owner, Action<OutboxMessageRow> update, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.OutboxMessages.SingleOrDefaultAsync(value => value.EventId == eventId && value.LeaseOwner == owner, cancellationToken);
        if (row is null) return; // A recovered lease must not be completed by its former owner.
        update(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsDue(string? value, DateTimeOffset now) => value is null || !DateTimeOffset.TryParse(value, out var at) || at <= now;
    internal static string Timestamp(DateTimeOffset value) => value.UtcDateTime.ToString("O");
    internal static string Trim(string value) => value.Length <= 2000 ? value : value[..2000];
    internal static OutboxEvent ToEvent(OutboxMessageRow row) => new(row.EventId, row.EventType, row.AggregateType,
        row.AggregateId, row.PayloadJson, DateTimeOffset.TryParse(row.OccurredAt, out var occurred) ? occurred : DateTimeOffset.MinValue, row.AttemptCount);
}

public sealed class SqliteConsumerInbox(IDbContextFactory<SqliteHistoricalDbContext> contextFactory) : IConsumerInbox
{
    public async Task<InboxAcquireResult> TryAcquireAsync(string consumerName, Guid eventId, string owner,
        TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var row = await db.EventInboxMessages.SingleOrDefaultAsync(value => value.ConsumerName == consumerName && value.EventId == eventId, cancellationToken);
        if (row is { Status: "COMPLETED" }) return InboxAcquireResult.AlreadyCompleted;
        if (row is not null && !IsExpired(row.LeaseExpiresAt, now)) return InboxAcquireResult.Busy;
        if (row is null)
        {
            row = new EventInboxMessageRow { ConsumerName = consumerName, EventId = eventId };
            db.EventInboxMessages.Add(row);
        }
        row.Status = "PROCESSING";
        row.LeaseOwner = owner;
        row.LeaseExpiresAt = SqliteOutboxDispatchStore.Timestamp(now.Add(leaseDuration));
        row.AttemptCount++;
        row.LastError = null;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return InboxAcquireResult.Acquired;
    }

    public Task MarkCompletedAsync(string consumerName, Guid eventId, string owner, DateTimeOffset completedAt, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(consumerName, eventId, owner, row =>
        {
            row.Status = "COMPLETED"; row.ProcessedAt = SqliteOutboxDispatchStore.Timestamp(completedAt);
            row.LeaseOwner = null; row.LeaseExpiresAt = null; row.LastError = null;
        }, cancellationToken);

    public Task AbandonAsync(string consumerName, Guid eventId, string owner, string error, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(consumerName, eventId, owner, row =>
        {
            row.Status = "PENDING"; row.LeaseOwner = null; row.LeaseExpiresAt = null;
            row.LastError = SqliteOutboxDispatchStore.Trim(error);
        }, cancellationToken);

    private async Task UpdateOwnedAsync(string consumerName, Guid eventId, string owner, Action<EventInboxMessageRow> update, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.EventInboxMessages.SingleOrDefaultAsync(value => value.ConsumerName == consumerName && value.EventId == eventId && value.LeaseOwner == owner, cancellationToken);
        if (row is null) return;
        update(row);
        await db.SaveChangesAsync(cancellationToken);
    }

    private static bool IsExpired(string? value, DateTimeOffset now) => value is null || !DateTimeOffset.TryParse(value, out var at) || at <= now;
}
