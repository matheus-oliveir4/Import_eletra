using System.Globalization;
using System.Text.Json;
using ImportErp.Application;
using ImportErp.Domain;
using Npgsql;

namespace ImportErp.Infrastructure;

/// <summary>Promotes staged evidence without overwriting the immutable source rows.</summary>
public sealed class PostgresHistoricalPromoter(NpgsqlDataSource dataSource) : IHistoricalPromoter
{
    public async Task<PromotionResult> PromoteAsync(
        WorkbookExtraction extraction,
        HistoricalPromotionPlan plan,
        Guid batchId,
        CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var sourceIds = await LoadSourceIdsAsync(connection, transaction, batchId, cancellationToken);
        if (sourceIds.Count != plan.SourceRowCount)
        {
            throw new InvalidOperationException("O lote não está completamente em staging; a promoção foi interrompida.");
        }

        var pre = extraction.Sheets.Single(sheet => string.Equals(sheet.Name, "Pré Embarque", StringComparison.OrdinalIgnoreCase));
        var post = extraction.Sheets.Single(sheet => string.Equals(sheet.Name, "Pós Embarque", StringComparison.OrdinalIgnoreCase));
        var purchaseOrderIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        var processCandidates = new Dictionary<string, (string? Importer, string? Status)>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in pre.Rows)
        {
            var po = Value(row, "PO Totvs");
            var importer = Value(row, "Importer") ?? "UNSPECIFIED";
            if (po is not null)
            {
                var key = PurchaseOrderKey(importer, po);
                if (!purchaseOrderIds.ContainsKey(key))
                {
                    purchaseOrderIds[key] = await UpsertPurchaseOrderAsync(connection, transaction, importer, po, cancellationToken);
                }

                await EnsureWorkflowSeedAsync(connection, transaction, WorkflowAggregateType.PurchaseOrder,
                    purchaseOrderIds[key], WorkflowRules.InitialState(WorkflowAggregateType.PurchaseOrder),
                    "historical-excel", batchId, sourceIds[SourceKey(row)], row.RowNumber, row.SheetName, cancellationToken);

                await InsertObservationAsync(connection, transaction, purchaseOrderIds[key], sourceIds[SourceKey(row)], row, cancellationToken);
            }

            AddProcessCandidate(processCandidates, ValidIp(Value(row, "IP Number")), importer, Value(row, "Status"));
        }

        foreach (var row in post.Rows)
        {
            AddProcessCandidate(processCandidates, ValidIp(Value(row, "IP Number")), Value(row, "Importer"), Value(row, "Status"));
        }

        var processIds = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        foreach (var (ip, candidate) in processCandidates)
        {
            processIds[ip] = await UpsertProcessAsync(connection, transaction, ip, candidate.Importer, candidate.Status, cancellationToken);
            var sourceRow = pre.Rows.Concat(post.Rows).FirstOrDefault(row =>
                string.Equals(ValidIp(Value(row, "IP Number")), ip, StringComparison.OrdinalIgnoreCase));
            var sourceRowId = sourceRow is not null && sourceIds.TryGetValue(SourceKey(sourceRow), out var foundSourceId)
                ? foundSourceId : Guid.Empty;
            await EnsureWorkflowSeedAsync(connection, transaction, WorkflowAggregateType.ImportProcess, processIds[ip],
                WorkflowRules.InitialState(WorkflowAggregateType.ImportProcess, candidate.Status),
                "historical-excel", batchId, sourceRowId, sourceRow?.RowNumber, sourceRow?.SheetName, cancellationToken);
        }

        var links = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in pre.Rows)
        {
            var po = Value(row, "PO Totvs");
            var importer = Value(row, "Importer") ?? "UNSPECIFIED";
            var ip = ValidIp(Value(row, "IP Number"));
            if (po is null || ip is null)
            {
                continue;
            }

            var poId = purchaseOrderIds[PurchaseOrderKey(importer, po)];
            var processId = processIds[ip];
            if (links.Add($"{poId:N}|{processId:N}"))
            {
                await InsertProcessLinkAsync(connection, transaction, poId, processId, cancellationToken);
            }
        }

        foreach (var cost in plan.Costs)
        {
            var sourceId = sourceIds[SourceKey(cost.SheetName, cost.SourceRowNumber)];
            await InsertCostAsync(connection, transaction, processIds[cost.IpNumber], sourceId, cost, cancellationToken);
        }

        foreach (var issue in plan.Issues)
        {
            sourceIds.TryGetValue(SourceKey(issue.SheetName, issue.RowNumber), out var sourceId);
            await InsertIssueAsync(connection, transaction, batchId, sourceId == Guid.Empty ? null : sourceId, issue, cancellationToken);
        }

        await SetPromotedAsync(connection, transaction, batchId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new PromotionResult(
            batchId,
            purchaseOrderIds.Count,
            pre.Rows.Count(row => Value(row, "PO Totvs") is not null),
            processIds.Count,
            links.Count,
            plan.Costs.Count,
            plan.Issues.Count,
            "PROMOTED");
    }

    private static async Task<Dictionary<string, Guid>> LoadSourceIdsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid batchId, CancellationToken cancellationToken)
    {
        const string sql = "SELECT id, sheet_name, row_number FROM migration.source_row WHERE batch_id = @batchId;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("batchId", batchId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<string, Guid>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken))
        {
            result[SourceKey(reader.GetString(1), reader.GetInt32(2))] = reader.GetGuid(0);
        }

        return result;
    }

    private static async Task<Guid> UpsertPurchaseOrderAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string importer, string number, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO procurement.purchase_order (id, importer, external_number, normalized_number)
            VALUES (@id, @importer, @number, @normalized)
            ON CONFLICT (importer, normalized_number)
            DO UPDATE SET updated_at = procurement.purchase_order.updated_at
            RETURNING id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("importer", importer);
        command.Parameters.AddWithValue("number", number);
        command.Parameters.AddWithValue("normalized", number.Trim().ToUpperInvariant());
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Não foi possível promover a PO."));
    }

    private static async Task<Guid> UpsertProcessAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string ip, string? importer, string? status, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO imports.import_process (id, importer, ip_number, normalized_ip_number, logistics_status)
            VALUES (@id, @importer, @ip, @normalized, @status)
            ON CONFLICT (normalized_ip_number)
            DO UPDATE SET updated_at = imports.import_process.updated_at
            RETURNING id;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("importer", (object?)importer ?? DBNull.Value);
        command.Parameters.AddWithValue("ip", ip);
        command.Parameters.AddWithValue("normalized", ip.Trim().ToUpperInvariant());
        command.Parameters.AddWithValue("status", (object?)status ?? DBNull.Value);
        return (Guid)(await command.ExecuteScalarAsync(cancellationToken) ?? throw new InvalidOperationException("Não foi possível promover o IP."));
    }

    private static async Task EnsureWorkflowSeedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        WorkflowAggregateType aggregateType, Guid aggregateId, string state, string source, Guid batchId,
        Guid sourceRowId, int? rowNumber, string? sheetName, CancellationToken cancellationToken)
    {
        var type = aggregateType == WorkflowAggregateType.PurchaseOrder ? "PURCHASE_ORDER" : "IMPORT_PROCESS";
        await using (var insertState = new NpgsqlCommand("""
            INSERT INTO audit.workflow_state (aggregate_type, aggregate_id, current_state, version)
            VALUES (@type, @id, @state, 0) ON CONFLICT (aggregate_type, aggregate_id) DO NOTHING
            """, connection, transaction))
        {
            insertState.Parameters.AddWithValue("type", type);
            insertState.Parameters.AddWithValue("id", aggregateId);
            insertState.Parameters.AddWithValue("state", state);
            await insertState.ExecuteNonQueryAsync(cancellationToken);
        }

        await using var insertEvent = new NpgsqlCommand("""
            INSERT INTO audit.workflow_transition_event
                (id, aggregate_type, aggregate_id, from_state, to_state, actor, reason, occurred_at, evidence, version)
            SELECT @eventId, @type, @id, 'IMPORTADO', @state, 'historical-import',
                   'Estado inicial derivado do histórico; não confirma operação no TOTVS.', now(),
                   CAST(@evidence AS jsonb), 0
            WHERE NOT EXISTS (
                SELECT 1 FROM audit.workflow_transition_event
                WHERE aggregate_type = @type AND aggregate_id = @id AND version = 0)
            ON CONFLICT (aggregate_type, aggregate_id, version) DO NOTHING
            """, connection, transaction);
        insertEvent.Parameters.AddWithValue("eventId", Guid.NewGuid());
        insertEvent.Parameters.AddWithValue("type", type);
        insertEvent.Parameters.AddWithValue("id", aggregateId);
        insertEvent.Parameters.AddWithValue("state", state);
        insertEvent.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(new { source, batchId, sourceRowId, rowNumber, sheetName }));
        await insertEvent.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertObservationAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid purchaseOrderId, Guid sourceRowId, ExtractedSourceRow row, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO procurement.po_line_observation
                (id, purchase_order_id, source_row_id, source_row_number, product_code_snapshot, description_snapshot,
                 quantity, unit_price, historical_amount, currency_code, necessity_date, historical_status, source_ip_text, raw_values)
            VALUES
                (@id, @poId, @sourceRowId, @rowNumber, @productCode, @description,
                 @quantity, @unitPrice, @amount, @currency, @necessity, @status, @sourceIp, CAST(@rawValues AS jsonb))
            ON CONFLICT (source_row_id) DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("poId", purchaseOrderId);
        command.Parameters.AddWithValue("sourceRowId", sourceRowId);
        command.Parameters.AddWithValue("rowNumber", row.RowNumber);
        command.Parameters.AddWithValue("productCode", (object?)Value(row, "Product Code") ?? DBNull.Value);
        command.Parameters.AddWithValue("description", (object?)Value(row, "Product Description") ?? DBNull.Value);
        command.Parameters.AddWithValue("quantity", (object?)DecimalValue(row, "Qty") ?? DBNull.Value);
        command.Parameters.AddWithValue("unitPrice", (object?)DecimalValue(row, "Unit Price") ?? DBNull.Value);
        command.Parameters.AddWithValue("amount", (object?)DecimalValue(row, "Total Price") ?? DBNull.Value);
        command.Parameters.AddWithValue("currency", (object?)Currency(Value(row, "Currency")) ?? DBNull.Value);
        command.Parameters.AddWithValue("necessity", (object?)DateValue(row, "Necessity") ?? DBNull.Value);
        command.Parameters.AddWithValue("status", (object?)Value(row, "Status") ?? DBNull.Value);
        command.Parameters.AddWithValue("sourceIp", (object?)Value(row, "IP Number") ?? DBNull.Value);
        command.Parameters.AddWithValue("rawValues", JsonSerializer.Serialize(row.Values));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertProcessLinkAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid poId, Guid processId, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO procurement.process_purchase_order (purchase_order_id, process_id)
            VALUES (@poId, @processId)
            ON CONFLICT DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("poId", poId);
        command.Parameters.AddWithValue("processId", processId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertCostAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid processId, Guid sourceRowId, PlannedProcessCost cost, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO costs.process_cost (id, process_id, source_row_id, source_column, cost_type, amount, currency_code)
            VALUES (@id, @processId, @sourceRowId, @sourceColumn, @type, @amount, @currency)
            ON CONFLICT (source_row_id, source_column) DO NOTHING;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("processId", processId);
        command.Parameters.AddWithValue("sourceRowId", sourceRowId);
        command.Parameters.AddWithValue("sourceColumn", cost.SourceColumn);
        command.Parameters.AddWithValue("type", cost.Type);
        command.Parameters.AddWithValue("amount", cost.Amount);
        command.Parameters.AddWithValue("currency", cost.Currency);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertIssueAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid batchId, Guid? sourceRowId, WorkbookIssue issue, CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO migration.data_issue (id, batch_id, source_row_id, severity, issue_code, field_name, evidence)
            SELECT @id, @batchId, @sourceRowId, @severity, @code, @field, CAST(@evidence AS jsonb)
            WHERE NOT EXISTS (
                SELECT 1 FROM migration.data_issue WHERE batch_id = @batchId AND issue_code = @code
                  AND source_row_id IS NOT DISTINCT FROM @sourceRowId AND field_name IS NOT DISTINCT FROM @field
            );
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("batchId", batchId);
        command.Parameters.AddWithValue("sourceRowId", (object?)sourceRowId ?? DBNull.Value);
        command.Parameters.AddWithValue("severity", issue.Severity);
        command.Parameters.AddWithValue("code", issue.Code);
        command.Parameters.AddWithValue("field", (object?)issue.ColumnName ?? DBNull.Value);
        command.Parameters.AddWithValue("evidence", JsonSerializer.Serialize(issue));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task SetPromotedAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid batchId, CancellationToken cancellationToken)
    {
        const string sql = "UPDATE migration.import_batch SET state = 'PROMOTED', promoted_at = now() WHERE id = @batchId;";
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("batchId", batchId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void AddProcessCandidate(IDictionary<string, (string? Importer, string? Status)> candidates, string? ip, string? importer, string? status)
    {
        if (ip is not null && !candidates.ContainsKey(ip))
        {
            candidates[ip] = (importer, status);
        }
    }

    private static string SourceKey(ExtractedSourceRow row) => SourceKey(row.SheetName, row.RowNumber);
    private static string SourceKey(string? sheetName, int? rowNumber) => $"{sheetName}|{rowNumber}";
    private static string PurchaseOrderKey(string importer, string number) => $"{importer.Trim()}|{number.Trim()}";
    private static string? Value(ExtractedSourceRow row, string field) => row.Values.TryGetValue(field, out var value)
        ? HistoricalValueNormalizer.Normalize(field, value)
        : null;
    private static string? ValidIp(string? value) => string.IsNullOrWhiteSpace(value) || string.Equals(value, "CANCELLED", StringComparison.OrdinalIgnoreCase) ? null : value.Trim();
    private static string? Currency(string? value) => value is { Length: 3 } ? value.ToUpperInvariant() : null;
    private static decimal? DecimalValue(ExtractedSourceRow row, string field) => Value(row, field) is { } raw
        && (decimal.TryParse(raw, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var invariant)
            || decimal.TryParse(raw, NumberStyles.Number, CultureInfo.GetCultureInfo("pt-BR"), out invariant)) ? invariant : null;
    private static DateOnly? DateValue(ExtractedSourceRow row, string field)
    {
        var raw = Value(row, field);
        if (DateOnly.TryParse(raw, CultureInfo.InvariantCulture, out var date)) return date;
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial)
            ? DateOnly.FromDateTime(DateTime.FromOADate(serial))
            : null;
    }
}
