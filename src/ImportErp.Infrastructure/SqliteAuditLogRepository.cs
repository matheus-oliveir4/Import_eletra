using ImportErp.Application;
using ImportErp.Domain;
using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

public sealed class SqliteAuditLogRepository(IDbContextFactory<SqliteHistoricalDbContext> contextFactory) : IAuditLogRepository
{
    public async Task<AuditLogPage> ListAsync(WorkflowAggregateType aggregateType, Guid aggregateId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        var safePage = Math.Max(1, page);
        var safeSize = Math.Clamp(pageSize, 1, 100);
        var aggregateName = aggregateType == WorkflowAggregateType.PurchaseOrder ? "PURCHASE_ORDER" : "IMPORT_PROCESS";
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.AuditLog.AsNoTracking().Where(value => value.AggregateType == aggregateName && value.AggregateId == aggregateId);
        var total = await query.CountAsync(cancellationToken);
        var rows = await query.OrderByDescending(value => value.OccurredAt).ThenByDescending(value => value.Id)
            .Skip((safePage - 1) * safeSize).Take(safeSize).ToArrayAsync(cancellationToken);
        var items = rows.Select(value => new AuditLogEntry(
            value.Id, value.AggregateType, value.AggregateId, value.EntityType, value.EntityId,
            value.Operation, value.FieldName, value.OldValueJson, value.NewValueJson, value.ActorId,
            DateTimeOffset.TryParse(value.OccurredAt, out var at) ? at : DateTimeOffset.MinValue,
            value.Reason, value.CorrelationId)).ToArray();
        return new AuditLogPage(safePage, safeSize, total, safePage * safeSize < total, items);
    }
}
