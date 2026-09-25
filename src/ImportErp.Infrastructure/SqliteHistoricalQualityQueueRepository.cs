using System.Text.Json;
using ImportErp.Application;
using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

/// <summary>SQLite implementation used by the local historical-data review screen.</summary>
public sealed class SqliteHistoricalQualityQueueRepository(IDbContextFactory<SqliteHistoricalDbContext> contextFactory)
    : IHistoricalQualityQueueRepository
{
    private const string MissingPoAndIpCode = "PRE_ROW_WITHOUT_PO_AND_IP";

    public async Task<HistoricalQualityPage> ListAsync(HistoricalQualityFilter filter, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, filter.Page);
        var pageSize = Math.Clamp(filter.PageSize, 1, 100);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var promotedBatchIds = await db.Batches.AsNoTracking()
            .Where(batch => batch.State == "PROMOTED")
            .Select(batch => batch.Id)
            .ToListAsync(cancellationToken);
        var sourceRows = await db.SourceRows.AsNoTracking()
            .Where(row => promotedBatchIds.Contains(row.BatchId))
            .ToListAsync(cancellationToken);
        var sourceById = sourceRows.ToDictionary(row => row.Id);
        var issues = await db.Issues.AsNoTracking()
            .Where(issue => promotedBatchIds.Contains(issue.BatchId))
            .ToListAsync(cancellationToken);
        var reviews = await db.QualityReviews.AsNoTracking()
            .Where(review => promotedBatchIds.Contains(review.BatchId))
            .OrderByDescending(review => review.RecordedAt)
            .ToListAsync(cancellationToken);
        var latestReviews = reviews
            .GroupBy(review => ReviewKey(review.SourceRowId, review.IssueCode), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => ToReview(group.First()), StringComparer.OrdinalIgnoreCase);

        var items = new List<HistoricalQualityItem>();
        foreach (var issue in issues)
        {
            sourceById.TryGetValue(issue.SourceRowId ?? Guid.Empty, out var sourceRow);
            var evidence = ReadEvidence(issue.EvidenceJson);
            var sourceId = issue.SourceRowId ?? Guid.Empty;
            items.Add(new HistoricalQualityItem(
                sourceId,
                issue.Code,
                issue.Severity,
                evidence.Message ?? "Pendência histórica preservada para revisão.",
                issue.ColumnName,
                sourceRow?.SheetName ?? evidence.SheetName,
                sourceRow?.RowNumber ?? evidence.RowNumber,
                sourceRow is null ? EmptyValues : ReadValues(sourceRow.RawValuesJson),
                sourceId == Guid.Empty ? null : latestReviews.GetValueOrDefault(ReviewKey(sourceId, issue.Code))));
        }

        // Older imported batches may predate this explicit queue item. Deriving it
        // from the immutable source prevents those rows from becoming invisible.
        foreach (var sourceRow in sourceRows.Where(IsPreShipmentRowWithoutPoAndIp))
        {
            if (items.Any(item => item.SourceRowId == sourceRow.Id && item.Code == MissingPoAndIpCode))
            {
                continue;
            }

            items.Add(new HistoricalQualityItem(
                sourceRow.Id,
                MissingPoAndIpCode,
                "REVIEW",
                "Linha de Pré Embarque sem PO TOTVS e sem IP válido; requer classificação sem alterar a origem.",
                "PO Totvs",
                sourceRow.SheetName,
                sourceRow.RowNumber,
                ReadValues(sourceRow.RawValuesJson),
                latestReviews.GetValueOrDefault(ReviewKey(sourceRow.Id, MissingPoAndIpCode))));
        }

        var filtered = items
            .Where(item => Matches(item, filter) && MatchesScope(item.SourceValues, filter.AllowedImporters))
            .OrderBy(item => item.SheetName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.SourceRowNumber)
            .ThenBy(item => item.Code, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var openCount = filtered.Count(IsOpen);
        var resolvedCount = filtered.Length - openCount;
        return new HistoricalQualityPage(
            page,
            pageSize,
            filtered.Length,
            openCount,
            resolvedCount,
            filtered.Skip((page - 1) * pageSize).Take(pageSize).ToArray());
    }

    public async Task<HistoricalQualityItem> ReviewAsync(ReviewHistoricalQualityIssue command, CancellationToken cancellationToken)
    {
        if (command.SourceRowId == Guid.Empty || string.IsNullOrWhiteSpace(command.Code))
        {
            throw new ArgumentException("A linha de origem e o código da pendência são obrigatórios.");
        }

        if (string.IsNullOrWhiteSpace(command.Reviewer) || string.IsNullOrWhiteSpace(command.Notes))
        {
            throw new ArgumentException("Informe o responsável e a justificativa da revisão.");
        }

        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var sourceRow = await db.SourceRows.SingleOrDefaultAsync(row => row.Id == command.SourceRowId, cancellationToken)
            ?? throw new KeyNotFoundException("Linha de origem não encontrada.");
        if (!MatchesScope(ReadValues(sourceRow.RawValuesJson), command.AllowedImporters))
        {
            throw new KeyNotFoundException("Pendência não encontrada.");
        }
        var batch = await db.Batches.SingleAsync(value => value.Id == sourceRow.BatchId && value.State == "PROMOTED", cancellationToken);
        var validStoredIssue = await db.Issues.AnyAsync(issue => issue.BatchId == batch.Id
            && issue.SourceRowId == sourceRow.Id && issue.Code == command.Code, cancellationToken);
        if (!validStoredIssue && !(command.Code == MissingPoAndIpCode && IsPreShipmentRowWithoutPoAndIp(sourceRow)))
        {
            throw new KeyNotFoundException("A pendência não pertence à linha de origem informada.");
        }

        var row = new QualityReviewRow
        {
            Id = Guid.NewGuid(),
            BatchId = batch.Id,
            SourceRowId = sourceRow.Id,
            IssueCode = command.Code.Trim(),
            Outcome = command.Outcome.ToString().ToUpperInvariant(),
            Reviewer = command.Reviewer.Trim(),
            Notes = command.Notes.Trim(),
            ProposedPurchaseOrder = TrimOrNull(command.ProposedPurchaseOrder),
            ProposedIpNumber = TrimOrNull(command.ProposedIpNumber),
            RecordedAt = DateTimeOffset.UtcNow.ToString("O")
        };
        db.QualityReviews.Add(row);
        var occurredAt = DateTimeOffset.UtcNow.ToString("O");
        db.AuditLog.Add(new AuditLogRow
        {
            Id = Guid.NewGuid(), AggregateType = "DATA_ISSUE", AggregateId = sourceRow.Id,
            EntityType = "DataIssue", EntityId = sourceRow.Id, Operation = "REVIEW",
            FieldName = row.IssueCode, OldValueJson = null,
            NewValueJson = JsonSerializer.Serialize(new { row.Outcome, row.ProposedPurchaseOrder, row.ProposedIpNumber }),
            ActorId = row.Reviewer, OccurredAt = occurredAt, Reason = row.Notes, CorrelationId = row.Id
        });
        db.OutboxMessages.Add(new OutboxMessageRow
        {
            EventId = row.Id, EventType = "DataIssueReviewed", AggregateType = "DATA_ISSUE",
            AggregateId = sourceRow.Id,
            PayloadJson = JsonSerializer.Serialize(new
            {
                sourceRowId = sourceRow.Id, issueCode = row.IssueCode, outcome = row.Outcome,
                actorId = row.Reviewer, notes = row.Notes, occurredAt
            }), OccurredAt = occurredAt
        });
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new HistoricalQualityItem(
            sourceRow.Id,
            row.IssueCode,
            "REVIEW",
            "Revisão registrada sem alterar os valores históricos da origem.",
            null,
            sourceRow.SheetName,
            sourceRow.RowNumber,
            ReadValues(sourceRow.RawValuesJson),
            ToReview(row));
    }

    public async Task<IReadOnlyDictionary<Guid, int>> CountOpenBySourceRowIdsAsync(
        IReadOnlyCollection<Guid> sourceRowIds,
        CancellationToken cancellationToken)
    {
        if (sourceRowIds.Count == 0)
        {
            return new Dictionary<Guid, int>();
        }

        var requestedIds = sourceRowIds.Distinct().ToArray();
        var openCounts = new Dictionary<Guid, int>();
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        foreach (var idChunk in requestedIds.Chunk(400))
        {
            var issues = await db.Issues.AsNoTracking()
                .Where(issue => issue.SourceRowId.HasValue && idChunk.Contains(issue.SourceRowId.Value))
                .Select(issue => new { SourceRowId = issue.SourceRowId!.Value, issue.Code })
                .ToListAsync(cancellationToken);
            if (issues.Count == 0) continue;

            var sourceIds = issues.Select(issue => issue.SourceRowId).Distinct().ToArray();
            var codes = issues.Select(issue => issue.Code).Distinct().ToArray();
            var latestReviews = await db.QualityReviews.AsNoTracking()
                .Where(review => sourceIds.Contains(review.SourceRowId) && codes.Contains(review.IssueCode))
                .OrderByDescending(review => review.RecordedAt)
                .Select(review => new { review.SourceRowId, review.IssueCode, review.Outcome })
                .ToListAsync(cancellationToken);
            var latestByIssue = latestReviews
                .GroupBy(review => ReviewKey(review.SourceRowId, review.IssueCode), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().Outcome, StringComparer.OrdinalIgnoreCase);

            foreach (var issue in issues)
            {
                var reviewKey = ReviewKey(issue.SourceRowId, issue.Code);
                if (!latestByIssue.TryGetValue(reviewKey, out var outcome)
                    || string.Equals(outcome, "ESCALATED", StringComparison.OrdinalIgnoreCase))
                {
                    openCounts[issue.SourceRowId] = openCounts.GetValueOrDefault(issue.SourceRowId) + 1;
                }
            }
        }

        return openCounts;
    }

    private static bool Matches(HistoricalQualityItem item, HistoricalQualityFilter filter)
    {
        if (!string.IsNullOrWhiteSpace(filter.Code) && !item.Code.Contains(filter.Code.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrWhiteSpace(filter.SheetName) && !string.Equals(item.SheetName, filter.SheetName.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
        return string.IsNullOrWhiteSpace(filter.Status)
            || filter.Status.Equals("all", StringComparison.OrdinalIgnoreCase)
            || (filter.Status.Equals("open", StringComparison.OrdinalIgnoreCase) && IsOpen(item))
            || (filter.Status.Equals("resolved", StringComparison.OrdinalIgnoreCase) && !IsOpen(item));
    }
    private static bool MatchesScope(IReadOnlyDictionary<string, string?> values, IReadOnlySet<string>? allowedImporters)
    {
        if (allowedImporters is null || allowedImporters.Contains("*")) return true;
        var importer = Value(values, "Importer", "Importador");
        return importer is not null && allowedImporters.Contains(importer);
    }

    private static bool IsOpen(HistoricalQualityItem item) => item.LatestReview is null
        || string.Equals(item.LatestReview.Outcome, HistoricalQualityReviewOutcome.Escalated.ToString(), StringComparison.OrdinalIgnoreCase);
    private static bool IsPreShipmentRowWithoutPoAndIp(SourceRow row)
    {
        if (!string.Equals(row.SheetName, "Pré Embarque", StringComparison.OrdinalIgnoreCase)) return false;
        var values = ReadValues(row.RawValuesJson);
        var po = Value(values, "PO Totvs");
        var ip = Value(values, "IP Number");
        return po is null && (ip is null || string.Equals(ip, "CANCELLED", StringComparison.OrdinalIgnoreCase));
    }

    private static HistoricalQualityReview ToReview(QualityReviewRow row) => new(
        row.Id, row.Outcome, row.Reviewer, row.Notes, row.ProposedPurchaseOrder, row.ProposedIpNumber,
        DateTimeOffset.TryParse(row.RecordedAt, out var recordedAt) ? recordedAt : DateTimeOffset.MinValue);
    private static WorkbookIssue ReadEvidence(string json) => JsonSerializer.Deserialize<WorkbookIssue>(json) ?? new WorkbookIssue("REVIEW", "UNKNOWN", "Pendência histórica preservada.");
    private static Dictionary<string, string?> ReadValues(string json) => JsonSerializer.Deserialize<Dictionary<string, string?>>(json) ?? new(StringComparer.OrdinalIgnoreCase);
    private static string? Value(IReadOnlyDictionary<string, string?> values, params string[] fields) => fields
        .Select(field => values.TryGetValue(field, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null)
        .FirstOrDefault(value => value is not null);
    private static string? TrimOrNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static string ReviewKey(Guid sourceRowId, string issueCode) => $"{sourceRowId:N}|{issueCode}";
    private static readonly IReadOnlyDictionary<string, string?> EmptyValues = new Dictionary<string, string?>();
}
