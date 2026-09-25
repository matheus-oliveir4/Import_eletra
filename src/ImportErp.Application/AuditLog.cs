using ImportErp.Domain;

namespace ImportErp.Application;

public sealed record AuditLogEntry(
    Guid Id,
    string AggregateType,
    Guid AggregateId,
    string EntityType,
    Guid EntityId,
    string Operation,
    string? FieldName,
    string? OldValueJson,
    string? NewValueJson,
    string ActorId,
    DateTimeOffset OccurredAt,
    string? Reason,
    Guid CorrelationId);

public sealed record AuditLogPage(int Page, int PageSize, int TotalCount, bool HasNext, IReadOnlyList<AuditLogEntry> Items);

public interface IAuditLogRepository
{
    Task<AuditLogPage> ListAsync(WorkflowAggregateType aggregateType, Guid aggregateId, int page, int pageSize,
        CancellationToken cancellationToken);
}
