using ImportErp.Domain;

namespace ImportErp.Application;

public interface IPurchaseOrderRepository
{
    Task<IReadOnlyList<PurchaseOrder>> ListAsync(CancellationToken cancellationToken);
    Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task SaveAsync(PurchaseOrder purchaseOrder, IReadOnlyList<PurchaseOrderFieldChange> changes,
        string actor, Guid correlationId, CancellationToken cancellationToken);
}

public interface IImportProcessRepository
{
    Task<IReadOnlyDictionary<Guid, ImportProcess>> GetByIdsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken cancellationToken);
}

public sealed class PurchaseOrderService(
    IPurchaseOrderRepository purchaseOrders,
    IImportProcessRepository processes,
    IHistoricalQualityQueueRepository? qualityQueue = null,
    IWorkflowRepository? workflows = null)
{
    public async Task<PurchaseOrderPage> ListAsync(PurchaseOrderFilter filter, CancellationToken cancellationToken)
    {
        var items = await purchaseOrders.ListAsync(cancellationToken);
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        var filteredOrders = items
            .Where(x => Matches(x, filter))
            .OrderBy(x => SortValue(x, filter.SortBy))
            .ThenBy(x => x.ExternalNumber, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var totalCount = filteredOrders.Length;
        var pageOrders = filteredOrders.Skip((page - 1) * pageSize).Take(pageSize).ToArray();
        var issueCounts = await GetOpenIssueCountsAsync(pageOrders, cancellationToken);
        var filtered = pageOrders
            .Select(x => new PurchaseOrderListItem(
                x.Id,
                x.ExternalNumber,
                x.Importer,
                x.IdentityStatus.ToString(),
                OfficialItemsKnown: false,
                x.History.Count,
                x.ProcessLinks.Count,
                x.History.Count(h => !string.IsNullOrWhiteSpace(h.SourceIp) && !IsCancelledIp(h.SourceIp)),
                x.History.Count(h => string.IsNullOrWhiteSpace(h.SourceIp) || IsCancelledIp(h.SourceIp)),
                CountIssuesFor(x, issueCounts),
                BalanceAvailable: false,
                x.Version))
            .ToArray();
        return new PurchaseOrderPage(page, pageSize, totalCount, page * pageSize < totalCount, filtered);
    }

    public async Task<PurchaseOrderOverview?> GetOverviewAsync(Guid id, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var po = await purchaseOrders.GetAsync(id, cancellationToken);
        if (po is null)
        {
            return null;
        }

        var processMap = await processes.GetByIdsAsync(po.ProcessLinks.Select(x => x.ProcessId).ToArray(), cancellationToken);
        var history = po.History
            .OrderBy(x => x.SourceRowNumber)
            .Select(x => ToHistoricalLine(x, asOfDate))
            .ToArray();

        var linkedProcesses = po.ProcessLinks
            .OrderBy(x => x.IpNumber, StringComparer.OrdinalIgnoreCase)
            .Select(link => processMap.TryGetValue(link.ProcessId, out var process)
                ? ToProcessSummary(link, process)
                : new ProcessSummary(link.ProcessId, link.IpNumber, null, link.ResolutionStatus.ToString(), []))
            .ToArray();

        var issueCounts = await GetOpenIssueCountsAsync([po], cancellationToken);
        var unresolvedIssueCount = CountIssuesFor(po, issueCounts);
        var commercialWorkflow = workflows is null ? null
            : await workflows.GetAsync(WorkflowAggregateType.PurchaseOrder, po.Id, cancellationToken);
        return new PurchaseOrderOverview(
            po.Id,
            po.ExternalNumber,
            po.Importer,
            po.IdentityStatus.ToString(),
            commercialWorkflow?.State ?? WorkflowRules.InitialState(WorkflowAggregateType.PurchaseOrder),
            po.Version,
            OfficialItemsKnown: false,
            BalanceAvailable: false,
            unresolvedIssueCount,
            HistoricalItemCount: history.Length,
            LinkedProcessCount: linkedProcesses.Length,
            po.OperationalFields,
            history,
            linkedProcesses,
            new CoverageSummary(
                history.Length,
                history.Count(x => !string.IsNullOrWhiteSpace(x.IpNumber) && !IsCancelledIp(x.IpNumber)),
                history.Count(x => string.IsNullOrWhiteSpace(x.IpNumber) || IsCancelledIp(x.IpNumber)),
                history.Count(x => x.RuptureRisk == RuptureRisk.Rupture),
                history.Count(x => x.RuptureRisk == RuptureRisk.Unknown)));
    }

    public async Task<HistoricalLinePage?> ListHistoricalItemsAsync(
        Guid id, int page, int pageSize, DateOnly asOfDate, CancellationToken cancellationToken)
    {
        var overview = await GetOverviewAsync(id, asOfDate, cancellationToken);
        if (overview is null) return null;

        var safePage = Math.Max(1, page);
        var safePageSize = Math.Clamp(pageSize, 1, 100);
        var lines = overview.HistoricalLines;
        return new HistoricalLinePage(
            safePage, safePageSize, lines.Count, safePage * safePageSize < lines.Count,
            lines.Skip((safePage - 1) * safePageSize).Take(safePageSize).ToArray());
    }

    public async Task<PurchaseOrderOverview> UpdateOperationalFieldsAsync(
        Guid id,
        UpdateOperationalFields command,
        DateOnly asOfDate,
        CancellationToken cancellationToken)
    {
        var po = await purchaseOrders.GetAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("PO não encontrada.");

        var changes = command.Fields.Select(pair =>
        {
            var prior = po.OperationalFields.TryGetValue(pair.Key, out var oldValue) ? oldValue : null;
            var updated = string.IsNullOrWhiteSpace(pair.Value) ? null : pair.Value.Trim();
            return new PurchaseOrderFieldChange(pair.Key.Trim(), prior, updated);
        }).Where(change => !string.Equals(change.OldValue, change.NewValue, StringComparison.Ordinal)).ToArray();
        if (string.IsNullOrWhiteSpace(command.Actor)) throw new DomainValidationException("A identidade do responsável é obrigatória.");
        if (changes.Length == 0)
            return await GetOverviewAsync(id, asOfDate, cancellationToken)
                ?? throw new InvalidOperationException("A PO foi removida durante a consulta.");
        po.SetOperationalFields(command.Fields, command.ExpectedVersion);
        var correlationId = Guid.NewGuid();
        await purchaseOrders.SaveAsync(po, changes, command.Actor, correlationId, cancellationToken);

        return await GetOverviewAsync(id, asOfDate, cancellationToken)
            ?? throw new InvalidOperationException("A PO foi removida durante a atualização.");
    }

    private static bool Matches(PurchaseOrder po, PurchaseOrderFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Number)
            && !po.ExternalNumber.Contains(filter.Number, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.Importer)
            && !string.Equals(po.Importer, filter.Importer, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (filter.AllowedImporters is { } allowed && !allowed.Contains("*") && !allowed.Contains(po.Importer)) return false;

        if (!ContainsAny(po.History, filter.Product, observation => $"{observation.ProductCode} {observation.ProductDescription}")) return false;
        if (!ContainsAny(po.History, filter.IpNumber, observation => observation.SourceIp)) return false;
        if (!ContainsAny(po.History, filter.Supplier, observation => Value(observation.RawValues, "Supplier", "Supplier Name", "Fornecedor"))) return false;
        if (!ContainsAny(po.History, filter.HistoricalStatus, observation => observation.LegacyStatus)) return false;
        if (!string.IsNullOrWhiteSpace(filter.Quality)
            && !string.Equals(po.IdentityStatus.ToString(), filter.Quality.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(filter.FulfillmentStatus) && !MatchesFulfillment(po, filter.FulfillmentStatus)) return false;
        if (filter.NecessityFrom is { } from && !po.History.Any(x => x.NecessityDate.HasValue && x.NecessityDate.Value >= from)) return false;
        if (filter.NecessityTo is { } to && !po.History.Any(x => x.NecessityDate.HasValue && x.NecessityDate.Value <= to)) return false;

        return true;
    }

    private async Task<IReadOnlyDictionary<Guid, int>> GetOpenIssueCountsAsync(
        IReadOnlyCollection<PurchaseOrder> orders, CancellationToken cancellationToken) => qualityQueue is null
        ? new Dictionary<Guid, int>()
        : await qualityQueue.CountOpenBySourceRowIdsAsync(orders.SelectMany(order => order.History).Select(item => item.SourceRowId).Distinct().ToArray(), cancellationToken);

    private static int CountIssuesFor(PurchaseOrder order, IReadOnlyDictionary<Guid, int> issueCounts) =>
        order.History.Sum(item => issueCounts.GetValueOrDefault(item.SourceRowId));
    private static bool ContainsAny(IEnumerable<HistoricalPoObservation> history, string? filter, Func<HistoricalPoObservation, string?> selector) =>
        string.IsNullOrWhiteSpace(filter) || history.Any(item => selector(item)?.Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase) == true);
    private static bool MatchesFulfillment(PurchaseOrder po, string value) => value.Trim().ToUpperInvariant() switch
    {
        "WITH_IP" => po.History.Any(x => !string.IsNullOrWhiteSpace(x.SourceIp) && !IsCancelledIp(x.SourceIp)),
        "WITHOUT_IP" => po.History.Any(x => string.IsNullOrWhiteSpace(x.SourceIp) || IsCancelledIp(x.SourceIp)),
        "ALL_WITH_IP" => po.History.Count > 0 && po.History.All(x => !string.IsNullOrWhiteSpace(x.SourceIp) && !IsCancelledIp(x.SourceIp)),
        _ => false
    };
    private static string SortValue(PurchaseOrder po, string? sortBy) => sortBy?.Trim().ToLowerInvariant() switch
    {
        null or "" or "priority" or "necessity" => po.History.Where(x => x.NecessityDate is not null).Select(x => x.NecessityDate!.Value.ToString("yyyy-MM-dd")).DefaultIfEmpty("9999-12-31").Min()!,
        "number" => po.ExternalNumber,
        "importer" => po.Importer,
        _ => throw new DomainValidationException("Ordenação inválida. Use priority, necessity, number ou importer.")
    };
    private static string? Value(IReadOnlyDictionary<string, string?> values, params string[] names) => names
        .Select(name => values.TryGetValue(name, out var value) ? value : null)
        .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static HistoricalLine ToHistoricalLine(HistoricalPoObservation value, DateOnly asOfDate)
    {
        var estimatedDelivery = ReadDate(value.RawValues, "ETE");
        var risk = KpiCalculator.CalculateRuptureRisk(value.NecessityDate, estimatedDelivery, value.LegacyStatus);
        var timing = KpiCalculator.CalculateTiming(
            ReadDate(value.RawValues, "SC Appr. Date"),
            ReadDate(value.RawValues, "PO Appr. Date"),
            ReadDate(value.RawValues, "PO Sent Date"),
            ReadDate(value.RawValues, "BL Date"),
            ReadDate(value.RawValues, "Arrival"),
            ReadDate(value.RawValues, "Delivery Date"),
            asOfDate);

        return new HistoricalLine(
            value.Id,
            value.SourceRowNumber,
            value.ProductCode,
            value.ProductDescription,
            value.Quantity,
            value.UnitPrice,
            value.HistoricalAmount,
            value.Currency,
            value.NecessityDate,
            value.LegacyStatus,
            value.SourceIp,
            estimatedDelivery,
            risk,
            timing,
            "PrÃ© Embarque",
            value.RawValues);
    }

    private static ProcessSummary ToProcessSummary(ImportProcessLink link, ImportProcess process) => new(
        process.Id,
        process.IpNumber,
        process.LogisticsStatus,
        link.ResolutionStatus.ToString(),
        process.Costs
            .GroupBy(x => new { x.CurrencyCode, x.Type })
            .OrderBy(x => x.Key.CurrencyCode)
            .ThenBy(x => x.Key.Type)
            .Select(x => new ProcessCostSummary(x.Key.Type.ToString(), x.Key.CurrencyCode, x.Sum(cost => cost.Amount)))
            .ToArray());

    private static DateOnly? ReadDate(IReadOnlyDictionary<string, string?> values, string name)
    {
        return values.TryGetValue(name, out var raw) && DateOnly.TryParse(raw, out var value) ? value : null;
    }

    private static bool IsCancelledIp(string? value) => string.Equals(value?.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase);
}

public sealed record PurchaseOrderFilter(
    string? Number, string? Importer, string? Supplier = null, string? Product = null,
    string? IpNumber = null, string? HistoricalStatus = null, string? FulfillmentStatus = null,
    string? Quality = null, DateOnly? NecessityFrom = null, DateOnly? NecessityTo = null,
    string? SortBy = null, int Page = 1, int PageSize = 50,
    IReadOnlySet<string>? AllowedImporters = null);

public sealed record PurchaseOrderPage(int Page, int PageSize, int TotalCount, bool HasNext, IReadOnlyList<PurchaseOrderListItem> Items);
public sealed record HistoricalLinePage(int Page, int PageSize, int TotalCount, bool HasNext, IReadOnlyList<HistoricalLine> Items);

public sealed record UpdateOperationalFields(long ExpectedVersion, IReadOnlyDictionary<string, string?> Fields, string Actor = "system");
public sealed record PurchaseOrderFieldChange(string FieldName, string? OldValue, string? NewValue);

public sealed record PurchaseOrderListItem(
    Guid Id,
    string Number,
    string Importer,
    string IdentityStatus,
    bool OfficialItemsKnown,
    int HistoricalItemCount,
    int LinkedProcessCount,
    int HistoricalItemsWithIp,
    int HistoricalItemsWithoutIp,
    int UnresolvedIssueCount,
    bool BalanceAvailable,
    long Version);

public sealed record PurchaseOrderOverview(
    Guid Id,
    string Number,
    string Importer,
    string IdentityStatus,
    string CommercialStatus,
    long Version,
    bool OfficialItemsKnown,
    bool BalanceAvailable,
    int UnresolvedIssueCount,
    int HistoricalItemCount,
    int LinkedProcessCount,
    IReadOnlyDictionary<string, string?> OperationalFields,
    IReadOnlyList<HistoricalLine> HistoricalLines,
    IReadOnlyList<ProcessSummary> Processes,
    CoverageSummary Coverage);

public sealed record HistoricalLine(
    Guid Id,
    int SourceRowNumber,
    string? ProductCode,
    string? ProductDescription,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? HistoricalAmount,
    string? Currency,
    DateOnly? NecessityDate,
    string? LegacyStatus,
    string? IpNumber,
    DateOnly? EstimatedDeliveryDate,
    RuptureRisk RuptureRisk,
    TimingKpis Timing,
    string SourceSheetName,
    IReadOnlyDictionary<string, string?> SourceValues);

public sealed record ProcessSummary(
    Guid Id,
    string IpNumber,
    string? LogisticsStatus,
    string LinkStatus,
    IReadOnlyList<ProcessCostSummary> Costs);

public sealed record ProcessCostSummary(string Type, string Currency, decimal Amount);

public sealed record CoverageSummary(
    int HistoricalLines,
    int LinesWithIp,
    int LinesWithoutIp,
    int RuptureRiskLines,
    int UnknownRiskLines);
