using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImportErp.Application;
using ImportErp.Domain;
using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

/// <summary>
/// Development-only SQLite implementation. Source rows are staged first and never
/// recalculated; promotion builds the PO/IP view from that immutable evidence.
/// </summary>
public sealed class SqliteHistoricalStore(
    IDbContextFactory<SqliteHistoricalDbContext> contextFactory,
    SqliteMigrationRunner migrationRunner,
    IPromotionTransactionProbe? transactionProbe = null)
    : IHistoricalStagingStore, IHistoricalPromoter
{
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        await migrationRunner.MigrateAsync(cancellationToken);
    }

    public async Task<StagingResult> StageAsync(
        WorkbookExtraction extraction,
        HistoricalPromotionPlan plan,
        CancellationToken cancellationToken)
    {
        Validate(extraction, plan);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var batch = await db.Batches.SingleOrDefaultAsync(
            value => value.FileSha256 == extraction.Sha256 && value.MappingVersion == plan.MappingVersion,
            cancellationToken);
        var existingBatch = batch is not null;
        if (batch is null)
        {
            batch = new BatchRow
            {
                Id = Guid.NewGuid(),
                FileName = extraction.FileName,
                FileSha256 = extraction.Sha256,
                MappingVersion = plan.MappingVersion,
                State = "RECEIVED"
            };
            db.Batches.Add(batch);
            await db.SaveChangesAsync(cancellationToken);
        }

        var existingKeys = (await db.SourceRows
                .Where(value => value.BatchId == batch.Id)
                .Select(value => new { value.SheetName, value.RowNumber })
                .ToListAsync(cancellationToken))
            .Select(value => SourceKey(value.SheetName, value.RowNumber))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var inserted = 0;
        var existing = 0;
        foreach (var row in extraction.Sheets.SelectMany(sheet => sheet.Rows))
        {
            if (!existingKeys.Add(SourceKey(row.SheetName, row.RowNumber)))
            {
                existing++;
                continue;
            }

            var rawValues = JsonSerializer.Serialize(row.Values);
            db.SourceRows.Add(new SourceRow
            {
                Id = Guid.NewGuid(),
                BatchId = batch.Id,
                SheetName = row.SheetName,
                RowNumber = row.RowNumber,
                RawValuesJson = rawValues,
                ErrorColumnsJson = JsonSerializer.Serialize(row.ErrorColumns),
                RowHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawValues)))
            });
            inserted++;
        }

        batch.State = "STAGED";
        batch.SourceRowCount = plan.SourceRowCount;
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StagingResult(batch.Id, existingBatch, inserted, existing, batch.State);
    }

    public async Task<PromotionResult> PromoteAsync(
        WorkbookExtraction extraction,
        HistoricalPromotionPlan plan,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        Validate(extraction, plan);
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var batch = await db.Batches.SingleOrDefaultAsync(value => value.Id == batchId, cancellationToken)
            ?? throw new InvalidOperationException("Lote de staging nÃ£o encontrado.");
        var sourceRows = await db.SourceRows.Where(value => value.BatchId == batchId).ToListAsync(cancellationToken);
        if (sourceRows.Count != plan.SourceRowCount)
        {
            throw new InvalidOperationException("O lote nÃ£o estÃ¡ completamente em staging; a promoÃ§Ã£o foi interrompida.");
        }

        var sourceIds = sourceRows.ToDictionary(value => SourceKey(value.SheetName, value.RowNumber), value => value.Id, StringComparer.OrdinalIgnoreCase);
        var pre = extraction.Sheets.Single(sheet => string.Equals(sheet.Name, "Pr\u00e9 Embarque", StringComparison.OrdinalIgnoreCase));
        var post = extraction.Sheets.Single(sheet => string.Equals(sheet.Name, "P\u00f3s Embarque", StringComparison.OrdinalIgnoreCase));
        var existingPurchaseOrders = await db.PurchaseOrders.ToListAsync(cancellationToken);
        var poIds = existingPurchaseOrders.ToDictionary(value => PurchaseOrderKey(value.Importer, value.ExternalNumber), value => value.Id, StringComparer.OrdinalIgnoreCase);
        var newPos = 0;
        var newObservations = 0;

        var observedSourceIds = (await db.Observations.Select(value => value.SourceRowId).ToListAsync(cancellationToken)).ToHashSet();
        foreach (var row in pre.Rows)
        {
            var number = Value(row, "PO Totvs");
            if (number is null)
            {
                continue;
            }

            var importer = Value(row, "Importer") ?? "UNSPECIFIED";
            var poKey = PurchaseOrderKey(importer, number);
            if (!poIds.TryGetValue(poKey, out var purchaseOrderId))
            {
                purchaseOrderId = Guid.NewGuid();
                poIds[poKey] = purchaseOrderId;
                db.PurchaseOrders.Add(new PurchaseOrderRow
                {
                    Id = purchaseOrderId,
                    Importer = importer,
                    ExternalNumber = number,
                    NormalizedNumber = number.ToUpperInvariant(),
                    OperationalFieldsJson = "{}"
                });
                AddWorkflowSeed(db, WorkflowAggregateType.PurchaseOrder, purchaseOrderId,
                    WorkflowRules.InitialState(WorkflowAggregateType.PurchaseOrder), batchId,
                    sourceIds[SourceKey(row)], row.RowNumber, "Pré Embarque");
                newPos++;
            }

            var sourceId = sourceIds[SourceKey(row)];
            if (observedSourceIds.Add(sourceId))
            {
                db.Observations.Add(new ObservationRow
                {
                    Id = Guid.NewGuid(),
                    PurchaseOrderId = purchaseOrderId,
                    SourceRowId = sourceId,
                    SourceRowNumber = row.RowNumber,
                    RawValuesJson = JsonSerializer.Serialize(row.Values)
                });
                newObservations++;
            }
        }

        var candidates = new Dictionary<string, (string? Importer, string? Status)>(StringComparer.OrdinalIgnoreCase);
        AddCandidates(candidates, post.Rows);
        AddCandidates(candidates, pre.Rows);
        var existingProcesses = await db.ImportProcesses.ToListAsync(cancellationToken);
        var processIds = existingProcesses.ToDictionary(value => value.NormalizedIpNumber, value => value.Id, StringComparer.OrdinalIgnoreCase);
        var newProcesses = 0;
        foreach (var (ip, candidate) in candidates)
        {
            var normalized = ip.ToUpperInvariant();
            if (processIds.ContainsKey(normalized))
            {
                continue;
            }

            var id = Guid.NewGuid();
            processIds[normalized] = id;
            db.ImportProcesses.Add(new ImportProcessRow
            {
                Id = id,
                IpNumber = ip,
                NormalizedIpNumber = normalized,
                Importer = candidate.Importer,
                LogisticsStatus = candidate.Status
            });
            var sourceRow = pre.Rows.Concat(post.Rows).FirstOrDefault(row =>
                string.Equals(ValidIp(Value(row, "IP Number")), ip, StringComparison.OrdinalIgnoreCase));
            var sourceRowId = sourceRow is not null && sourceIds.TryGetValue(SourceKey(sourceRow), out var foundSourceId)
                ? foundSourceId : Guid.Empty;
            AddWorkflowSeed(db, WorkflowAggregateType.ImportProcess, id,
                WorkflowRules.InitialState(WorkflowAggregateType.ImportProcess, candidate.Status), batchId,
                sourceRowId, sourceRow?.RowNumber, sourceRow?.SheetName);
            newProcesses++;
        }

        var links = (await db.ProcessPurchaseOrders.ToListAsync(cancellationToken))
            .Select(value => $"{value.PurchaseOrderId:N}|{value.ProcessId:N}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newLinks = 0;
        foreach (var row in pre.Rows)
        {
            var number = Value(row, "PO Totvs");
            var importer = Value(row, "Importer") ?? "UNSPECIFIED";
            var ip = ValidIp(Value(row, "IP Number"));
            if (number is null || ip is null)
            {
                continue;
            }

            var poId = poIds[PurchaseOrderKey(importer, number)];
            var processId = processIds[ip.ToUpperInvariant()];
            if (links.Add($"{poId:N}|{processId:N}"))
            {
                db.ProcessPurchaseOrders.Add(new ProcessPurchaseOrderRow { PurchaseOrderId = poId, ProcessId = processId });
                newLinks++;
            }
        }

        var costSourceColumns = (await db.ProcessCosts
                .Select(value => new { value.SourceRowId, value.SourceColumn })
                .ToListAsync(cancellationToken))
            .Select(value => $"{value.SourceRowId:N}|{value.SourceColumn}")
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newCosts = 0;
        foreach (var cost in plan.Costs)
        {
            var sourceId = sourceIds[SourceKey(cost.SheetName, cost.SourceRowNumber)];
            if (!costSourceColumns.Add($"{sourceId:N}|{cost.SourceColumn}"))
            {
                continue;
            }

            db.ProcessCosts.Add(new ProcessCostRow
            {
                Id = Guid.NewGuid(),
                ProcessId = processIds[cost.IpNumber.ToUpperInvariant()],
                SourceRowId = sourceId,
                SourceColumn = cost.SourceColumn,
                Type = cost.Type,
                Amount = cost.Amount,
                Currency = cost.Currency
            });
            newCosts++;
        }

        var issueKeys = (await db.Issues.Where(value => value.BatchId == batchId).ToListAsync(cancellationToken))
            .Select(value => IssueKey(value.SourceRowId, value.Code, value.ColumnName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var newIssues = 0;
        foreach (var issue in plan.Issues)
        {
            sourceIds.TryGetValue(SourceKey(issue.SheetName, issue.RowNumber), out var sourceId);
            var key = IssueKey(sourceId == Guid.Empty ? null : sourceId, issue.Code, issue.ColumnName);
            if (!issueKeys.Add(key))
            {
                continue;
            }

            db.Issues.Add(new ImportIssueRow
            {
                Id = Guid.NewGuid(),
                BatchId = batchId,
                SourceRowId = sourceId == Guid.Empty ? null : sourceId,
                Severity = issue.Severity,
                Code = issue.Code,
                ColumnName = issue.ColumnName,
                EvidenceJson = JsonSerializer.Serialize(issue)
            });
            newIssues++;
        }

        batch.State = "PROMOTED";
        await db.SaveChangesAsync(cancellationToken);
        transactionProbe?.BeforeCommit();
        await transaction.CommitAsync(cancellationToken);
        return new PromotionResult(batchId, newPos, newObservations, newProcesses, newLinks, newCosts, newIssues, batch.State);
    }

    private static void Validate(WorkbookExtraction extraction, HistoricalPromotionPlan plan)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(extraction.Sha256, plan.FileSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O plano nÃ£o pertence ao arquivo extraÃ­do.");
        }
    }

    private static void AddWorkflowSeed(SqliteHistoricalDbContext db, WorkflowAggregateType type, Guid id,
        string initialState, Guid batchId, Guid sourceRowId, int? rowNumber, string? sheetName)
    {
        db.WorkflowStates.Add(new WorkflowStateRow
        {
            AggregateType = type == WorkflowAggregateType.PurchaseOrder ? "PURCHASE_ORDER" : "IMPORT_PROCESS",
            AggregateId = id,
            CurrentState = initialState,
            Version = 0
        });
        db.WorkflowTransitionEvents.Add(new WorkflowTransitionEventRow
        {
            Id = Guid.NewGuid(),
            AggregateType = type == WorkflowAggregateType.PurchaseOrder ? "PURCHASE_ORDER" : "IMPORT_PROCESS",
            AggregateId = id,
            FromState = "IMPORTADO",
            ToState = initialState,
            Actor = "historical-import",
            Reason = "Estado inicial derivado do histórico; não confirma operação no TOTVS.",
            OccurredAt = DateTimeOffset.UtcNow.ToString("O"),
            EvidenceJson = JsonSerializer.Serialize(new { source = "historical-excel", batchId, sourceRowId, rowNumber, sheetName }),
            Version = 0
        });
    }

    private static void AddCandidates(IDictionary<string, (string? Importer, string? Status)> target, IEnumerable<ExtractedSourceRow> rows)
    {
        foreach (var row in rows)
        {
            var ip = ValidIp(Value(row, "IP Number"));
            if (ip is not null && !target.ContainsKey(ip))
            {
                target[ip] = (Value(row, "Importer"), Value(row, "Status"));
            }
        }
    }

    private static string? Value(ExtractedSourceRow row, string field) => row.Values.TryGetValue(field, out var value)
        ? HistoricalValueNormalizer.Normalize(field, value)
        : null;
    private static string? ValidIp(string? value) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "CANCELLED", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
    private static string SourceKey(ExtractedSourceRow row) => SourceKey(row.SheetName, row.RowNumber);
    private static string SourceKey(string? sheetName, int? rowNumber) => $"{sheetName}|{rowNumber}";
    private static string PurchaseOrderKey(string importer, string number) => $"{importer.Trim()}|{number.Trim()}";
    private static string IssueKey(Guid? sourceId, string code, string? column) => $"{sourceId?.ToString("N") ?? "none"}|{code}|{column ?? "none"}";
}

/// <summary>Test/integration seam used to verify transactional rollback before commit.</summary>
public interface IPromotionTransactionProbe
{
    void BeforeCommit();
}
