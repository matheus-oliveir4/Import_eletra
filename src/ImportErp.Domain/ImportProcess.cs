namespace ImportErp.Domain;

public sealed class ImportProcess
{
    private readonly List<ProcessCost> _costs = [];

    public ImportProcess(Guid id, string importer, string ipNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(importer);
        ArgumentException.ThrowIfNullOrWhiteSpace(ipNumber);

        Id = id;
        Importer = importer.Trim();
        IpNumber = ipNumber.Trim();
        NormalizedIpNumber = IpNumber.ToUpperInvariant();
    }

    public Guid Id { get; }
    public string Importer { get; }
    public string IpNumber { get; }
    public string NormalizedIpNumber { get; }
    public string? LogisticsStatus { get; private set; }
    public IReadOnlyList<ProcessCost> Costs => _costs;

    public void SetLogisticsStatus(string? value) => LogisticsStatus = string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public void AddCost(ProcessCost cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        if (cost.ProcessId != Id)
        {
            throw new InvalidOperationException("O custo pertence a outro IP.");
        }

        if (_costs.Any(x => x.SourceRowId == cost.SourceRowId && x.SourceColumn == cost.SourceColumn))
        {
            throw new InvalidOperationException("O custo de origem já foi importado para este IP.");
        }

        _costs.Add(cost);
    }
}

public sealed record ProcessCost(
    Guid Id,
    Guid ProcessId,
    Guid SourceRowId,
    string SourceColumn,
    ProcessCostType Type,
    decimal Amount,
    string CurrencyCode);

public enum ProcessCostType
{
    Freight,
    TaxesPaid,
    Fines,
    Storage,
    Demurrage
}
