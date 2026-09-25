using ImportErp.Application;
using ImportErp.Domain;
using Npgsql;

namespace ImportErp.Infrastructure;

public sealed class PostgresAuditLogRepository(NpgsqlDataSource dataSource) : IAuditLogRepository
{
    public async Task<AuditLogPage> ListAsync(WorkflowAggregateType aggregateType, Guid aggregateId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var name = aggregateType == WorkflowAggregateType.PurchaseOrder ? "PURCHASE_ORDER" : "IMPORT_PROCESS";
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var countCommand = new NpgsqlCommand("SELECT count(*) FROM audit.audit_log WHERE aggregate_type = @type AND aggregate_id = @id", connection);
        countCommand.Parameters.AddWithValue("type", name);
        countCommand.Parameters.AddWithValue("id", aggregateId);
        var total = Convert.ToInt32(await countCommand.ExecuteScalarAsync(cancellationToken));
        await using var command = new NpgsqlCommand("""
            SELECT id, aggregate_type, aggregate_id, entity_type, entity_id, operation, field_name,
                   old_value::text, new_value::text, actor_id, occurred_at, reason, correlation_id
            FROM audit.audit_log WHERE aggregate_type = @type AND aggregate_id = @id
            ORDER BY occurred_at DESC, id DESC LIMIT @limit OFFSET @offset
            """, connection);
        command.Parameters.AddWithValue("type", name);
        command.Parameters.AddWithValue("id", aggregateId);
        command.Parameters.AddWithValue("limit", safeSize);
        command.Parameters.AddWithValue("offset", (safePage - 1) * safeSize);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var items = new List<AuditLogEntry>();
        while (await reader.ReadAsync(cancellationToken))
            items.Add(new AuditLogEntry(reader.GetGuid(0), reader.GetString(1), reader.GetGuid(2), reader.GetString(3), reader.GetGuid(4),
                reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
                reader.IsDBNull(8) ? null : reader.GetString(8), reader.GetString(9), reader.GetFieldValue<DateTimeOffset>(10),
                reader.IsDBNull(11) ? null : reader.GetString(11), reader.GetGuid(12)));
        return new AuditLogPage(safePage, safeSize, total, safePage * safeSize < total, items);
    }
}

public sealed class DenyAuditLogRepository : IAuditLogRepository
{
    public Task<AuditLogPage> ListAsync(WorkflowAggregateType aggregateType, Guid aggregateId, int page, int pageSize,
        CancellationToken cancellationToken) => Task.FromResult(new AuditLogPage(1, 1, 0, false, []));
}
