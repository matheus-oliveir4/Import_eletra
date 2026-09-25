using System.Text.Json;
using ImportErp.Application;
using ImportErp.Domain;
using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

public sealed class SqliteWorkflowRepository(IDbContextFactory<SqliteHistoricalDbContext> contextFactory) : IWorkflowRepository
{
    public async Task<string?> GetImporterAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        return type switch
        {
            WorkflowAggregateType.PurchaseOrder => await db.PurchaseOrders.AsNoTracking().Where(row => row.Id == id).Select(row => row.Importer).SingleOrDefaultAsync(cancellationToken),
            WorkflowAggregateType.ImportProcess => await db.ImportProcesses.AsNoTracking().Where(row => row.Id == id).Select(row => row.Importer).SingleOrDefaultAsync(cancellationToken),
            _ => null
        };
    }

    public async Task<WorkflowSnapshot?> GetAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await GetImporterAsync(type, id, cancellationToken) is null) return null;
        var key = TypeName(type);
        var row = await db.WorkflowStates.AsNoTracking().SingleOrDefaultAsync(value => value.AggregateType == key && value.AggregateId == id, cancellationToken);
        return row is null ? await InitialAsync(db, type, id, cancellationToken) : ToSnapshot(row);
    }

    public async Task<IReadOnlyList<WorkflowTransitionEvent>?> GetHistoryAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await GetImporterAsync(type, id, cancellationToken) is null) return null;
        var key = TypeName(type);
        var rows = await db.WorkflowTransitionEvents.AsNoTracking()
            .Where(value => value.AggregateType == key && value.AggregateId == id)
            .OrderBy(value => value.Version).ToArrayAsync(cancellationToken);
        return rows.Select(ToEvent).ToArray();
    }

    public async Task<WorkflowTransitionEvent> TransitionAsync(WorkflowAggregateType type, Guid id, long expectedVersion,
        string target, string actor, string reason, IReadOnlyDictionary<string, string?> evidence,
        CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var key = TypeName(type);
        var state = await db.WorkflowStates.SingleOrDefaultAsync(value => value.AggregateType == key && value.AggregateId == id, cancellationToken);
        if (state is null)
        {
            var initial = await InitialAsync(db, type, id, cancellationToken)
                ?? throw new KeyNotFoundException("Entidade não encontrada.");
            state = new WorkflowStateRow { AggregateType = key, AggregateId = id, CurrentState = initial.State, Version = 0 };
            db.WorkflowStates.Add(state);
            await db.SaveChangesAsync(cancellationToken);
        }
        if (state.Version != expectedVersion) throw new ConcurrencyException(id, expectedVersion, state.Version);

        var from = state.CurrentState;
        var to = WorkflowRules.Canonical(target);
        if (to == "REABRIR") to = state.PreviousActiveState ?? (type == WorkflowAggregateType.ImportProcess ? "NOVO" : "IDENTIFICADA_NO_LEGADO");
        if (IsActive(type, from)) state.PreviousActiveState = from;
        state.CurrentState = to;
        state.Version++;
        var row = new WorkflowTransitionEventRow
        {
            Id = Guid.NewGuid(), AggregateType = key, AggregateId = id, FromState = from, ToState = to,
            Actor = actor, Reason = reason, OccurredAt = DateTimeOffset.UtcNow.ToString("O"),
            EvidenceJson = JsonSerializer.Serialize(evidence), Version = state.Version
        };
        db.WorkflowTransitionEvents.Add(row);
        db.AuditLog.Add(new AuditLogRow
        {
            Id = Guid.NewGuid(), AggregateType = key, AggregateId = id,
            EntityType = type == WorkflowAggregateType.PurchaseOrder ? "PurchaseOrder" : "ImportProcess",
            EntityId = id, Operation = "TRANSITION", FieldName = "workflowState",
            OldValueJson = JsonSerializer.Serialize(from), NewValueJson = JsonSerializer.Serialize(to),
            ActorId = actor, OccurredAt = row.OccurredAt, Reason = reason, CorrelationId = row.Id
        });
        db.OutboxMessages.Add(new OutboxMessageRow
        {
            EventId = row.Id, EventType = "WorkflowTransitioned", AggregateType = key,
            AggregateId = id, PayloadJson = JsonSerializer.Serialize(new
            {
                aggregateType = key, aggregateId = id, fromState = from, toState = to,
                actorId = actor, reason, occurredAt = row.OccurredAt, version = row.Version
            }), OccurredAt = row.OccurredAt
        });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            await using var currentDb = await contextFactory.CreateDbContextAsync(cancellationToken);
            var actualVersion = await currentDb.WorkflowStates.AsNoTracking()
                .Where(value => value.AggregateType == key && value.AggregateId == id)
                .Select(value => (long?)value.Version).SingleOrDefaultAsync(cancellationToken) ?? expectedVersion + 1;
            throw new ConcurrencyException(id, expectedVersion, actualVersion);
        }
        await transaction.CommitAsync(cancellationToken);
        return ToEvent(row);
    }

    private static async Task<WorkflowSnapshot?> InitialAsync(SqliteHistoricalDbContext db, WorkflowAggregateType type, Guid id, CancellationToken cancellationToken)
    {
        string? legacyStatus = null;
        var exists = type switch
        {
            WorkflowAggregateType.PurchaseOrder => await db.PurchaseOrders.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken),
            WorkflowAggregateType.ImportProcess => await db.ImportProcesses.AsNoTracking().AnyAsync(value => value.Id == id, cancellationToken),
            _ => false
        };
        if (!exists) return null;
        if (type == WorkflowAggregateType.ImportProcess)
            legacyStatus = await db.ImportProcesses.AsNoTracking().Where(value => value.Id == id).Select(value => value.LogisticsStatus).SingleOrDefaultAsync(cancellationToken);
        return new WorkflowSnapshot(type, id, WorkflowRules.InitialState(type, legacyStatus), null, 0);
    }

    private static bool IsActive(WorkflowAggregateType type, string state) => type == WorkflowAggregateType.ImportProcess
        ? state is not ("FINALIZADO" or "CANCELADO")
        : state is not ("CONCLUIDA" or "CANCELADA");
    private static string TypeName(WorkflowAggregateType type) => type switch
    {
        WorkflowAggregateType.PurchaseOrder => "PURCHASE_ORDER",
        WorkflowAggregateType.ImportProcess => "IMPORT_PROCESS",
        _ => throw new ArgumentOutOfRangeException(nameof(type))
    };
    private static WorkflowSnapshot ToSnapshot(WorkflowStateRow row) => new(
        row.AggregateType == "PURCHASE_ORDER" ? WorkflowAggregateType.PurchaseOrder : WorkflowAggregateType.ImportProcess,
        row.AggregateId, row.CurrentState, row.PreviousActiveState, row.Version);
    private static WorkflowTransitionEvent ToEvent(WorkflowTransitionEventRow row) => new(
        row.Id, row.AggregateType == "PURCHASE_ORDER" ? WorkflowAggregateType.PurchaseOrder : WorkflowAggregateType.ImportProcess,
        row.AggregateId, row.FromState, row.ToState, row.Actor, row.Reason,
        DateTimeOffset.TryParse(row.OccurredAt, out var instant) ? instant : DateTimeOffset.MinValue,
        JsonSerializer.Deserialize<Dictionary<string, string?>>(row.EvidenceJson) ?? new(), row.Version);
}

public sealed class DenyWorkflowRepository : IWorkflowRepository
{
    public Task<string?> GetImporterAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    public Task<WorkflowSnapshot?> GetAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken) => Task.FromResult<WorkflowSnapshot?>(null);
    public Task<IReadOnlyList<WorkflowTransitionEvent>?> GetHistoryAsync(WorkflowAggregateType type, Guid id, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<WorkflowTransitionEvent>?>(null);
    public Task<WorkflowTransitionEvent> TransitionAsync(WorkflowAggregateType type, Guid id, long expectedVersion, string target, string actor, string reason, IReadOnlyDictionary<string, string?> evidence, CancellationToken cancellationToken) => throw new InvalidOperationException("Workflow requer armazenamento operacional configurado.");
}
