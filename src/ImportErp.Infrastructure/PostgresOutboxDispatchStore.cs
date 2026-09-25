using ImportErp.Application;
using Npgsql;

namespace ImportErp.Infrastructure;

/// <summary>PostgreSQL queue implementation. Claiming uses FOR UPDATE SKIP LOCKED so workers do not wait on each other.</summary>
public sealed class PostgresOutboxDispatchStore(NpgsqlDataSource dataSource) : IOutboxStore
{
    public async Task<IReadOnlyList<OutboxLease>> ClaimAsync(string owner, int maximum, TimeSpan leaseDuration,
        DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            WITH candidates AS (
                SELECT event_id
                FROM audit.outbox_message
                WHERE published_at IS NULL
                  AND dead_lettered_at IS NULL
                  AND (next_attempt_at IS NULL OR next_attempt_at <= @now)
                  AND (lease_expires_at IS NULL OR lease_expires_at <= @now)
                ORDER BY occurred_at, event_id
                FOR UPDATE SKIP LOCKED
                LIMIT @maximum
            )
            UPDATE audit.outbox_message AS message
            SET lease_owner = @owner,
                lease_expires_at = @expiresAt,
                attempt_count = message.attempt_count + 1
            FROM candidates
            WHERE message.event_id = candidates.event_id
            RETURNING message.event_id, message.event_type, message.aggregate_type, message.aggregate_id,
                      message.payload::text, message.occurred_at, message.attempt_count
            """, connection);
        command.Parameters.AddWithValue("now", now);
        command.Parameters.AddWithValue("maximum", maximum);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("expiresAt", now.Add(leaseDuration));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var leases = new List<OutboxLease>();
        while (await reader.ReadAsync(cancellationToken))
        {
            var message = new OutboxEvent(reader.GetGuid(0), reader.GetString(1), reader.GetString(2), reader.GetGuid(3),
                reader.GetString(4), reader.GetFieldValue<DateTimeOffset>(5), reader.GetInt32(6));
            leases.Add(new OutboxLease(message, owner, now.Add(leaseDuration)));
        }
        return leases;
    }

    public Task MarkPublishedAsync(Guid eventId, string owner, DateTimeOffset publishedAt, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(eventId, owner, """
            published_at = @at, next_attempt_at = NULL, last_error = NULL,
            lease_owner = NULL, lease_expires_at = NULL
            """, publishedAt, null, cancellationToken);

    public Task ScheduleRetryAsync(Guid eventId, string owner, DateTimeOffset nextAttemptAt, string error, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(eventId, owner, """
            next_attempt_at = @at, last_error = @error,
            lease_owner = NULL, lease_expires_at = NULL
            """, nextAttemptAt, error, cancellationToken);

    public Task DeadLetterAsync(Guid eventId, string owner, DateTimeOffset failedAt, string error, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(eventId, owner, """
            dead_lettered_at = @at, last_error = @error,
            lease_owner = NULL, lease_expires_at = NULL
            """, failedAt, error, cancellationToken);

    private async Task UpdateOwnedAsync(Guid eventId, string owner, string assignments, DateTimeOffset at, string? error,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            UPDATE audit.outbox_message SET {assignments}
            WHERE event_id = @eventId AND lease_owner = @owner AND published_at IS NULL
            """, connection);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("at", at);
        if (error is not null) command.Parameters.AddWithValue("error", error.Length <= 2000 ? error : error[..2000]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}

public sealed class PostgresConsumerInbox(NpgsqlDataSource dataSource) : IConsumerInbox
{
    public async Task<InboxAcquireResult> TryAcquireAsync(string consumerName, Guid eventId, string owner,
        TimeSpan leaseDuration, DateTimeOffset now, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var acquire = new NpgsqlCommand("""
            INSERT INTO audit.event_inbox_message
                (consumer_name, event_id, status, lease_owner, lease_expires_at, attempt_count)
            VALUES (@consumer, @eventId, 'PROCESSING', @owner, @expiresAt, 1)
            ON CONFLICT (consumer_name, event_id) DO UPDATE
            SET status = 'PROCESSING', lease_owner = EXCLUDED.lease_owner,
                lease_expires_at = EXCLUDED.lease_expires_at,
                attempt_count = audit.event_inbox_message.attempt_count + 1,
                last_error = NULL
            WHERE audit.event_inbox_message.status <> 'COMPLETED'
              AND (audit.event_inbox_message.lease_expires_at IS NULL OR audit.event_inbox_message.lease_expires_at <= @now)
            RETURNING 1
            """, connection);
        acquire.Parameters.AddWithValue("consumer", consumerName);
        acquire.Parameters.AddWithValue("eventId", eventId);
        acquire.Parameters.AddWithValue("owner", owner);
        acquire.Parameters.AddWithValue("expiresAt", now.Add(leaseDuration));
        acquire.Parameters.AddWithValue("now", now);
        if (await acquire.ExecuteScalarAsync(cancellationToken) is not null) return InboxAcquireResult.Acquired;

        await using var state = new NpgsqlCommand("""
            SELECT status FROM audit.event_inbox_message WHERE consumer_name = @consumer AND event_id = @eventId
            """, connection);
        state.Parameters.AddWithValue("consumer", consumerName);
        state.Parameters.AddWithValue("eventId", eventId);
        return string.Equals(await state.ExecuteScalarAsync(cancellationToken) as string, "COMPLETED", StringComparison.Ordinal)
            ? InboxAcquireResult.AlreadyCompleted : InboxAcquireResult.Busy;
    }

    public Task MarkCompletedAsync(string consumerName, Guid eventId, string owner, DateTimeOffset completedAt,
        CancellationToken cancellationToken) => UpdateOwnedAsync(consumerName, eventId, owner, """
            status = 'COMPLETED', processed_at = @at, lease_owner = NULL, lease_expires_at = NULL, last_error = NULL
            """, completedAt, null, cancellationToken);

    public Task AbandonAsync(string consumerName, Guid eventId, string owner, string error, CancellationToken cancellationToken) =>
        UpdateOwnedAsync(consumerName, eventId, owner, """
            status = 'PENDING', lease_owner = NULL, lease_expires_at = NULL, last_error = @error
            """, DateTimeOffset.UtcNow, error, cancellationToken);

    private async Task UpdateOwnedAsync(string consumerName, Guid eventId, string owner, string assignments,
        DateTimeOffset at, string? error, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var command = new NpgsqlCommand($"""
            UPDATE audit.event_inbox_message SET {assignments}
            WHERE consumer_name = @consumer AND event_id = @eventId AND lease_owner = @owner
            """, connection);
        command.Parameters.AddWithValue("consumer", consumerName);
        command.Parameters.AddWithValue("eventId", eventId);
        command.Parameters.AddWithValue("owner", owner);
        command.Parameters.AddWithValue("at", at);
        if (error is not null) command.Parameters.AddWithValue("error", error.Length <= 2000 ? error : error[..2000]);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
