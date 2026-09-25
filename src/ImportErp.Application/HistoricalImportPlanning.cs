using System.Globalization;

namespace ImportErp.Application;

/// <summary>
/// Creates a deterministic, read-only promotion plan. It never recalculates an Excel formula.
/// </summary>
public sealed class HistoricalImportPlanner(IHistoricalWorkbookExtractor extractor)
{
    private const string PreShipment = "Pré Embarque";
    private const string PostShipment = "Pós Embarque";

    public async Task<HistoricalPromotionPlan> CreatePlanAsync(string workbookPath, CancellationToken cancellationToken)
    {
        var extraction = await extractor.ExtractAsync(workbookPath, cancellationToken);
        var pre = extraction.Sheets.Single(sheet => string.Equals(sheet.Name, PreShipment, StringComparison.OrdinalIgnoreCase));
        var post = extraction.Sheets.Single(sheet => string.Equals(sheet.Name, PostShipment, StringComparison.OrdinalIgnoreCase));

        var purchaseOrders = new HashSet<PurchaseOrderIdentity>(PurchaseOrderIdentityComparer.Instance);
        var preProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var postProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var poIpLinks = new HashSet<PurchaseOrderProcessLink>(PurchaseOrderProcessLinkComparer.Instance);
        var issues = new List<WorkbookIssue>();
        var preRowsWithPo = 0;
        var preRowsWithIp = 0;
        var preRowsWithoutIp = 0;
        var rowsWithoutPo = 0;

        foreach (var row in pre.Rows)
        {
            AddMixedValueIssues(row, issues);
            var po = Value(row, "PO Totvs");
            var importer = Value(row, "Importer");
            var ip = ValidIp(Value(row, "IP Number"));
            if (po is null)
            {
                rowsWithoutPo++;
            }
            else
            {
                preRowsWithPo++;
                purchaseOrders.Add(new PurchaseOrderIdentity(importer ?? "UNSPECIFIED", po));
            }

            if (ip is null)
            {
                preRowsWithoutIp++;
            }
            else
            {
                preRowsWithIp++;
                preProcesses.Add(ip);
                if (po is not null)
                {
                    poIpLinks.Add(new PurchaseOrderProcessLink(importer ?? "UNSPECIFIED", po, ip));
                }
                else
                {
                    issues.Add(new WorkbookIssue("REVIEW", "IP_WITHOUT_PO", "Linha de Pré Embarque possui IP, mas não possui PO TOTVS.", row.SheetName, row.RowNumber, "PO Totvs"));
                }
            }

            foreach (var column in row.ErrorColumns)
            {
                issues.Add(new WorkbookIssue("REVIEW", "EXCEL_ERROR", "Valor de erro do Excel preservado na origem.", row.SheetName, row.RowNumber, column));
            }
        }

        var costs = new List<PlannedProcessCost>();
        foreach (var row in post.Rows)
        {
            AddMixedValueIssues(row, issues);
            var ip = ValidIp(Value(row, "IP Number"));
            if (ip is null)
            {
                issues.Add(new WorkbookIssue("REVIEW", "POST_ROW_WITHOUT_IP", "Linha de Pós Embarque não cria processo sem IP.", row.SheetName, row.RowNumber, "IP Number"));
                continue;
            }

            postProcesses.Add(ip);
            AddCost(costs, issues, row, ip, "Freight", "Freight Cost", "W", Value(row, "Freight Ccy."));
            AddCost(costs, issues, row, ip, "TaxesPaid", "Taxes Paid (R$)", "AE", "BRL");
            AddCost(costs, issues, row, ip, "Fines", "Fines R$", "AP", "BRL");
            AddCost(costs, issues, row, ip, "Storage", "Storage R$", "AQ", "BRL");
            AddCost(costs, issues, row, ip, "Demurrage", "Demurrage R$", "AR", "BRL");
        }

        foreach (var ip in postProcesses.Except(preProcesses, StringComparer.OrdinalIgnoreCase))
        {
            issues.Add(new WorkbookIssue("REVIEW", "POST_IP_NOT_IN_PRE", $"IP {ip} existe apenas no Pós Embarque; será promovido sem vínculo inventado de PO.", PostShipment));
        }

        return new HistoricalPromotionPlan(
            extraction.FileName,
            extraction.Sha256,
            "historical-workbook-v1",
            pre.Rows.Count + post.Rows.Count,
            purchaseOrders.Count,
            preProcesses.Union(postProcesses, StringComparer.OrdinalIgnoreCase).Count(),
            preRowsWithPo,
            preRowsWithIp,
            preRowsWithoutIp,
            rowsWithoutPo,
            poIpLinks.Count,
            costs,
            issues);
    }

    private static void AddCost(
        ICollection<PlannedProcessCost> costs,
        ICollection<WorkbookIssue> issues,
        ExtractedSourceRow row,
        string ip,
        string type,
        string amountField,
        string sourceColumn,
        string? currency)
    {
        var raw = Value(row, amountField);
        if (raw is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(currency))
        {
            issues.Add(new WorkbookIssue("REVIEW", "COST_CURRENCY_MISSING", $"{amountField} possui valor sem moeda.", row.SheetName, row.RowNumber, amountField));
            return;
        }

        if (!TryParseDecimal(raw, out var amount))
        {
            issues.Add(new WorkbookIssue("REVIEW", "COST_NOT_NUMERIC", $"{amountField} não pôde ser convertido para decimal sem perda.", row.SheetName, row.RowNumber, amountField));
            return;
        }

        costs.Add(new PlannedProcessCost(ip, row.SheetName, row.RowNumber, amountField, sourceColumn, type, amount, currency.Trim().ToUpperInvariant()));
    }

    private static string? Value(ExtractedSourceRow row, string field) => row.Values.TryGetValue(field, out var value)
        ? HistoricalValueNormalizer.Normalize(field, value)
        : null;

    private static void AddMixedValueIssues(ExtractedSourceRow row, ICollection<WorkbookIssue> issues)
    {
        foreach (var (field, rawValue) in row.Values)
        {
            if (rawValue is not null && HistoricalValueNormalizer.ContainsMultipleValues(field, rawValue))
            {
                issues.Add(new WorkbookIssue(
                    "REVIEW",
                    "MIXED_SCALAR_VALUES",
                    $"O campo {field} contém mais de um valor ou separador ambíguo. O valor original foi preservado e não será usado para vincular entidades.",
                    row.SheetName,
                    row.RowNumber,
                    field));
            }
        }
    }

    private static string? ValidIp(string? value) => string.IsNullOrWhiteSpace(value)
        || string.Equals(value, "CANCELLED", StringComparison.OrdinalIgnoreCase)
        ? null
        : value.Trim();

    private static bool TryParseDecimal(string value, out decimal result)
    {
        var trimmed = value.Trim();
        var commaIsDecimalSeparator = trimmed.Contains(',')
            && (!trimmed.Contains('.') || trimmed.LastIndexOf(',') > trimmed.LastIndexOf('.'));
        var primaryCulture = commaIsDecimalSeparator ? CultureInfo.GetCultureInfo("pt-BR") : CultureInfo.InvariantCulture;
        var secondaryCulture = commaIsDecimalSeparator ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo("pt-BR");
        return decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowExponent, primaryCulture, out result)
            || decimal.TryParse(trimmed, NumberStyles.Number | NumberStyles.AllowExponent, secondaryCulture, out result);
    }
}

public sealed record HistoricalPromotionPlan(
    string FileName,
    string FileSha256,
    string MappingVersion,
    int SourceRowCount,
    int PurchaseOrderCount,
    int ImportProcessCount,
    int PreRowsWithPo,
    int PreRowsWithIp,
    int PreRowsWithoutIp,
    int RowsWithoutPo,
    int PurchaseOrderProcessLinkCount,
    IReadOnlyList<PlannedProcessCost> Costs,
    IReadOnlyList<WorkbookIssue> Issues);

public sealed record PlannedProcessCost(
    string IpNumber,
    string SheetName,
    int SourceRowNumber,
    string SourceField,
    string SourceColumn,
    string Type,
    decimal Amount,
    string Currency);

public sealed record PurchaseOrderIdentity(string Importer, string Number);
public sealed record PurchaseOrderProcessLink(string Importer, string PurchaseOrderNumber, string IpNumber);

public interface IHistoricalStagingStore
{
    Task<StagingResult> StageAsync(
        WorkbookExtraction extraction,
        HistoricalPromotionPlan plan,
        CancellationToken cancellationToken);
}

public sealed record StagingResult(
    Guid BatchId,
    bool ExistingBatch,
    int InsertedSourceRows,
    int ExistingSourceRows,
    string State);

public interface IHistoricalPromoter
{
    Task<PromotionResult> PromoteAsync(
        WorkbookExtraction extraction,
        HistoricalPromotionPlan plan,
        Guid batchId,
        CancellationToken cancellationToken);
}

public sealed record PromotionResult(
    Guid BatchId,
    int PurchaseOrdersPromoted,
    int HistoricalObservationsPromoted,
    int ImportProcessesPromoted,
    int PurchaseOrderProcessLinksPromoted,
    int CostsPromoted,
    int IssuesPromoted,
    string State);

internal sealed class PurchaseOrderIdentityComparer : IEqualityComparer<PurchaseOrderIdentity>
{
    public static PurchaseOrderIdentityComparer Instance { get; } = new();
    public bool Equals(PurchaseOrderIdentity? x, PurchaseOrderIdentity? y) => x is not null && y is not null
        && string.Equals(x.Importer, y.Importer, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.Number, y.Number, StringComparison.OrdinalIgnoreCase);
    public int GetHashCode(PurchaseOrderIdentity obj) => HashCode.Combine(obj.Importer.ToUpperInvariant(), obj.Number.ToUpperInvariant());
}

internal sealed class PurchaseOrderProcessLinkComparer : IEqualityComparer<PurchaseOrderProcessLink>
{
    public static PurchaseOrderProcessLinkComparer Instance { get; } = new();
    public bool Equals(PurchaseOrderProcessLink? x, PurchaseOrderProcessLink? y) => x is not null && y is not null
        && string.Equals(x.Importer, y.Importer, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.PurchaseOrderNumber, y.PurchaseOrderNumber, StringComparison.OrdinalIgnoreCase)
        && string.Equals(x.IpNumber, y.IpNumber, StringComparison.OrdinalIgnoreCase);
    public int GetHashCode(PurchaseOrderProcessLink obj) => HashCode.Combine(obj.Importer.ToUpperInvariant(), obj.PurchaseOrderNumber.ToUpperInvariant(), obj.IpNumber.ToUpperInvariant());
}
