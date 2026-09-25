using System.Collections.Concurrent;
using ImportErp.Application;
using ImportErp.Domain;

namespace ImportErp.Infrastructure;

public sealed class InMemoryErpStore : IPurchaseOrderRepository, IImportProcessRepository
{
    private readonly ConcurrentDictionary<Guid, PurchaseOrder> _purchaseOrders = new();
    private readonly ConcurrentDictionary<Guid, ImportProcess> _processes = new();

    public InMemoryErpStore()
    {
        Seed();
    }

    public Task<IReadOnlyList<PurchaseOrder>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<PurchaseOrder>>(_purchaseOrders.Values.ToArray());

    public Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        _purchaseOrders.TryGetValue(id, out var purchaseOrder);
        return Task.FromResult(purchaseOrder);
    }

    public Task SaveAsync(PurchaseOrder purchaseOrder, IReadOnlyList<PurchaseOrderFieldChange> changes,
        string actor, Guid correlationId, CancellationToken cancellationToken)
    {
        _purchaseOrders[purchaseOrder.Id] = purchaseOrder;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<Guid, ImportProcess>> GetByIdsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<Guid, ImportProcess> result = ids
            .Where(id => _processes.ContainsKey(id))
            .ToDictionary(id => id, id => _processes[id]);
        return Task.FromResult(result);
    }

    private void Seed()
    {
        var processId = Guid.Parse("63e9e0d1-19bc-4ec5-8705-58fc312d71d1");
        var poId = Guid.Parse("14a13f4f-7dd3-4ed7-befe-c53f05a1d1fa");
        var process = new ImportProcess(processId, "ELETRA MATRIZ", "NH-017/2025");
        process.SetLogisticsStatus("DELIVERED");
        process.AddCost(new ProcessCost(
            Guid.Parse("7df67932-5f2c-4d71-bf02-9018cb8d2010"), processId,
            Guid.Parse("1a088ef9-5f50-4958-b8ec-b3484965c4d7"), "W",
            ProcessCostType.Freight, 17520m, "CNY"));
        process.AddCost(new ProcessCost(
            Guid.Parse("9f76b44c-84be-4f04-93f9-5cb2c72c2cf3"), processId,
            Guid.Parse("1a088ef9-5f50-4958-b8ec-b3484965c4d7"), "AQ",
            ProcessCostType.Storage, 545.45m, "BRL"));
        _processes[process.Id] = process;

        var po = new PurchaseOrder(poId, "ELETRA MATRIZ", "16111");
        po.AddHistoricalObservation(CreateObservation(
            po.Id,
            "8b5760fd-3a8c-4f7a-934d-42f24f93700d",
            4,
            "HXAP1000R21108-CPLV2",
            "Roteador de borda",
            39m,
            1303.63m,
            50841.57m,
            "CNY",
            new DateOnly(2026, 1, 1),
            "CANCELLED",
            "CANCELLED"));
        po.AddHistoricalObservation(CreateObservation(
            po.Id,
            "e86b2cae-9f9e-4b06-b283-9101b34db785",
            5,
            "MPG72R21003",
            "Módulo de comunicação",
            10316m,
            79.2m,
            817027.2m,
            "CNY",
            new DateOnly(2026, 1, 1),
            "CANCELLED",
            "CANCELLED"));
        _purchaseOrders[po.Id] = po;
    }

    private static HistoricalPoObservation CreateObservation(
        Guid purchaseOrderId,
        string sourceId,
        int row,
        string productCode,
        string productDescription,
        decimal quantity,
        decimal unitPrice,
        decimal amount,
        string currency,
        DateOnly necessity,
        string status,
        string ip) => new(
            Guid.NewGuid(),
            purchaseOrderId,
            Guid.Parse(sourceId),
            row,
            productCode,
            productDescription,
            quantity,
            unitPrice,
            amount,
            currency,
            necessity,
            status,
            ip,
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["Necessity"] = necessity.ToString("yyyy-MM-dd"),
                ["Status"] = status,
                ["IP Number"] = ip
            });
}
