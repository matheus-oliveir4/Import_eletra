namespace ImportErp.Application;

/// <summary>
/// Read model for the historical-data review queue. Source values remain immutable;
/// a review stores only the decision and any proposed operational reference.
/// </summary>
public interface IHistoricalQualityQueueRepository
{
    Task<HistoricalQualityPage> ListAsync(HistoricalQualityFilter filter, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, int>> CountOpenBySourceRowIdsAsync(
        IReadOnlyCollection<Guid> sourceRowIds,
        CancellationToken cancellationToken);
    Task<HistoricalQualityItem> ReviewAsync(ReviewHistoricalQualityIssue command, CancellationToken cancellationToken);
}

public sealed record HistoricalQualityFilter(
    string? Status,
    string? Code,
    string? SheetName,
    int Page = 1,
    int PageSize = 50,
    IReadOnlySet<string>? AllowedImporters = null);

public sealed record HistoricalQualityPage(
    int Page,
    int PageSize,
    int TotalCount,
    int OpenCount,
    int ResolvedCount,
    IReadOnlyList<HistoricalQualityItem> Items);

public sealed record HistoricalQualityItem(
    Guid SourceRowId,
    string Code,
    string Severity,
    string Message,
    string? ColumnName,
    string? SheetName,
    int? SourceRowNumber,
    IReadOnlyDictionary<string, string?> SourceValues,
    HistoricalQualityReview? LatestReview);

public sealed record HistoricalQualityReview(
    Guid Id,
    string Outcome,
    string Reviewer,
    string Notes,
    string? ProposedPurchaseOrder,
    string? ProposedIpNumber,
    DateTimeOffset RecordedAt);

public sealed record ReviewHistoricalQualityIssue(
    Guid SourceRowId,
    string Code,
    HistoricalQualityReviewOutcome Outcome,
    string Reviewer,
    string Notes,
    string? ProposedPurchaseOrder,
    string? ProposedIpNumber,
    IReadOnlySet<string>? AllowedImporters = null);

public enum HistoricalQualityReviewOutcome
{
    Resolved,
    Escalated
}
