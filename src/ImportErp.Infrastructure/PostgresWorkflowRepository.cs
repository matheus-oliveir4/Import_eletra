using System.Text.Json;
using ImportErp.Application;
using ImportErp.Domain;
using Npgsql;

namespace ImportErp.Infrastructure;

public sealed class PostgresWorkflowRepository(NpgsqlDataSource dataSource) : IWorkflowRepository
{
    public async Task<string?> GetImporterAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        return await ReadImporterAsync(connection, null, type, id, cancellationToken);
    }

    public async Task<WorkflowSnapshot?> GetAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var importer = await ReadImporterAsync(connection, null, type, id, cancellationToken);
        if (importer is null) return null;
        var stored = await ReadStateAsync(connection, null, type, id, false, cancellationToken);
        return stored ?? new WorkflowSnapshot(type, id, await ReadInitialStateAsync(connection, null, type, id, cancellationToken), null, 0);
    }

    public async Task<IReadOnlyList<WorkflowTransitionEvent>?> GetHistoryAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        if (await ReadImporterAsync(connection, null, type, id, cancellationToken) is null) return null;
        await using var command = new NpgsqlCommand("""
            SELECT id, aggregate_type, aggregate_id, from_state, to_state, actor, reason, occurred_at, evidence::text, version
            FROM audit.workflow_transition_event
            WHERE aggregate_type = @type AND aggregate_id = @id ORDER BY version
            """, connection);
        command.Parameters.AddWithValue("type", TypeName(type));
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var events = new List<WorkflowTransitionEvent>();
        while (await reader.ReadAsync(cancellationToken)) events.Add(ReadEvent(reader));
        return events;
    }

    public async Task<WorkflowTransitionEvent> TransitionAsync(WorkflowAggregateType type, Guid id, long expectedVersion,
        string target, string actor, string reason, IReadOnlyDictionary<string, string?> evidence,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var initialState = await ReadInitialStateAsync(connection, transaction, type, id, cancellationToken);
        await using (var initialize = new NpgsqlCommand("""
            INSERT INTO audit.workflow_state (aggregate_type, aggregate_id, current_state, version)
            VALUES (@type, @id, @state, 0) ON CONFLICT (aggregate_type, aggregate_id) DO NOTHING
            """, connection, transaction))
        {
            initialize.Parameters.AddWithValue("type", TypeName(type));
            initialize.Parameters.AddWithValue("id", id);
            initialize.Parameters.AddWithValue("state", initialState);
            await initialize.ExecuteNonQueryAsync(cancellationToken);
        }
        var current = await ReadStateAsync(connection, transaction, type, id, true, cancellationToken)
            ?? throw new KeyNotFoundException("Entidade não encontrada.");
        if (current.Version != expectedVersion) throw new ConcurrencyException(id, expectedVersion, current.Version);

        var from = current.State;
        var to = WorkflowRules.Canonical(target);
        if (to == "REABRIR") to = current.PreviousActiveState ?? (type == WorkflowAggregateType.ImportProcess ? "NOVO" : "IDENTIFICADA_NO_LEGADO");
        var previousActive = IsActive(type, from) ? from : current.PreviousActiveState;
        var newVersion = current.Version + 1;
        await using (var update = new NpgsqlCommand("""
            UPDATE audit.workflow_state SET current_state = @state, previous_active_state = @previous, version = @version
            WHERE aggregate_type = @type AND aggregate_id = @id AND version = @expected
            """, connection, transaction))
        {
            update.Parameters.AddWithValue("state", to);
            update.Parameters.AddWithValue("previous", (object?)previousActive ?? DBNull.Value);
            update.Parameters.AddWithValue("version", newVersion);
            update.Parameters.AddWithValue("type", TypeName(type));
            update.Parameters.AddWithValue("id", id);
            update.Parameters.AddWithValue("expected", expectedVersion);
            if (await update.ExecuteNonQueryAsync(cancellationToken) != 1) throw new ConcurrencyException(id, expectedVersion, newVersion);
        }
        var eventId = Guid.NewGuid();
        var occurredAt = DateTimeOffset.UtcNow;
        await using (var append = new NpgsqlCommand("""
            INSERT INTO audit.workflow_transition_event
                (id, aggregate_type, aggregate_id, from_state, to_state, actor, reason, occurred_at, evidence, version)
            VALUES (@eventId, @type, @id, @from, @to, @actor, @reason, @occurredAt, @evidence::jsonb, @version)
            """, connection, transaction))
        {
            append.Parameters.AddWithValue("eventId", eventId);
            append.Parameters.AddWithValue("type", TypeName(type));
            append.Parameters.AddWithValue("id", id);
            append.Parameters.AddWithValue("from", from);
            append.Parameters.AddWithValue("to", to);
            append.Parameters.AddWithValue("actor", actor);
            append.Parameters.AddWithValue("reason", reason);
            append.Parameters.AddWithValue("occurredAt", occurredAt);
            append.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(evidence));
            append.Parameters.AddWithValue("version", newVersion);
            await append.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var audit = new NpgsqlCommand("""
            INSERT INTO audit.audit_log
                (id, aggregate_type, aggregate_id, entity_type, entity_id, operation, field_name,
                 old_value, new_value, actor_id, occurred_at, reason, correlation_id)
            VALUES (@id, @type, @aggregateId, @entityType, @entityId, 'TRANSITION', 'workflowState',
                    CAST(@oldValue AS jsonb), CAST(@newValue AS jsonb), @actor, @occurredAt, @reason, @correlationId)
            """, connection, transaction))
        {
            audit.Parameters.AddWithValue("id", Guid.NewGuid());
            audit.Parameters.AddWithValue("type", TypeName(type));
            audit.Parameters.AddWithValue("aggregateId", id);
            audit.Parameters.AddWithValue("entityType", type == WorkflowAggregateType.PurchaseOrder ? "PurchaseOrder" : "ImportProcess");
            audit.Parameters.AddWithValue("entityId", id);
            audit.Parameters.AddWithValue("oldValue", JsonSerializer.Serialize(from));
            audit.Parameters.AddWithValue("newValue", JsonSerializer.Serialize(to));
            audit.Parameters.AddWithValue("actor", actor);
            audit.Parameters.AddWithValue("occurredAt", occurredAt);
            audit.Parameters.AddWithValue("reason", reason);
            audit.Parameters.AddWithValue("correlationId", eventId);
            await audit.ExecuteNonQueryAsync(cancellationToken);
        }
        await using (var outbox = new NpgsqlCommand("""
            INSERT INTO audit.outbox_message
                (event_id, event_type, aggregate_type, aggregate_id, payload, occurred_at)
            VALUES (@eventId, 'WorkflowTransitioned', @type, @aggregateId, CAST(@payload AS jsonb), @occurredAt)
            """, connection, transaction))
        {
            outbox.Parameters.AddWithValue("eventId", eventId);
            outbox.Parameters.AddWithValue("type", TypeName(type));
            outbox.Parameters.AddWithValue("aggregateId", id);
            outbox.Parameters.AddWithValue("payload", JsonSerializer.Serialize(new
            {
                aggregateType = TypeName(type), aggregateId = id, fromState = from, toState = to,
                actorId = actor, reason, occurredAt, version = newVersion
            }));
            outbox.Parameters.AddWithValue("occurredAt", occurredAt);
            await outbox.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
        return new WorkflowTransitionEvent(eventId, type, id, from, to, actor, reason, occurredAt, evidence, newVersion);
    }

    private static async Task<string?> ReadImporterAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        var table = type == WorkflowAggregateType.PurchaseOrder ? "procurement.purchase_order" : "imports.import_process";
        await using var command = new NpgsqlCommand($"SELECT importer FROM {table} WHERE id = @id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    private static async Task<string> ReadInitialStateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        if (await ReadImporterAsync(connection, transaction, type, id, cancellationToken) is null)
            throw new KeyNotFoundException("Entidade não encontrada.");
        if (type == WorkflowAggregateType.PurchaseOrder) return WorkflowRules.InitialState(type);
        await using var command = new NpgsqlCommand("SELECT logistics_status FROM imports.import_process WHERE id = @id", connection, transaction);
        command.Parameters.AddWithValue("id", id);
        return WorkflowRules.InitialState(type, await command.ExecuteScalarAsync(cancellationToken) as string);
    }

    private static async Task<WorkflowSnapshot?> ReadStateAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        WorkflowAggregateType type, Guid id, bool forUpdate, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT current_state, previous_active_state, version FROM audit.workflow_state
            WHERE aggregate_type = @type AND aggregate_id = @id {(forUpdate ? "FOR UPDATE" : "")}
            """, connection, transaction);
        command.Parameters.AddWithValue("type", TypeName(type));
        command.Parameters.AddWithValue("id", id);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new WorkflowSnapshot(type, id, reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1), reader.GetInt64(2))
            : null;
    }

    private static WorkflowTransitionEvent ReadEvent(NpgsqlDataReader reader) => new(
        reader.GetGuid(0), reader.GetString(1) == "PURCHASE_ORDER" ? WorkflowAggregateType.PurchaseOrder : WorkflowAggregateType.ImportProcess,
        reader.GetGuid(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetString(6),
        reader.GetFieldValue<DateTimeOffset>(7), JsonSerializer.Deserialize<Dictionary<string, string?>>(reader.GetString(8)) ?? new(), reader.GetInt64(9));

    private static bool IsActive(WorkflowAggregateType type, string state) => type == WorkflowAggregateType.ImportProcess
        ? state is not ("FINALIZADO" or "CANCELADO") : state is not ("CONCLUIDA" or "CANCELADA");
    private static string TypeName(WorkflowAggregateType type) => type == WorkflowAggregateType.PurchaseOrder ? "PURCHASE_ORDER" : "IMPORT_PROCESS";
}
