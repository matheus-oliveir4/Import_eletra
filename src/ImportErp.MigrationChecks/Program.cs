using ImportErp.Infrastructure;
using ImportErp.Application;
using ImportErp.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;

if (args.Length == 1)
{
    var workbookReader = new OpenXmlHistoricalWorkbookReader();
    var plan = await new HistoricalImportPlanner(workbookReader).CreatePlanAsync(args[0], CancellationToken.None);
    var extraction = await workbookReader.ExtractAsync(args[0], CancellationToken.None);
    var report = new HistoricalReconciliationService().Reconcile(extraction, plan);
    Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
    return;
}

if (args.Length != 0)
{
    throw new ArgumentException("Use sem argumentos para os checks ou informe um único caminho XLSX para a reconciliação.");
}

var databasePath = Path.Combine(Path.GetTempPath(), $"import-erp-migration-check-{Guid.NewGuid():N}.db");
var workbookPath = Path.Combine(Path.GetTempPath(), $"import-erp-workbook-check-{Guid.NewGuid():N}.xlsx");
try
{
    HistoricalWorkbookFixture.Create(workbookPath);
    var workbookReader = new OpenXmlHistoricalWorkbookReader();
    var preview = await workbookReader.PreviewAsync(workbookPath, CancellationToken.None);
    Require(preview.Issues.Count == 0, "Fixture must have the expected two-sheet layout.");
    Require(preview.Sheets.Single(sheet => sheet.Name == "Pré Embarque").BusinessRowCount == 5, "Pre-shipment rows must include all fixture cases.");
    Require(preview.Sheets.Single(sheet => sheet.Name == "Pré Embarque").CachedFormulaValueCount == 1, "The cached formula value must be observed without calculation.");

    var extraction = await workbookReader.ExtractAsync(workbookPath, CancellationToken.None);
    var pre = extraction.Sheets.Single(sheet => sheet.Name == "Pré Embarque");
    Require(pre.Rows.Single(row => row.RowNumber == 5).Values["Rupture Risk"] == "7", "Only the stored formula value may be read.");
    Require(pre.Rows.Single(row => row.RowNumber == 6).ErrorColumns.SequenceEqual(["NCM"]), "Excel errors must remain available for quality review.");
    var plan = await new HistoricalImportPlanner(workbookReader).CreatePlanAsync(workbookPath, CancellationToken.None);
    Require(plan.PurchaseOrderCount == 2 && plan.ImportProcessCount == 4, "Repeated PO and cancelled IP must not create duplicate or cancelled processes.");
    Require(plan.PreRowsWithoutIp == 2 && plan.RowsWithoutPo == 2, "Rows without PO or valid IP must be preserved in the plan.");
    Require(plan.Costs.Count == 3 && plan.Costs.Any(cost => cost.Amount == 1.25m) && plan.Costs.Any(cost => cost.Amount == 0m), "Decimal comma and known zero costs must be normalized without loss.");
    Require(plan.Issues.Any(issue => issue.Code == "EXCEL_ERROR") && plan.Issues.Any(issue => issue.Code == "IP_WITHOUT_PO") && plan.Issues.Any(issue => issue.Code == "POST_IP_NOT_IN_PRE"), "Fixture quality cases must create review issues.");
    var reconciliation = new HistoricalReconciliationService().Reconcile(extraction, plan);
    Require(reconciliation.Processes.Count == 4 && reconciliation.Processes.Single(process => process.IpNumber == "IP-001") is { PreRowCount: 1, PostRowCount: 1, ExistsInPre: true, ExistsInPost: true }, "Reconciliation must compare each IP at its source grain.");
    Require(reconciliation.Processes.Single(process => process.IpNumber == "IP-004") is { ExistsInPre: false, ExistsInPost: true }, "Post-only IPs must remain visible without invented PO links.");
    Require(reconciliation.CostsByCurrency.SequenceEqual([new CurrencyReconciliation("BRL", 2, 42.50m), new CurrencyReconciliation("USD", 1, 1.25m)]), "Costs must reconcile by currency without cross-currency totals.");

    var services = new ServiceCollection();
    services.AddDbContextFactory<SqliteHistoricalDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
    services.AddSingleton<SqliteMigrationRunner>();
    services.AddSingleton<IPromotionTransactionProbe, ThrowOnceBeforeCommit>();
    services.AddSingleton<SqliteHistoricalStore>();
    services.AddSingleton<IHistoricalQualityQueueRepository, SqliteHistoricalQualityQueueRepository>();
    services.AddSingleton<IErpAccessRepository, SqliteAccessControlRepository>();
    services.AddSingleton<IPurchaseOrderRepository, SqlitePurchaseOrderRepository>();
    services.AddSingleton<IImportProcessRepository, SqliteImportProcessRepository>();
    services.AddSingleton<IOutboxStore, SqliteOutboxDispatchStore>();
    services.AddSingleton<IConsumerInbox, SqliteConsumerInbox>();
    await using var provider = services.BuildServiceProvider();
    var runner = provider.GetRequiredService<SqliteMigrationRunner>();

    var first = await runner.MigrateAsync();
    Require(first.Applied.SequenceEqual(["M001_historical_core", "M002_historical_quality_reviews", "M003_access_control", "M004_workflows", "M005_audit_outbox", "M006_outbox_dispatch"]), "First execution must apply the historical core, quality, access-control, workflow and outbox dispatch migrations.");
    Require(first.Baselined.Count == 0, "A new database must not be baselined.");

    var factory = provider.GetRequiredService<IDbContextFactory<SqliteHistoricalDbContext>>();
    await using (var context = await factory.CreateDbContextAsync())
    {
        Require(await context.Database.CanConnectAsync(), "Migrated SQLite database must accept connections.");
        Require(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM __import_erp_migrations").SingleAsync() == 6, "Migration history must record the applied migrations.");
        Require(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'SourceRows'").SingleAsync() == 1, "M001 must create SourceRows.");
        Require(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'QualityReviews'").SingleAsync() == 1, "M002 must create QualityReviews.");
        Require(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'ErpUsers'").SingleAsync() == 1, "M003 must create the user access mapping.");
        Require(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'WorkflowTransitionEvents'").SingleAsync() == 1, "M004 must create the workflow journal.");
        Require(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'OutboxMessages'").SingleAsync() == 1, "M005 must create the transactional outbox.");
        Require(await context.Database.SqlQueryRaw<long>("SELECT COUNT(*) AS Value FROM sqlite_master WHERE type = 'table' AND name = 'EventInboxMessages'").SingleAsync() == 1, "M006 must create the idempotent consumer inbox.");
    }

    var second = await runner.MigrateAsync();
    Require(second.Applied.Count == 0 && second.Baselined.Count == 0, "Second execution must be idempotent.");

    var store = provider.GetRequiredService<SqliteHistoricalStore>();
    var staged = await store.StageAsync(extraction, plan, CancellationToken.None);
    Require(!staged.ExistingBatch && staged.InsertedSourceRows == 9, "Initial staging must preserve every source row.");
    var interrupted = false;
    try
    {
        await store.PromoteAsync(extraction, plan, staged.BatchId, CancellationToken.None);
    }
    catch (InvalidOperationException exception) when (exception.Message == ThrowOnceBeforeCommit.Message)
    {
        interrupted = true;
    }

    Require(interrupted, "The promotion check must inject a failure before commit.");
    await using (var context = await factory.CreateDbContextAsync())
    {
        Require(await context.PurchaseOrders.CountAsync() == 0 && await context.ImportProcesses.CountAsync() == 0 && await context.Observations.CountAsync() == 0 && await context.ProcessCosts.CountAsync() == 0, "Injected failure must roll back the complete promotion.");
    }

    var promoted = await store.PromoteAsync(extraction, plan, staged.BatchId, CancellationToken.None);
    Require(promoted.PurchaseOrdersPromoted == 2 && promoted.HistoricalObservationsPromoted == 3 && promoted.ImportProcessesPromoted == 4, "Promotion must keep PO and IP grains separate.");
    Require(promoted.PurchaseOrderProcessLinksPromoted == 2 && promoted.CostsPromoted == 3 && promoted.IssuesPromoted == 5, "Promotion must preserve links, costs and review issues exactly once.");
    var restaged = await store.StageAsync(extraction, plan, CancellationToken.None);
    var repromoted = await store.PromoteAsync(extraction, plan, restaged.BatchId, CancellationToken.None);
    Require(restaged.ExistingBatch && restaged.InsertedSourceRows == 0 && restaged.ExistingSourceRows == 9, "Restaging must be idempotent.");
    Require(repromoted.PurchaseOrdersPromoted == 0 && repromoted.HistoricalObservationsPromoted == 0 && repromoted.ImportProcessesPromoted == 0 && repromoted.PurchaseOrderProcessLinksPromoted == 0 && repromoted.CostsPromoted == 0 && repromoted.IssuesPromoted == 0, "Repromotion must not duplicate records.");
    var qualityQueue = provider.GetRequiredService<IHistoricalQualityQueueRepository>();
    var qualityPage = await qualityQueue.ListAsync(new HistoricalQualityFilter("open", null, null), CancellationToken.None);
    var missingIdentity = qualityPage.Items.Single(item => item.Code == "PRE_ROW_WITHOUT_PO_AND_IP");
    Require(qualityPage.OpenCount == qualityPage.TotalCount && missingIdentity.SourceValues.ContainsKey("PO Totvs"), "Quality queue must expose unresolved source rows without PO and IP.");
    var reviewed = await qualityQueue.ReviewAsync(new ReviewHistoricalQualityIssue(
        missingIdentity.SourceRowId, missingIdentity.Code, HistoricalQualityReviewOutcome.Resolved,
        "qa-local", "Classificação registrada para revisão posterior.", "PO-TEST", "IP-TEST"), CancellationToken.None);
    Require(reviewed.LatestReview is { Outcome: "RESOLVED", ProposedPurchaseOrder: "PO-TEST", ProposedIpNumber: "IP-TEST" }, "Quality review must retain the decision separately from the source values.");
    var resolvedQualityPage = await qualityQueue.ListAsync(new HistoricalQualityFilter("resolved", missingIdentity.Code, null), CancellationToken.None);
    Require(resolvedQualityPage.TotalCount == 1 && resolvedQualityPage.ResolvedCount == 1, "Resolved quality items must be filterable without deleting their source evidence.");

    var purchaseOrderService = new PurchaseOrderService(
        provider.GetRequiredService<IPurchaseOrderRepository>(),
        provider.GetRequiredService<IImportProcessRepository>(),
        qualityQueue);
    var portfolio = await purchaseOrderService.ListAsync(
        new PurchaseOrderFilter("PO-", null, IpNumber: "IP-001", Page: 1, PageSize: 1), CancellationToken.None);
    Require(portfolio is { TotalCount: 1, HasNext: false } && portfolio.Items.Single() is { Number: "PO-100", OfficialItemsKnown: false, BalanceAvailable: false },
        "Portfolio filters must remain PO-centric and declare unknown official items and balance.");
    var scopedPortfolio = await purchaseOrderService.ListAsync(
        new PurchaseOrderFilter(null, null, AllowedImporters: new HashSet<string>(["OUT_OF_SCOPE"], StringComparer.OrdinalIgnoreCase)), CancellationToken.None);
    Require(scopedPortfolio.TotalCount == 0, "Importer scope must be applied before portfolio pagination and totals.");
    var localAdmin = await provider.GetRequiredService<IErpAccessRepository>().FindAsync(
        new ErpActor("http://localhost:8180/realms/import-erp", "11111111-1111-1111-1111-111111111111", "Administrador local"), CancellationToken.None);
    Require(localAdmin is not null && localAdmin.Has(ErpPermissions.MigrationCommit) && localAdmin.AllowsImporter("ELETRA MATRIZ"),
        "Access lookup must use issuer + subject and only grant the explicitly seeded administrative scope.");
    var poId = portfolio.Items.Single().Id;
    var overview = await purchaseOrderService.GetOverviewAsync(poId, new DateOnly(2026, 9, 24), CancellationToken.None);
    Require(overview is { HistoricalItemCount: 2, LinkedProcessCount: 2, OfficialItemsKnown: false, BalanceAvailable: false, UnresolvedIssueCount: > 0 },
        "PO overview must expose coverage, open historical issues and no invented official balance.");
    var historyPage = await purchaseOrderService.ListHistoricalItemsAsync(poId, 2, 1, new DateOnly(2026, 9, 24), CancellationToken.None);
    Require(historyPage is { TotalCount: 2, HasNext: false } && historyPage.Items.Single().SourceSheetName == "PrÃ© Embarque",
        "Historical detail must be paginated and retain sheet-level lineage.");
    var updatedOverview = await purchaseOrderService.UpdateOperationalFieldsAsync(poId,
        new UpdateOperationalFields(overview!.Version, new Dictionary<string, string?> { ["responsible"] = "qa-local" }),
        new DateOnly(2026, 9, 24), CancellationToken.None);
    var concurrencyDetected = false;
    try
    {
        await purchaseOrderService.UpdateOperationalFieldsAsync(poId,
            new UpdateOperationalFields(overview.Version, new Dictionary<string, string?> { ["responsible"] = "second-writer" }),
            new DateOnly(2026, 9, 24), CancellationToken.None);
    }
    catch (ConcurrencyException)
    {
        concurrencyDetected = true;
    }
    Require(updatedOverview.Version == overview.Version + 1 && concurrencyDetected,
        "Operational changes must advance the version and reject a stale write.");

    var dispatchEventId = Guid.NewGuid();
    await using (var context = await factory.CreateDbContextAsync())
    {
        foreach (var pending in await context.OutboxMessages.Where(row => row.PublishedAt == null).ToListAsync())
            pending.PublishedAt = DateTimeOffset.UtcNow.ToString("O");
        context.OutboxMessages.Add(new OutboxMessageRow
        {
            EventId = dispatchEventId, EventType = "DispatchTest", AggregateType = "PURCHASE_ORDER", AggregateId = poId,
            PayloadJson = "{\"version\":1}", OccurredAt = DateTimeOffset.UtcNow.ToString("O")
        });
        await context.SaveChangesAsync();
    }
    var testConsumer = new FailOnceOutboxConsumer();
    var dispatcher = new OutboxDispatcher(provider.GetRequiredService<IOutboxStore>(), provider.GetRequiredService<IConsumerInbox>(), [testConsumer]);
    var dispatchStart = DateTimeOffset.UtcNow;
    var firstDispatch = await dispatcher.DispatchOnceAsync("migration-check-worker-a", 1, TimeSpan.FromMinutes(1), 3, dispatchStart, CancellationToken.None);
    Require(firstDispatch.Retried == 1 && testConsumer.InvocationCount == 1, "A transient consumer failure must schedule an outbox retry.");
    var secondDispatch = await dispatcher.DispatchOnceAsync("migration-check-worker-b", 1, TimeSpan.FromMinutes(1), 3, dispatchStart.AddMinutes(1), CancellationToken.None);
    Require(secondDispatch.Delivered == 1 && testConsumer.InvocationCount == 2, "A retry must redeliver the same stable event id.");
    await using (var context = await factory.CreateDbContextAsync())
    {
        var delivered = await context.OutboxMessages.SingleAsync(row => row.EventId == dispatchEventId);
        Require(delivered is { PublishedAt: not null, AttemptCount: 2, LeaseOwner: null }, "Successful delivery must acknowledge only the current lease owner.");
        delivered.PublishedAt = null; // Simulate a duplicate delivery after an acknowledgement-loss window.
        await context.SaveChangesAsync();
    }
    var duplicateDispatch = await dispatcher.DispatchOnceAsync("migration-check-worker-c", 1, TimeSpan.FromMinutes(1), 3, dispatchStart.AddMinutes(2), CancellationToken.None);
    Require(duplicateDispatch.Delivered == 1 && testConsumer.InvocationCount == 2, "The consumer inbox must suppress a completed event on duplicate delivery.");

    var leaseEventId = Guid.NewGuid();
    await using (var context = await factory.CreateDbContextAsync())
    {
        context.OutboxMessages.Add(new OutboxMessageRow
        {
            EventId = leaseEventId, EventType = "LeaseTest", AggregateType = "PURCHASE_ORDER", AggregateId = poId,
            PayloadJson = "{}", OccurredAt = DateTimeOffset.UtcNow.ToString("O")
        });
        await context.SaveChangesAsync();
    }
    var outbox = provider.GetRequiredService<IOutboxStore>();
    var firstLease = await outbox.ClaimAsync("lease-owner-a", 1, TimeSpan.FromMinutes(1), dispatchStart.AddMinutes(3), CancellationToken.None);
    var competingLease = await outbox.ClaimAsync("lease-owner-b", 1, TimeSpan.FromMinutes(1), dispatchStart.AddMinutes(3), CancellationToken.None);
    var recoveredLease = await outbox.ClaimAsync("lease-owner-b", 1, TimeSpan.FromMinutes(1), dispatchStart.AddMinutes(5), CancellationToken.None);
    Require(firstLease.Single().Event.EventId == leaseEventId && competingLease.Count == 0 && recoveredLease.Single().Event is { EventId: var recoveredId, AttemptCount: 2 } && recoveredId == leaseEventId,
        "A valid lease must exclude another dispatcher and an expired lease must be recoverable.");
    Console.WriteLine("Migration checks passed.");
}
finally
{
    Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
    if (File.Exists(databasePath)) File.Delete(databasePath);
    if (File.Exists(workbookPath)) File.Delete(workbookPath);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

file sealed class ThrowOnceBeforeCommit : IPromotionTransactionProbe
{
    public const string Message = "Injected promotion failure before commit.";
    private bool _thrown;

    public void BeforeCommit()
    {
        if (_thrown) return;
        _thrown = true;
        throw new InvalidOperationException(Message);
    }
}

file sealed class FailOnceOutboxConsumer : IOutboxConsumer
{
    public string Name => "migration-check-consumer";
    public int InvocationCount { get; private set; }
    public Task ConsumeAsync(OutboxEvent message, CancellationToken cancellationToken)
    {
        InvocationCount++;
        if (InvocationCount == 1) throw new TransientOutboxConsumerException("Injected transient delivery failure.");
        return Task.CompletedTask;
    }
}
