using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ImportErp.Application;
using Npgsql;

namespace ImportErp.Infrastructure;

/// <summary>Persists immutable source rows before any promotion to operational aggregates.</summary>
public sealed class PostgresHistoricalStagingStore(NpgsqlDataSource dataSource) : IHistoricalStagingStore
{
    public async Task<StagingResult> StageAsync(
        WorkbookExtraction extraction,
        HistoricalPromotionPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(extraction);
        ArgumentNullException.ThrowIfNull(plan);
        if (!string.Equals(extraction.Sha256, plan.FileSha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("O plano não pertence ao arquivo extraído.");
        }

        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var (batchId, existingBatch) = await GetOrCreateBatchAsync(connection, transaction, extraction, plan.MappingVersion, cancellationToken);
        var inserted = 0;
        var existing = 0;

        foreach (var row in extraction.Sheets.SelectMany(sheet => sheet.Rows))
        {
            var rawJson = JsonSerializer.Serialize(row.Values);
            var errorsJson = JsonSerializer.Serialize(row.ErrorColumns);
            var rowHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawJson)));
            var wasInserted = await InsertSourceRowAsync(
                connection,
                transaction,
                batchId,
                row,
                rawJson,
                errorsJson,
                rowHash,
                cancellationToken);
            if (wasInserted)
            {
                inserted++;
            }
            else
            {
                existing++;
            }
        }

        await SetBatchStateAsync(connection, transaction, batchId, "STAGED", extraction.Sheets.Sum(sheet => sheet.Rows.Count), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new StagingResult(batchId, existingBatch, inserted, existing, "STAGED");
    }

    private static async Task<(Guid BatchId, bool Existing)> GetOrCreateBatchAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        WorkbookExtraction extraction,
        string mappingVersion,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO migration.import_batch (id, file_name, file_sha256, mapping_version, state)
            VALUES (@id, @fileName, @sha, @mappingVersion, 'RECEIVED')
            ON CONFLICT (file_sha256, mapping_version)
            DO UPDATE SET file_name = migration.import_batch.file_name
            RETURNING id, (xmax <> 0) AS existing;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("fileName", extraction.FileName);
        command.Parameters.AddWithValue("sha", extraction.Sha256);
        command.Parameters.AddWithValue("mappingVersion", mappingVersion);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidOperationException("Não foi possível criar ou localizar o lote de importação.");
        }

        return (reader.GetGuid(0), reader.GetBoolean(1));
    }

    private static async Task<bool> InsertSourceRowAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        ExtractedSourceRow row,
        string rawJson,
        string errorsJson,
        string rowHash,
        CancellationToken cancellationToken)
    {
        const string sql = """
            INSERT INTO migration.source_row
                (id, batch_id, sheet_name, row_number, raw_values, error_columns, row_hash)
            VALUES
                (@id, @batchId, @sheetName, @rowNumber, CAST(@rawValues AS jsonb), CAST(@errorColumns AS jsonb), @rowHash)
            ON CONFLICT (batch_id, sheet_name, row_number) DO NOTHING;
            """;

        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("id", Guid.NewGuid());
        command.Parameters.AddWithValue("batchId", batchId);
        command.Parameters.AddWithValue("sheetName", row.SheetName);
        command.Parameters.AddWithValue("rowNumber", row.RowNumber);
        command.Parameters.AddWithValue("rawValues", rawJson);
        command.Parameters.AddWithValue("errorColumns", errorsJson);
        command.Parameters.AddWithValue("rowHash", rowHash);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static async Task SetBatchStateAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        Guid batchId,
        string state,
        int sourceRowCount,
        CancellationToken cancellationToken)
    {
        const string sql = """
            UPDATE migration.import_batch
            SET state = @state, source_row_count = @sourceRowCount
            WHERE id = @batchId;
            """;
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        command.Parameters.AddWithValue("state", state);
        command.Parameters.AddWithValue("sourceRowCount", sourceRowCount);
        command.Parameters.AddWithValue("batchId", batchId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
