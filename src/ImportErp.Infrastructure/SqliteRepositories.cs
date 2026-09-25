using System.Globalization;
using System.Text.Json;
using ImportErp.Application;
using ImportErp.Domain;
using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

/// <summary>Reads the test database back into the PO-centric application model.</summary>
public sealed class SqlitePurchaseOrderRepository(IDbContextFactory<SqliteHistoricalDbContext> contextFactory) : IPurchaseOrderRepository
{
    public async Task<IReadOnlyList<PurchaseOrder>> ListAsync(CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.PurchaseOrders.AsNoTracking().OrderBy(value => value.Importer).ThenBy(value => value.ExternalNumber).ToListAsync(cancellationToken);
        var observations = await db.Observations.AsNoTracking().ToListAsync(cancellationToken);
        var links = await db.ProcessPurchaseOrders.AsNoTracking().ToListAsync(cancellationToken);
        var processNumbers = await db.ImportProcesses.AsNoTracking().ToDictionaryAsync(value => value.Id, value => value.IpNumber, cancellationToken);
        return rows.Select(row => Hydrate(row, observations.Where(value => value.PurchaseOrderId == row.Id), links.Where(value => value.PurchaseOrderId == row.Id), processNumbers)).ToArray();
    }

    public async Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var row = await db.PurchaseOrders.AsNoTracking().SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (row is null)
        {
            return null;
        }

        var observations = await db.Observations.AsNoTracking().Where(value => value.PurchaseOrderId == id).ToListAsync(cancellationToken);
        var links = await db.ProcessPurchaseOrders.AsNoTracking().Where(value => value.PurchaseOrderId == id).ToListAsync(cancellationToken);
        var processIds = links.Select(value => value.ProcessId).ToArray();
        var processNumbers = await db.ImportProcesses.AsNoTracking()
            .Where(value => processIds.Contains(value.Id))
            .ToDictionaryAsync(value => value.Id, value => value.IpNumber, cancellationToken);
        return Hydrate(row, observations, links, processNumbers);
    }

    public async Task SaveAsync(PurchaseOrder purchaseOrder, IReadOnlyList<PurchaseOrderFieldChange> changes,
        string actor, Guid correlationId, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var priorVersion = purchaseOrder.Version - 1;
        var updated = await db.PurchaseOrders
            .Where(value => value.Id == purchaseOrder.Id && value.Version == priorVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(value => value.OperationalFieldsJson, JsonSerializer.Serialize(purchaseOrder.OperationalFields))
                .SetProperty(value => value.Version, purchaseOrder.Version), cancellationToken);
        if (updated != 1)
        {
            throw new ConcurrencyException(purchaseOrder.Id, priorVersion, priorVersion + 1);
        }

        var occurredAt = DateTimeOffset.UtcNow.ToString("O");
        db.AuditLog.AddRange(changes.Select(change => new AuditLogRow
        {
            Id = Guid.NewGuid(),
            AggregateType = "PURCHASE_ORDER",
            AggregateId = purchaseOrder.Id,
            EntityType = "PurchaseOrder",
            EntityId = purchaseOrder.Id,
            Operation = "UPDATE",
            FieldName = change.FieldName,
            OldValueJson = change.OldValue is null ? null : JsonSerializer.Serialize(change.OldValue),
            NewValueJson = change.NewValue is null ? null : JsonSerializer.Serialize(change.NewValue),
            ActorId = actor,
            OccurredAt = occurredAt,
            CorrelationId = correlationId
        }));
        db.OutboxMessages.Add(new OutboxMessageRow
        {
            EventId = correlationId,
            EventType = "PurchaseOrderOperationalFieldsChanged",
            AggregateType = "PURCHASE_ORDER",
            AggregateId = purchaseOrder.Id,
            PayloadJson = JsonSerializer.Serialize(new { purchaseOrderId = purchaseOrder.Id, version = purchaseOrder.Version, changes }),
            OccurredAt = occurredAt
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static PurchaseOrder Hydrate(
        PurchaseOrderRow row,
        IEnumerable<ObservationRow> observations,
        IEnumerable<ProcessPurchaseOrderRow> links,
        IReadOnlyDictionary<Guid, string> processNumbers)
    {
        var purchaseOrder = new PurchaseOrder(row.Id, row.Importer, row.ExternalNumber);
        purchaseOrder.RestoreOperationalState(ReadDictionary(row.OperationalFieldsJson), row.Version);
        foreach (var observation in observations.OrderBy(value => value.SourceRowNumber))
        {
            var values = ReadDictionary(observation.RawValuesJson);
            purchaseOrder.AddHistoricalObservation(new HistoricalPoObservation(
                observation.Id,
                row.Id,
                observation.SourceRowId,
                observation.SourceRowNumber,
                Value(values, "Product Code"),
                Value(values, "Product Description"),
                DecimalValue(values, "Qty"),
                DecimalValue(values, "Unit Price"),
                DecimalValue(values, "Total Price"),
                Value(values, "Currency"),
                DateValue(values, "Necessity"),
                Value(values, "Status"),
                Value(values, "IP Number"),
                values));
        }

        foreach (var link in links)
        {
            purchaseOrder.LinkProcess(new ImportProcessLink(
                row.Id,
                link.ProcessId,
                processNumbers.TryGetValue(link.ProcessId, out var number) ? number : "IP indisponÃ­vel",
                LinkResolutionStatus.Historical));
        }

        return purchaseOrder;
    }

    private static Dictionary<string, string?> ReadDictionary(string json) => JsonSerializer.Deserialize<Dictionary<string, string?>>(json)
        ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    private static string? Value(IReadOnlyDictionary<string, string?> values, string field) => values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
    private static decimal? DecimalValue(IReadOnlyDictionary<string, string?> values, string field) => Value(values, field) is { } raw
        && (decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var invariant)
            || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out invariant)) ? invariant : null;
    private static DateOnly? DateValue(IReadOnlyDictionary<string, string?> values, string field)
    {
        var raw = Value(values, field);
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var date)) return date;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
            : null;
    }
}

public sealed class SqliteImportProcessRepository(IDbContextFactory<SqliteHistoricalDbContext> contextFactory) : IImportProcessRepository
{
    public async Task<IReadOnlyDictionary<Guid, ImportProcess>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return new Dictionary<Guid, ImportProcess>();
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var rows = await db.ImportProcesses.AsNoTracking().Where(value => ids.Contains(value.Id)).ToListAsync(cancellationToken);
        var costs = await db.ProcessCosts.AsNoTracking().Where(value => ids.Contains(value.ProcessId)).ToListAsync(cancellationToken);
        var workflowStates = await db.WorkflowStates.AsNoTracking()
            .Where(value => value.AggregateType == "IMPORT_PROCESS" && ids.Contains(value.AggregateId))
            .ToDictionaryAsync(value => value.AggregateId, value => value.CurrentState, cancellationToken);
        return rows.ToDictionary(row => row.Id, row => Hydrate(row, costs.Where(cost => cost.ProcessId == row.Id), workflowStates));
    }

    private static ImportProcess Hydrate(ImportProcessRow row, IEnumerable<ProcessCostRow> costs, IReadOnlyDictionary<Guid, string> workflowStates)
    {
        var process = new ImportProcess(row.Id, row.Importer ?? "UNSPECIFIED", row.IpNumber);
        process.SetLogisticsStatus(workflowStates.TryGetValue(row.Id, out var state)
            ? state
            : WorkflowRules.InitialState(WorkflowAggregateType.ImportProcess, row.LogisticsStatus));
        foreach (var cost in costs)
        {
            if (Enum.TryParse<ProcessCostType>(cost.Type, true, out var type))
            {
                process.AddCost(new ProcessCost(cost.Id, row.Id, cost.SourceRowId, cost.SourceColumn, type, cost.Amount, cost.Currency));
            }
        }

        return process;
    }
}
