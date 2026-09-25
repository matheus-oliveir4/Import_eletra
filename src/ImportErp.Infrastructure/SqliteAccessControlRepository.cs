using ImportErp.Application;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ImportErp.Infrastructure;

/// <summary>Maps the immutable OIDC pair (issuer, subject) to ERP roles and scopes.</summary>
public sealed class SqliteAccessControlRepository(IDbContextFactory<SqliteHistoricalDbContext> contextFactory) : IErpAccessRepository
{
    public async Task<ErpAccess?> FindAsync(ErpActor actor, CancellationToken cancellationToken)
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);
        var user = await db.ErpUsers.AsNoTracking().SingleOrDefaultAsync(value => value.Issuer == actor.Issuer && value.Subject == actor.Subject && value.IsActive, cancellationToken);
        if (user is null) return null;
        var roles = await db.ErpUserRoles.AsNoTracking().Where(value => value.UserId == user.Id).Select(value => value.Role).ToArrayAsync(cancellationToken);
        var scopes = await db.ErpUserImporterScopes.AsNoTracking().Where(value => value.UserId == user.Id).Select(value => value.Importer).ToArrayAsync(cancellationToken);
        return new ErpAccess(actor, ErpRolePermissions.ForRoles(roles), new HashSet<string>(scopes, StringComparer.OrdinalIgnoreCase));
    }
}

/// <summary>Production implementation; it never falls back to claims or email.</summary>
public sealed class PostgresAccessControlRepository(NpgsqlDataSource dataSource) : IErpAccessRepository
{
    public async Task<ErpAccess?> FindAsync(ErpActor actor, CancellationToken cancellationToken)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var userCommand = new NpgsqlCommand("""
            SELECT id FROM identity.erp_user
            WHERE issuer = @issuer AND subject = @subject AND is_active = true
            """, connection);
        userCommand.Parameters.AddWithValue("issuer", actor.Issuer);
        userCommand.Parameters.AddWithValue("subject", actor.Subject);
        var userId = await userCommand.ExecuteScalarAsync(cancellationToken);
        if (userId is not Guid id) return null;

        var roles = await ReadStringsAsync("SELECT role FROM identity.erp_user_role WHERE user_id = @userId", id, connection, cancellationToken);
        var scopes = await ReadStringsAsync("SELECT importer_code FROM identity.erp_user_importer_scope WHERE user_id = @userId", id, connection, cancellationToken);
        return new ErpAccess(actor, ErpRolePermissions.ForRoles(roles), new HashSet<string>(scopes, StringComparer.OrdinalIgnoreCase));
    }

    private static async Task<string[]> ReadStringsAsync(string sql, Guid userId, NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("userId", userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var values = new List<string>();
        while (await reader.ReadAsync(cancellationToken)) values.Add(reader.GetString(0));
        return values.ToArray();
    }
}

public sealed class DenyErpAccessRepository : IErpAccessRepository
{
    public Task<ErpAccess?> FindAsync(ErpActor actor, CancellationToken cancellationToken) => Task.FromResult<ErpAccess?>(null);
}
