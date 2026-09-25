using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace ImportErp.Infrastructure;

public sealed record SqliteMigrationResult(IReadOnlyList<string> Applied, IReadOnlyList<string> Baselined);
public sealed record SqliteMigration(string Id, string Sql, string Checksum);

/// <summary>
/// Applies local SQLite scripts independently from API startup. Existing databases
/// created by the former EnsureCreated bootstrap are baselined only after their
/// complete schema is verified; they are never dropped or recreated.
/// </summary>
public sealed class SqliteMigrationRunner(IDbContextFactory<SqliteHistoricalDbContext> contextFactory)
{
    private static readonly string[] HistoricalCoreTables =
    ["Batches", "SourceRows", "PurchaseOrders", "ImportProcesses", "Observations", "ProcessPurchaseOrders", "ProcessCosts", "Issues"];

    public async Task<SqliteMigrationResult> MigrateAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var connection = (SqliteConnection)db.Database.GetDbConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await EnsureHistoryTableAsync(connection, transaction, cancellationToken);

        var applied = await ReadAppliedAsync(connection, transaction, cancellationToken);
        var existingTables = await ReadTablesAsync(connection, transaction, cancellationToken);
        var result = new List<string>();
        var baselined = new List<string>();
        foreach (var migration in SqliteMigrationCatalog.All)
        {
            if (applied.TryGetValue(migration.Id, out var checksum))
            {
                if (!string.Equals(checksum, migration.Checksum, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException($"A migration SQLite jÃ¡ aplicada foi alterada: {migration.Id}.");
                }

                continue;
            }

            if (migration.Id == "M001_historical_core" && existingTables.Overlaps(HistoricalCoreTables))
            {
                var missing = HistoricalCoreTables.Where(table => !existingTables.Contains(table)).ToArray();
                if (missing.Length > 0)
                {
                    throw new InvalidOperationException($"O banco SQLite existente tem esquema parcial; nÃ£o Ã© seguro migrar sem intervenÃ§Ã£o: {string.Join(", ", missing)}.");
                }

                await RecordAsync(connection, transaction, migration, cancellationToken);
                baselined.Add(migration.Id);
                continue;
            }

            // SQLite understands its own trigger bodies; splitting merely on a
            // semicolon turns a valid CREATE TRIGGER ... BEGIN ... END statement
            // into incomplete fragments. Execute the versioned script as one batch.
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            await RecordAsync(connection, transaction, migration, cancellationToken);
            result.Add(migration.Id);
        }

        await transaction.CommitAsync(cancellationToken);
        return new SqliteMigrationResult(result, baselined);
    }

    private static async Task EnsureHistoryTableAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "CREATE TABLE IF NOT EXISTS __import_erp_migrations (id TEXT NOT NULL PRIMARY KEY, checksum TEXT NOT NULL, applied_at TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<Dictionary<string, string>> ReadAppliedAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id, checksum FROM __import_erp_migrations;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0), reader.GetString(1));
        return result;
    }

    private static async Task<HashSet<string>> ReadTablesAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table';";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0));
        return result;
    }

    private static async Task RecordAsync(SqliteConnection connection, SqliteTransaction transaction, SqliteMigration migration, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "INSERT INTO __import_erp_migrations (id, checksum, applied_at) VALUES ($id, $checksum, $appliedAt);";
        command.Parameters.AddWithValue("$id", migration.Id);
        command.Parameters.AddWithValue("$checksum", migration.Checksum);
        command.Parameters.AddWithValue("$appliedAt", DateTimeOffset.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

}

internal static class SqliteMigrationCatalog
{
    public static IReadOnlyList<SqliteMigration> All { get; } = Load();

    private static IReadOnlyList<SqliteMigration> Load()
    {
        var assembly = typeof(SqliteMigrationCatalog).Assembly;
        return assembly.GetManifestResourceNames()
            .Where(name => name.EndsWith(".sqlite.sql", StringComparison.OrdinalIgnoreCase))
            .OrderBy(name => name, StringComparer.Ordinal)
            .Select(name =>
            {
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Migration resource not found: {name}.");
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                var sql = reader.ReadToEnd();
                var start = name.LastIndexOf(".M", StringComparison.Ordinal);
                var id = name[(start >= 0 ? start + 1 : 0)..].Replace(".sqlite.sql", string.Empty, StringComparison.OrdinalIgnoreCase);
                return new SqliteMigration(id, sql, Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(sql))));
            })
            .ToArray();
    }
}
