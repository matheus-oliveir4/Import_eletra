namespace ImportErp.Application;

/// <summary>
/// Reconciles the historical sheets at the process grain. Monetary values remain
/// grouped by currency and are never joined to PO observations before summing.
/// </summary>
public sealed class HistoricalReconciliationService
{
    public HistoricalReconciliationReport Reconcile(WorkbookExtraction extraction, HistoricalPromotionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(plan);

        var pre = extraction.Sheets.Single(sheet => sheet.HeaderColumns.Values.Any(header => string.Equals(header, "PO Totvs", StringComparison.OrdinalIgnoreCase)));
        var post = extraction.Sheets.Single(sheet => !ReferenceEquals(sheet, pre));
        var preByIp = GroupRowsByIp(pre.Rows);
        var postByIp = GroupRowsByIp(post.Rows);
        var linksByIp = plan.Issues
            .Where(issue => string.Equals(issue.Code, "IP_WITHOUT_PO", StringComparison.OrdinalIgnoreCase))
            .Select(issue => issue.RowNumber)
            .ToHashSet();

        var processes = preByIp.Keys.Union(postByIp.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(ip => ip, StringComparer.OrdinalIgnoreCase)
            .Select(ip => new ProcessReconciliation(
                ip,
                preByIp.GetValueOrDefault(ip, 0),
                postByIp.GetValueOrDefault(ip, 0),
                preByIp.ContainsKey(ip),
                postByIp.ContainsKey(ip)))
            .ToArray();

        var costs = plan.Costs
            .GroupBy(cost => cost.Currency, StringComparer.OrdinalIgnoreCase)
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => new CurrencyReconciliation(group.Key, group.Count(), group.Sum(cost => cost.Amount)))
            .ToArray();

        return new HistoricalReconciliationReport(
            processes,
            costs,
            pre.Rows.Count,
            post.Rows.Count,
            linksByIp.Count);
    }

    private static Dictionary<string, int> GroupRowsByIp(IEnumerable<ExtractedSourceRow> rows)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            if (!row.Values.TryGetValue("IP Number", out var raw) || string.IsNullOrWhiteSpace(raw) || string.Equals(raw.Trim(), "CANCELLED", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ip = raw.Trim();
            result[ip] = result.GetValueOrDefault(ip) + 1;
        }

        return result;
    }
}

public sealed record HistoricalReconciliationReport(
    IReadOnlyList<ProcessReconciliation> Processes,
    IReadOnlyList<CurrencyReconciliation> CostsByCurrency,
    int PreSourceRows,
    int PostSourceRows,
    int PreRowsWithIpWithoutPo);

public sealed record ProcessReconciliation(
    string IpNumber,
    int PreRowCount,
    int PostRowCount,
    bool ExistsInPre,
    bool ExistsInPost);

public sealed record CurrencyReconciliation(string Currency, int CostCount, decimal Amount);
