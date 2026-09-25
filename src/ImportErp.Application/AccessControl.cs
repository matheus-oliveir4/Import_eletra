namespace ImportErp.Application;

/// <summary>Permissions are stable contracts; roles only grant collections of them.</summary>
public static class ErpPermissions
{
    public const string PurchaseOrderRead = "purchase-order.read";
    public const string PurchaseOrderUpdate = "purchase-order.update";
    public const string DataIssueRead = "data-issue.read";
    public const string DataIssueReview = "data-issue.review";
    public const string MigrationPreview = "migration.preview";
    public const string MigrationCommit = "migration.commit";
    public const string UserManage = "user.manage";
    public const string ProcessTransition = "process.transition";
    public const string ProcessReopen = "process.reopen";
    public const string ProcessCancel = "process.cancel";
    public const string PurchaseOrderTransition = "purchase-order.transition";
    public const string PurchaseOrderReopen = "purchase-order.reopen";
    public const string AuditRead = "audit.read";
}

public sealed record ErpActor(string Issuer, string Subject, string? DisplayName);

/// <summary>An explicit wildcard is an administrative importer scope, never an implicit bypass.</summary>
public sealed record ErpAccess(ErpActor Actor, IReadOnlySet<string> Permissions, IReadOnlySet<string> ImporterScopes)
{
    public bool Has(string permission) => Permissions.Contains(permission);
    public bool AllowsImporter(string importer) => ImporterScopes.Contains("*") || ImporterScopes.Contains(importer.Trim());
    public bool AllowsAllImporters => ImporterScopes.Contains("*");
}

public interface IErpAccessRepository
{
    Task<ErpAccess?> FindAsync(ErpActor actor, CancellationToken cancellationToken);
}

public static class ErpRolePermissions
{
    public static IReadOnlySet<string> ForRoles(IEnumerable<string> roles)
    {
        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles.Select(value => value.Trim().ToUpperInvariant()))
        foreach (var permission in role switch
        {
            "ADMINISTRATOR" => All,
            "IMPORTACAO" => Importacao,
            "COMPRAS" => Compras,
            "LOGISTICA" => Logistics,
            "FISCAL" or "GESTOR" or "CONSULTA" => ReadOnly,
            _ => Empty
        }) permissions.Add(permission);
        return permissions;
    }

    private static readonly string[] All = [ErpPermissions.PurchaseOrderRead, ErpPermissions.PurchaseOrderUpdate, ErpPermissions.DataIssueRead, ErpPermissions.DataIssueReview, ErpPermissions.MigrationPreview, ErpPermissions.MigrationCommit, ErpPermissions.UserManage, ErpPermissions.ProcessTransition, ErpPermissions.ProcessReopen, ErpPermissions.ProcessCancel, ErpPermissions.PurchaseOrderTransition, ErpPermissions.PurchaseOrderReopen, ErpPermissions.AuditRead];
    private static readonly string[] Importacao = [ErpPermissions.PurchaseOrderRead, ErpPermissions.PurchaseOrderUpdate, ErpPermissions.DataIssueRead, ErpPermissions.DataIssueReview, ErpPermissions.MigrationPreview, ErpPermissions.ProcessTransition, ErpPermissions.ProcessReopen, ErpPermissions.ProcessCancel, ErpPermissions.PurchaseOrderTransition, ErpPermissions.PurchaseOrderReopen, ErpPermissions.AuditRead];
    private static readonly string[] Compras = [ErpPermissions.PurchaseOrderRead, ErpPermissions.PurchaseOrderUpdate, ErpPermissions.DataIssueRead, ErpPermissions.PurchaseOrderTransition, ErpPermissions.AuditRead];
    private static readonly string[] Logistics = [ErpPermissions.PurchaseOrderRead, ErpPermissions.DataIssueRead, ErpPermissions.ProcessTransition, ErpPermissions.ProcessCancel, ErpPermissions.AuditRead];
    private static readonly string[] ReadOnly = [ErpPermissions.PurchaseOrderRead, ErpPermissions.DataIssueRead, ErpPermissions.AuditRead];
    private static readonly string[] Empty = [];
}
