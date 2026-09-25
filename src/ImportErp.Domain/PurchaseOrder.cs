namespace ImportErp.Domain;

public sealed class PurchaseOrder
{
    private readonly List<HistoricalPoObservation> _history = [];
    private readonly List<ImportProcessLink> _processLinks = [];
    private readonly Dictionary<string, string?> _operationalFields = new(StringComparer.OrdinalIgnoreCase);

    public PurchaseOrder(
        Guid id,
        string importer,
        string externalNumber,
        string sourceKind = "HISTORICAL_EXCEL")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importer);
        ArgumentException.ThrowIfNullOrWhiteSpace(externalNumber);

        Id = id;
        Importer = importer.Trim();
        ExternalNumber = externalNumber.Trim();
        NormalizedNumber = Normalize(externalNumber);
        SourceKind = sourceKind;
        IdentityStatus = PurchaseOrderIdentityStatus.HistoricalUnverified;
    }

    public Guid Id { get; }
    public string Importer { get; }
    public string ExternalNumber { get; }
    public string NormalizedNumber { get; }
    public string SourceKind { get; }
    public PurchaseOrderIdentityStatus IdentityStatus { get; private set; }
    public long Version { get; private set; }
    public IReadOnlyList<HistoricalPoObservation> History => _history;
    public IReadOnlyList<ImportProcessLink> ProcessLinks => _processLinks;
    public IReadOnlyDictionary<string, string?> OperationalFields => _operationalFields;

    public void AddHistoricalObservation(HistoricalPoObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);
        if (observation.PurchaseOrderId != Id)
        {
            throw new InvalidOperationException("A observação pertence a outra PO.");
        }

        if (_history.Any(x => x.SourceRowId == observation.SourceRowId))
        {
            throw new InvalidOperationException("A linha histórica já está vinculada à PO.");
        }

        _history.Add(observation);
    }

    public void LinkProcess(ImportProcessLink link)
    {
        ArgumentNullException.ThrowIfNull(link);
        if (link.PurchaseOrderId != Id)
        {
            throw new InvalidOperationException("O vínculo pertence a outra PO.");
        }

        if (_processLinks.Any(x => x.ProcessId == link.ProcessId))
        {
            return;
        }

        _processLinks.Add(link);
    }

    public void SetOperationalFields(IReadOnlyDictionary<string, string?> fields, long expectedVersion)
    {
        if (expectedVersion != Version)
        {
            throw new ConcurrencyException(Id, expectedVersion, Version);
        }

        foreach (var (name, value) in fields)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new DomainValidationException("O nome do campo operacional é obrigatório.");
            }

            _operationalFields[name.Trim()] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        Version++;
    }

    /// <summary>Restores persisted operational state without treating it as a user edit.</summary>
    public void RestoreOperationalState(IReadOnlyDictionary<string, string?> fields, long version)
    {
        if (version < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version));
        }

        _operationalFields.Clear();
        foreach (var (name, value) in fields.Where(pair => !string.IsNullOrWhiteSpace(pair.Key)))
        {
            _operationalFields[name.Trim()] = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        Version = version;
    }

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}

public sealed record HistoricalPoObservation(
    Guid Id,
    Guid PurchaseOrderId,
    Guid SourceRowId,
    int SourceRowNumber,
    string? ProductCode,
    string? ProductDescription,
    decimal? Quantity,
    decimal? UnitPrice,
    decimal? HistoricalAmount,
    string? Currency,
    DateOnly? NecessityDate,
    string? LegacyStatus,
    string? SourceIp,
    IReadOnlyDictionary<string, string?> RawValues);

public sealed record ImportProcessLink(
    Guid PurchaseOrderId,
    Guid ProcessId,
    string IpNumber,
    LinkResolutionStatus ResolutionStatus);

public enum PurchaseOrderIdentityStatus
{
    HistoricalUnverified,
    Confirmed,
    NeedsReview
}

public enum LinkResolutionStatus
{
    Historical,
    Confirmed,
    PendingReview
}

public class DomainValidationException(string message) : Exception(message);

public sealed class ConcurrencyException(Guid entityId, long expectedVersion, long actualVersion)
    : Exception("A PO foi alterada por outro usuário.")
{
    public Guid EntityId { get; } = entityId;
    public long ExpectedVersion { get; } = expectedVersion;
    public long ActualVersion { get; } = actualVersion;
}
