using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

public sealed class SqliteHistoricalDbContext(DbContextOptions<SqliteHistoricalDbContext> options) : DbContext(options)
{
    public DbSet<BatchRow> Batches => Set<BatchRow>();
    public DbSet<SourceRow> SourceRows => Set<SourceRow>();
    public DbSet<PurchaseOrderRow> PurchaseOrders => Set<PurchaseOrderRow>();
    public DbSet<ImportProcessRow> ImportProcesses => Set<ImportProcessRow>();
    public DbSet<ObservationRow> Observations => Set<ObservationRow>();
    public DbSet<ProcessPurchaseOrderRow> ProcessPurchaseOrders => Set<ProcessPurchaseOrderRow>();
    public DbSet<ProcessCostRow> ProcessCosts => Set<ProcessCostRow>();
    public DbSet<ImportIssueRow> Issues => Set<ImportIssueRow>();
    public DbSet<QualityReviewRow> QualityReviews => Set<QualityReviewRow>();
    public DbSet<ErpUserRow> ErpUsers => Set<ErpUserRow>();
    public DbSet<ErpUserRoleRow> ErpUserRoles => Set<ErpUserRoleRow>();
    public DbSet<ErpUserImporterScopeRow> ErpUserImporterScopes => Set<ErpUserImporterScopeRow>();
    public DbSet<WorkflowStateRow> WorkflowStates => Set<WorkflowStateRow>();
    public DbSet<WorkflowTransitionEventRow> WorkflowTransitionEvents => Set<WorkflowTransitionEventRow>();
    public DbSet<AuditLogRow> AuditLog => Set<AuditLogRow>();
    public DbSet<OutboxMessageRow> OutboxMessages => Set<OutboxMessageRow>();
    public DbSet<EventInboxMessageRow> EventInboxMessages => Set<EventInboxMessageRow>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<BatchRow>().HasIndex(x => new { x.FileSha256, x.MappingVersion }).IsUnique();
        model.Entity<SourceRow>().HasIndex(x => new { x.BatchId, x.SheetName, x.RowNumber }).IsUnique();
        model.Entity<PurchaseOrderRow>().HasIndex(x => new { x.Importer, x.NormalizedNumber }).IsUnique();
        model.Entity<ImportProcessRow>().HasIndex(x => x.NormalizedIpNumber).IsUnique();
        model.Entity<ObservationRow>().HasIndex(x => x.SourceRowId).IsUnique();
        model.Entity<ProcessPurchaseOrderRow>().HasKey(x => new { x.PurchaseOrderId, x.ProcessId });
        model.Entity<ProcessCostRow>().HasIndex(x => new { x.SourceRowId, x.SourceColumn }).IsUnique();
        model.Entity<ImportIssueRow>().HasIndex(x => new { x.BatchId, x.SourceRowId, x.Code, x.ColumnName }).IsUnique();
        model.Entity<QualityReviewRow>().HasIndex(x => new { x.BatchId, x.SourceRowId, x.IssueCode, x.RecordedAt });
        model.Entity<ErpUserRow>().HasIndex(x => new { x.Issuer, x.Subject }).IsUnique();
        model.Entity<ErpUserRoleRow>().HasKey(x => new { x.UserId, x.Role });
        model.Entity<ErpUserImporterScopeRow>().HasKey(x => new { x.UserId, x.Importer });
        model.Entity<WorkflowStateRow>().HasKey(x => new { x.AggregateType, x.AggregateId });
        model.Entity<WorkflowStateRow>().Property(x => x.Version).IsConcurrencyToken();
        model.Entity<WorkflowTransitionEventRow>().HasIndex(x => new { x.AggregateType, x.AggregateId, x.Version }).IsUnique();
        model.Entity<OutboxMessageRow>().HasKey(x => x.EventId);
        model.Entity<OutboxMessageRow>().HasIndex(x => new { x.PublishedAt, x.OccurredAt });
        model.Entity<EventInboxMessageRow>().HasKey(x => new { x.ConsumerName, x.EventId });
        model.Entity<EventInboxMessageRow>().HasIndex(x => new { x.Status, x.LeaseExpiresAt });
    }
}

public sealed class BatchRow { public Guid Id { get; set; } public string FileName { get; set; } = ""; public string FileSha256 { get; set; } = ""; public string MappingVersion { get; set; } = ""; public string State { get; set; } = ""; public int SourceRowCount { get; set; } }
public sealed class SourceRow { public Guid Id { get; set; } public Guid BatchId { get; set; } public string SheetName { get; set; } = ""; public int RowNumber { get; set; } public string RawValuesJson { get; set; } = "{}"; public string ErrorColumnsJson { get; set; } = "[]"; public string RowHash { get; set; } = ""; }
public sealed class PurchaseOrderRow { public Guid Id { get; set; } public string Importer { get; set; } = ""; public string ExternalNumber { get; set; } = ""; public string NormalizedNumber { get; set; } = ""; public string OperationalFieldsJson { get; set; } = "{}"; public long Version { get; set; } }
public sealed class ImportProcessRow { public Guid Id { get; set; } public string? Importer { get; set; } public string IpNumber { get; set; } = ""; public string NormalizedIpNumber { get; set; } = ""; public string? LogisticsStatus { get; set; } }
public sealed class ObservationRow { public Guid Id { get; set; } public Guid PurchaseOrderId { get; set; } public Guid SourceRowId { get; set; } public int SourceRowNumber { get; set; } public string RawValuesJson { get; set; } = "{}"; }
public sealed class ProcessPurchaseOrderRow { public Guid PurchaseOrderId { get; set; } public Guid ProcessId { get; set; } }
public sealed class ProcessCostRow { public Guid Id { get; set; } public Guid ProcessId { get; set; } public Guid SourceRowId { get; set; } public string SourceColumn { get; set; } = ""; public string Type { get; set; } = ""; public decimal Amount { get; set; } public string Currency { get; set; } = ""; }
public sealed class ImportIssueRow { public Guid Id { get; set; } public Guid BatchId { get; set; } public Guid? SourceRowId { get; set; } public string Severity { get; set; } = ""; public string Code { get; set; } = ""; public string? ColumnName { get; set; } public string EvidenceJson { get; set; } = "{}"; }
public sealed class QualityReviewRow { public Guid Id { get; set; } public Guid BatchId { get; set; } public Guid SourceRowId { get; set; } public string IssueCode { get; set; } = ""; public string Outcome { get; set; } = ""; public string Reviewer { get; set; } = ""; public string Notes { get; set; } = ""; public string? ProposedPurchaseOrder { get; set; } public string? ProposedIpNumber { get; set; } public string RecordedAt { get; set; } = ""; }
public sealed class ErpUserRow { public Guid Id { get; set; } public string Issuer { get; set; } = ""; public string Subject { get; set; } = ""; public string? DisplayName { get; set; } public bool IsActive { get; set; } }
public sealed class ErpUserRoleRow { public Guid UserId { get; set; } public string Role { get; set; } = ""; }
public sealed class ErpUserImporterScopeRow { public Guid UserId { get; set; } public string Importer { get; set; } = ""; }
public sealed class WorkflowStateRow { public string AggregateType { get; set; } = ""; public Guid AggregateId { get; set; } public string CurrentState { get; set; } = ""; public string? PreviousActiveState { get; set; } public long Version { get; set; } }
public sealed class WorkflowTransitionEventRow { public Guid Id { get; set; } public string AggregateType { get; set; } = ""; public Guid AggregateId { get; set; } public string FromState { get; set; } = ""; public string ToState { get; set; } = ""; public string Actor { get; set; } = ""; public string Reason { get; set; } = ""; public string OccurredAt { get; set; } = ""; public string EvidenceJson { get; set; } = "{}"; public long Version { get; set; } }
public sealed class AuditLogRow { public Guid Id { get; set; } public string AggregateType { get; set; } = ""; public Guid AggregateId { get; set; } public string EntityType { get; set; } = ""; public Guid EntityId { get; set; } public string Operation { get; set; } = ""; public string? FieldName { get; set; } public string? OldValueJson { get; set; } public string? NewValueJson { get; set; } public string ActorId { get; set; } = ""; public string OccurredAt { get; set; } = ""; public string? Reason { get; set; } public Guid CorrelationId { get; set; } }
public sealed class OutboxMessageRow { public Guid EventId { get; set; } public string EventType { get; set; } = ""; public string AggregateType { get; set; } = ""; public Guid AggregateId { get; set; } public string PayloadJson { get; set; } = "{}"; public string OccurredAt { get; set; } = ""; public string? PublishedAt { get; set; } public int AttemptCount { get; set; } public string? NextAttemptAt { get; set; } public string? LastError { get; set; } public string? LeaseOwner { get; set; } public string? LeaseExpiresAt { get; set; } public string? DeadLetteredAt { get; set; } }
public sealed class EventInboxMessageRow { public string ConsumerName { get; set; } = ""; public Guid EventId { get; set; } public string Status { get; set; } = "PENDING"; public string? LeaseOwner { get; set; } public string? LeaseExpiresAt { get; set; } public int AttemptCount { get; set; } public string? ProcessedAt { get; set; } public string? LastError { get; set; } }
