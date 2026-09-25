using ImportErp.Application;
using ImportErp.Domain;
using ImportErp.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.IdentityModel.Tokens;
using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

if (builder.Environment.IsDevelopment())
{
    var repositoryRoot = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", ".."));
    var keyDirectory = Path.Combine(repositoryRoot, "data", "local", "dataprotection");
    Directory.CreateDirectory(keyDirectory);
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(new DirectoryInfo(keyDirectory))
        .SetApplicationName("ImportErp.Local");
}

builder.Services.AddOpenApi();
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
var webOrigin = builder.Configuration["Authentication:WebOrigin"] ?? "http://localhost:3000";
builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins(webOrigin)
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
    options.Cookie.Name = "__Host-erp-csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.Strict;
    options.Cookie.Path = "/";
});
builder.Services.AddAuthentication(options =>
    {
        options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    })
    .AddCookie(options =>
    {
        options.Cookie.Name = "__Host-erp-session";
        options.LoginPath = "/auth/login";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.Path = "/";
        options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }

            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };
        options.Events.OnValidatePrincipal = context =>
        {
            var started = context.Principal?.FindFirst("erp_session_started")?.Value;
            if (!DateTimeOffset.TryParse(started, out var issued)
                || DateTimeOffset.UtcNow - issued > TimeSpan.FromHours(8))
            {
                context.RejectPrincipal();
                return context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }

            return Task.CompletedTask;
        };
    })
    .AddOpenIdConnect(options =>
    {
        options.Authority = builder.Configuration["Authentication:Authority"];
        options.ClientId = builder.Configuration["Authentication:ClientId"];
        options.ClientSecret = builder.Configuration["Authentication:ClientSecret"];
        options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
        options.SignInScheme = CookieAuthenticationDefaults.AuthenticationScheme;
        options.ResponseType = "code";
        options.UsePkce = true;
        options.SaveTokens = false;
        options.GetClaimsFromUserInfoEndpoint = false;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "name";
        options.TokenValidationParameters.RoleClaimType = "roles";
        options.Scope.Clear();
        options.Scope.Add("openid");
        options.Scope.Add("profile");
        options.Events.OnTokenValidated = context =>
        {
            if (context.Principal?.Identity is System.Security.Claims.ClaimsIdentity identity)
            {
                identity.AddClaim(new System.Security.Claims.Claim("erp_session_started", DateTimeOffset.UtcNow.ToString("O")));
            }

            return Task.CompletedTask;
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<PurchaseOrderService>();
builder.Services.AddSingleton<WorkflowService>();
builder.Services.AddSingleton<OpenXmlHistoricalWorkbookReader>();
builder.Services.AddSingleton<IHistoricalWorkbookReader>(provider => provider.GetRequiredService<OpenXmlHistoricalWorkbookReader>());
builder.Services.AddSingleton<IHistoricalWorkbookExtractor>(provider => provider.GetRequiredService<OpenXmlHistoricalWorkbookReader>());
builder.Services.AddSingleton<HistoricalImportPlanner>();
if (builder.Environment.IsDevelopment())
{
    var defaultSqlitePath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "..", "..", "data", "local", "import_erp_test.db"));
    var sqliteConnection = builder.Configuration.GetConnectionString("ImportErpSqlite")
        ?? $"Data Source={defaultSqlitePath}";
    Directory.CreateDirectory(Path.GetDirectoryName(defaultSqlitePath)!);
    builder.Services.AddDbContextFactory<SqliteHistoricalDbContext>(options => options.UseSqlite(sqliteConnection));
    builder.Services.AddSingleton<SqliteMigrationRunner>();
    builder.Services.AddSingleton<SqliteHistoricalStore>();
    builder.Services.AddSingleton<IHistoricalStagingStore>(provider => provider.GetRequiredService<SqliteHistoricalStore>());
    builder.Services.AddSingleton<IHistoricalPromoter>(provider => provider.GetRequiredService<SqliteHistoricalStore>());
    builder.Services.AddSingleton<IHistoricalQualityQueueRepository, SqliteHistoricalQualityQueueRepository>();
    builder.Services.AddSingleton<IErpAccessRepository, SqliteAccessControlRepository>();
    builder.Services.AddSingleton<IWorkflowRepository, SqliteWorkflowRepository>();
    builder.Services.AddSingleton<IAuditLogRepository, SqliteAuditLogRepository>();
    builder.Services.AddSingleton<IOutboxStore, SqliteOutboxDispatchStore>();
    builder.Services.AddSingleton<IConsumerInbox, SqliteConsumerInbox>();
    builder.Services.AddSingleton<OutboxDispatcher>();
    builder.Services.AddSingleton<IPurchaseOrderRepository, SqlitePurchaseOrderRepository>();
    builder.Services.AddSingleton<IImportProcessRepository, SqliteImportProcessRepository>();
}
else
{
    var connectionString = builder.Configuration.GetConnectionString("ImportErp");
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        builder.Services.AddSingleton(NpgsqlDataSource.Create(connectionString));
        builder.Services.AddSingleton<IHistoricalStagingStore, PostgresHistoricalStagingStore>();
        builder.Services.AddSingleton<IHistoricalPromoter, PostgresHistoricalPromoter>();
        builder.Services.AddSingleton<IErpAccessRepository, PostgresAccessControlRepository>();
        builder.Services.AddSingleton<IWorkflowRepository, PostgresWorkflowRepository>();
        builder.Services.AddSingleton<IAuditLogRepository, PostgresAuditLogRepository>();
        builder.Services.AddSingleton<IOutboxStore, PostgresOutboxDispatchStore>();
        builder.Services.AddSingleton<IConsumerInbox, PostgresConsumerInbox>();
        builder.Services.AddSingleton<OutboxDispatcher>();
    }
    else
    {
        builder.Services.AddSingleton<InMemoryErpStore>();
        builder.Services.AddSingleton<IPurchaseOrderRepository>(provider => provider.GetRequiredService<InMemoryErpStore>());
        builder.Services.AddSingleton<IImportProcessRepository>(provider => provider.GetRequiredService<InMemoryErpStore>());
        builder.Services.AddSingleton<IErpAccessRepository, DenyErpAccessRepository>();
        builder.Services.AddSingleton<IWorkflowRepository, DenyWorkflowRepository>();
        builder.Services.AddSingleton<IAuditLogRepository, DenyAuditLogRepository>();
    }
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    await app.Services.GetRequiredService<SqliteHistoricalStore>().MigrateAsync();
}

app.UseExceptionHandler(errorApp => errorApp.Run(async context =>
{
    var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var status = exception switch
    {
        KeyNotFoundException => StatusCodes.Status404NotFound,
        UnauthorizedAccessException => StatusCodes.Status403Forbidden,
        ConcurrencyException => StatusCodes.Status409Conflict,
        WorkflowTransitionException => StatusCodes.Status422UnprocessableEntity,
        DomainValidationException => StatusCodes.Status400BadRequest,
        _ => StatusCodes.Status500InternalServerError
    };

    context.Response.StatusCode = status;
    var detail = app.Environment.IsDevelopment() ? exception?.Message ?? "Erro inesperado." : "Erro inesperado.";
    if (exception is WorkflowTransitionException workflowError)
        await context.Response.WriteAsJsonAsync(new { status, detail, code = "WORKFLOW_PRECONDITION_FAILED", errors = new { missingEvidence = workflowError.MissingEvidence, permission = workflowError.RequiredPermission } });
    else
        await context.Response.WriteAsJsonAsync(new ProblemDetailsResponse(status, detail));
}));

app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

var auth = app.MapGroup("/auth");
auth.MapGet("/login", (HttpContext context) =>
{
    var targetOrigin = new Uri(webOrigin);
    var requested = context.Request.Query["returnUrl"].ToString();
    var destination = Uri.TryCreate(requested, UriKind.Absolute, out var parsed)
        && parsed.GetLeftPart(UriPartial.Authority).Equals(targetOrigin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
        ? parsed.ToString()
        : targetOrigin.ToString();
    return Results.Challenge(new AuthenticationProperties { RedirectUri = destination }, [OpenIdConnectDefaults.AuthenticationScheme]);
}).AllowAnonymous();

auth.MapGet("/me", (HttpContext context) => Results.Ok(new
{
    subject = context.User.FindFirst("sub")?.Value,
    issuer = context.User.FindFirst("iss")?.Value,
    name = context.User.FindFirst("name")?.Value ?? context.User.FindFirst("preferred_username")?.Value,
    roles = context.User.FindAll("roles").Select(claim => claim.Value).ToArray()
})).RequireAuthorization();

auth.MapGet("/csrf", (HttpContext context, IAntiforgery antiforgery) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    return Results.Ok(new { requestToken = tokens.RequestToken });
}).RequireAuthorization();

auth.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery) =>
{
    try
    {
        await antiforgery.ValidateRequestAsync(context);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.Problem("Token CSRF ausente ou inválido.", statusCode: StatusCodes.Status400BadRequest);
    }

    await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.NoContent();
}).RequireAuthorization();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

var api = app.MapGroup("/api/v1").RequireAuthorization().AddEndpointFilter(async (context, next) =>
{
    var method = context.HttpContext.Request.Method;
    if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
    {
        return await next(context);
    }

    try
    {
        var antiforgery = context.HttpContext.RequestServices.GetRequiredService<IAntiforgery>();
        await antiforgery.ValidateRequestAsync(context.HttpContext);
    }
    catch (AntiforgeryValidationException)
    {
        return Results.Problem("Token CSRF ausente ou inválido.", statusCode: StatusCodes.Status400BadRequest);
    }

    return await next(context);
});

// Historical files can expose every importer; only the explicit migration
// permission may invoke even development-only preview/stage/promote commands.
var imports = api.MapGroup("/imports");
imports.AddEndpointFilter(async (EndpointFilterInvocationContext context, EndpointFilterDelegate next) =>
{
    var accessRepository = context.HttpContext.RequestServices.GetRequiredService<IErpAccessRepository>();
    var configuration = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
    var requestPath = context.HttpContext.Request.Path.Value ?? "";
    var previewOnly = requestPath.EndsWith("/preview", StringComparison.OrdinalIgnoreCase)
        || requestPath.EndsWith("/plan", StringComparison.OrdinalIgnoreCase);
    var requiredPermission = previewOnly ? ErpPermissions.MigrationPreview : ErpPermissions.MigrationCommit;
    var access = await RequirePermissionAsync(context.HttpContext, accessRepository, configuration, requiredPermission, context.HttpContext.RequestAborted);
    return access is null ? Results.Forbid() : await next(context);
});

api.MapGet("/purchase-orders", async (
    string? number,
    string? importer,
    string? supplier,
    string? product,
    string? ipNumber,
    string? historicalStatus,
    string? fulfillmentStatus,
    string? quality,
    DateOnly? necessityFrom,
    DateOnly? necessityTo,
    string? sortBy,
    int? page,
    int? pageSize,
    HttpContext context,
    IErpAccessRepository accessRepository,
    IConfiguration configuration,
    PurchaseOrderService service,
    CancellationToken cancellationToken) =>
{
    var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.PurchaseOrderRead, cancellationToken);
    if (access is null) return Results.Forbid();
    var result = await service.ListAsync(new PurchaseOrderFilter(
        number, importer, supplier, product, ipNumber, historicalStatus, fulfillmentStatus,
        quality, necessityFrom, necessityTo, sortBy, page ?? 1, pageSize ?? 50, access.ImporterScopes), cancellationToken);
    return Results.Ok(result);
});

api.MapGet("/purchase-orders/{id:guid}/overview", async (
    Guid id,
    HttpResponse response,
    HttpContext context,
    IErpAccessRepository accessRepository,
    IConfiguration configuration,
    PurchaseOrderService service,
    CancellationToken cancellationToken) =>
{
    var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.PurchaseOrderRead, cancellationToken);
    if (access is null) return Results.Forbid();
    var overview = await service.GetOverviewAsync(id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    if (overview is not null && !access.AllowsImporter(overview.Importer)) return Results.NotFound();
    if (overview is not null) response.Headers["ETag"] = QuoteEtag(overview.Version);
    return overview is null ? Results.NotFound() : Results.Ok(overview);
});

api.MapGet("/purchase-orders/{id:guid}/history-items", async (
    Guid id,
    int? page,
    int? pageSize,
    HttpContext context,
    IErpAccessRepository accessRepository,
    IConfiguration configuration,
    PurchaseOrderService service,
    CancellationToken cancellationToken) =>
{
    var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.PurchaseOrderRead, cancellationToken);
    if (access is null) return Results.Forbid();
    var overview = await service.GetOverviewAsync(id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    if (overview is null || !access.AllowsImporter(overview.Importer)) return Results.NotFound();
    var items = await service.ListHistoricalItemsAsync(
        id, page ?? 1, pageSize ?? 50, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    return items is null ? Results.NotFound() : Results.Ok(items);
});

api.MapPatch("/purchase-orders/{id:guid}/operational-fields", async (
    Guid id,
    UpdateOperationalFieldsRequest request,
    HttpRequest httpRequest,
    HttpResponse httpResponse,
    HttpContext context,
    IErpAccessRepository accessRepository,
    IConfiguration configuration,
    PurchaseOrderService service,
    CancellationToken cancellationToken) =>
{
    var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.PurchaseOrderUpdate, cancellationToken);
    if (access is null) return Results.Forbid();
    var existing = await service.GetOverviewAsync(id, DateOnly.FromDateTime(DateTime.UtcNow), cancellationToken);
    if (existing is null || !access.AllowsImporter(existing.Importer)) return Results.NotFound();
    if (request.Fields.Count == 0)
    {
        return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "Informe ao menos um campo operacional."));
    }

    var ifMatch = ParseEtag(httpRequest.Headers["If-Match"].ToString());
    if (ifMatch is null)
    {
        return Results.Problem("Informe If-Match com a versÃ£o atual da PO.", statusCode: StatusCodes.Status428PreconditionRequired);
    }
    if (ifMatch != request.ExpectedVersion)
    {
        return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "ExpectedVersion deve ser igual a If-Match."));
    }

    var overview = await service.UpdateOperationalFieldsAsync(
        id,
        new UpdateOperationalFields(ifMatch.Value, request.Fields, ActorIdentity(context)),
        DateOnly.FromDateTime(DateTime.UtcNow),
        cancellationToken);
    httpResponse.Headers["ETag"] = QuoteEtag(overview.Version);
    return Results.Ok(overview);
});

api.MapGet("/quality-issues", async (
    string? status,
    string? code,
    string? sheetName,
    int? page,
    int? pageSize,
    IServiceProvider services,
    HttpContext context,
    IErpAccessRepository accessRepository,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.DataIssueRead, cancellationToken);
    if (access is null) return Results.Forbid();
    var queue = services.GetService<IHistoricalQualityQueueRepository>();
    if (queue is null)
    {
        return Results.Problem("A fila de qualidade está disponível apenas no armazenamento configurado.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    return Results.Ok(await queue.ListAsync(new HistoricalQualityFilter(status, code, sheetName, page ?? 1, pageSize ?? 50, access.ImporterScopes), cancellationToken));
});

api.MapPost("/quality-issues/{sourceRowId:guid}/reviews", async (
    Guid sourceRowId,
    ReviewHistoricalQualityIssueRequest request,
    IServiceProvider services,
    HttpContext context,
    IErpAccessRepository accessRepository,
    IConfiguration configuration,
    CancellationToken cancellationToken) =>
{
    var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.DataIssueReview, cancellationToken);
    if (access is null) return Results.Forbid();
    var queue = services.GetService<IHistoricalQualityQueueRepository>();
    if (queue is null)
    {
        return Results.Problem("A fila de qualidade está disponível apenas no armazenamento configurado.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    if (!Enum.TryParse<HistoricalQualityReviewOutcome>(request.Outcome, ignoreCase: true, out var outcome))
    {
        return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "Resultado de revisão inválido."));
    }

    var item = await queue.ReviewAsync(new ReviewHistoricalQualityIssue(
        sourceRowId, request.Code, outcome, ActorIdentity(context), request.Notes,
        request.ProposedPurchaseOrder, request.ProposedIpNumber, access.ImporterScopes), cancellationToken);
    return Results.Created($"/api/v1/quality-issues/{sourceRowId}", item);
});

MapWorkflowEndpoints(api);

api.MapGet("/audit/{aggregateType}/{id:guid}", async (string aggregateType, Guid id, int? page, int? pageSize,
    HttpContext context, IErpAccessRepository accessRepository, IConfiguration configuration,
    WorkflowService workflows, IAuditLogRepository audit, CancellationToken cancellationToken) =>
{
    var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.AuditRead, cancellationToken);
    if (access is null) return Results.Forbid();
    var type = aggregateType.Trim().ToUpperInvariant() switch
    {
        "PO" or "PURCHASE_ORDER" => WorkflowAggregateType.PurchaseOrder,
        "IP" or "IMPORT_PROCESS" => WorkflowAggregateType.ImportProcess,
        _ => (WorkflowAggregateType?)null
    };
    if (type is null) return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "Tipo de agregado inválido."));
    var visible = await workflows.GetAsync(type.Value, id, access, cancellationToken);
    if (visible is null) return Results.NotFound();
    return Results.Ok(await audit.ListAsync(type.Value, id, page ?? 1, pageSize ?? 50, cancellationToken));
});

imports.MapPost("/preview", async (
    ImportPreviewRequest request,
    IHistoricalWorkbookReader reader,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    if (!environment.IsDevelopment())
    {
        return Results.NotFound();
    }

    var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    var path = Path.GetFullPath(request.WorkbookPath);
    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "O arquivo deve estar dentro do diretório do projeto."));
    }

    var preview = await reader.PreviewAsync(path, cancellationToken);
    return Results.Ok(preview);
});

imports.MapPost("/plan", async (
    ImportPreviewRequest request,
    HistoricalImportPlanner planner,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    if (!environment.IsDevelopment())
    {
        return Results.NotFound();
    }

    var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    var path = Path.GetFullPath(request.WorkbookPath);
    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "O arquivo deve estar dentro do diretório do projeto."));
    }

    return Results.Ok(await planner.CreatePlanAsync(path, cancellationToken));
});

imports.MapPost("/stage", async (
    ImportPreviewRequest request,
    HistoricalImportPlanner planner,
    IHistoricalWorkbookExtractor extractor,
    IServiceProvider services,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    if (!environment.IsDevelopment())
    {
        return Results.NotFound();
    }

    var store = services.GetService<IHistoricalStagingStore>();
    if (store is null)
    {
        return Results.Problem("Configure ConnectionStrings:ImportErp para habilitar o staging PostgreSQL.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    var path = Path.GetFullPath(request.WorkbookPath);
    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "O arquivo deve estar dentro do diretório do projeto."));
    }

    var extraction = await extractor.ExtractAsync(path, cancellationToken);
    var plan = await planner.CreatePlanAsync(path, cancellationToken);
    return Results.Ok(await store.StageAsync(extraction, plan, cancellationToken));
});

imports.MapPost("/promote", async (
    ImportPreviewRequest request,
    HistoricalImportPlanner planner,
    IHistoricalWorkbookExtractor extractor,
    IServiceProvider services,
    IHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    if (!environment.IsDevelopment())
    {
        return Results.NotFound();
    }

    var stagingStore = services.GetService<IHistoricalStagingStore>();
    var promoter = services.GetService<IHistoricalPromoter>();
    if (stagingStore is null || promoter is null)
    {
        return Results.Problem("Configure ConnectionStrings:ImportErp para habilitar staging e promoção PostgreSQL.", statusCode: StatusCodes.Status503ServiceUnavailable);
    }

    var root = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", ".."));
    var path = Path.GetFullPath(request.WorkbookPath);
    if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "O arquivo deve estar dentro do diretório do projeto."));
    }

    var extraction = await extractor.ExtractAsync(path, cancellationToken);
    var plan = await planner.CreatePlanAsync(path, cancellationToken);
    var staged = await stagingStore.StageAsync(extraction, plan, cancellationToken);
    return Results.Ok(await promoter.PromoteAsync(extraction, plan, staged.BatchId, cancellationToken));
});

app.Run();

static long? ParseEtag(string? value)
{
    var tag = value?.Trim();
    if (string.IsNullOrWhiteSpace(tag)) return null;
    if (tag.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) tag = tag[2..].Trim();
    tag = tag.Trim('\"');
    return long.TryParse(tag, out var version) && version >= 0 ? version : null;
}

static string QuoteEtag(long version) => $"\"{version}\"";

static async Task<ErpAccess?> RequirePermissionAsync(
    HttpContext context,
    IErpAccessRepository accessRepository,
    IConfiguration configuration,
    string permission,
    CancellationToken cancellationToken)
{
    var subject = context.User.FindFirst("sub")?.Value;
    var issuer = context.User.FindFirst("iss")?.Value ?? configuration["Authentication:Authority"];
    if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(issuer)) return null;
    var access = await accessRepository.FindAsync(new ErpActor(issuer, subject, ActorDisplayName(context)), cancellationToken);
    return access is not null && access.Has(permission) && access.ImporterScopes.Count > 0 ? access : null;
}

static string ActorIdentity(HttpContext context) =>
    $"{context.User.FindFirst("iss")?.Value ?? "unknown-issuer"}|{context.User.FindFirst("sub")?.Value ?? "unknown-subject"}";

static string ActorDisplayName(HttpContext context) => context.User.FindFirst("name")?.Value
    ?? context.User.FindFirst("preferred_username")?.Value
    ?? context.User.FindFirst("sub")?.Value
    ?? "usuário autenticado";

static void MapWorkflowEndpoints(RouteGroupBuilder api)
{
    MapFor(WorkflowAggregateType.PurchaseOrder, "/purchase-orders/{id:guid}/workflow");
    MapFor(WorkflowAggregateType.ImportProcess, "/processes/{id:guid}/workflow");

    void MapFor(WorkflowAggregateType type, string path)
    {
        api.MapGet(path, async (Guid id, HttpContext context, HttpResponse response, WorkflowService workflow,
            IErpAccessRepository accessRepository, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.PurchaseOrderRead, cancellationToken);
            if (access is null) return Results.Forbid();
            var state = await workflow.GetAsync(type, id, access, cancellationToken);
            if (state is not null) response.Headers["ETag"] = QuoteEtag(state.Version);
            return state is null ? Results.NotFound() : Results.Ok(state);
        });

        api.MapGet(path + "/history", async (Guid id, HttpContext context, WorkflowService workflow,
            IErpAccessRepository accessRepository, IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var access = await RequirePermissionAsync(context, accessRepository, configuration, ErpPermissions.DataIssueRead, cancellationToken);
            if (access is null) return Results.Forbid();
            var history = await workflow.GetHistoryAsync(type, id, access, cancellationToken);
            return history is null ? Results.NotFound() : Results.Ok(history);
        });

        api.MapPost(path + "/transitions", async (Guid id, TransitionWorkflowRequest request, HttpContext context,
            IErpAccessRepository accessRepository, IConfiguration configuration, WorkflowService workflow,
            CancellationToken cancellationToken) =>
        {
            var access = await RequirePermissionAsync(context, accessRepository, configuration,
                ErpPermissions.PurchaseOrderRead, cancellationToken);
            if (access is null) return Results.Forbid();
            var ifMatch = ParseEtag(context.Request.Headers["If-Match"].ToString());
            if (ifMatch is null) return Results.Problem("Informe If-Match com a versão atual do workflow.", statusCode: StatusCodes.Status428PreconditionRequired);
            if (ifMatch != request.ExpectedVersion) return Results.BadRequest(new ProblemDetailsResponse(StatusCodes.Status400BadRequest, "ExpectedVersion deve ser igual a If-Match."));
            var result = await workflow.TransitionAsync(new TransitionWorkflowCommand(
                type, id, ifMatch.Value, request.TargetState, ActorIdentity(context), request.Reason,
                request.Evidence, access), cancellationToken);
            context.Response.Headers["ETag"] = QuoteEtag(result.Version);
            return Results.Ok(result);
        });
    }
}

public sealed class UpdateOperationalFieldsRequest
{
    public long ExpectedVersion { get; init; }
    public Dictionary<string, string?> Fields { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed class TransitionWorkflowRequest
{
    public long ExpectedVersion { get; init; }
    public string TargetState { get; init; } = "";
    public string Reason { get; init; } = "";
    public Dictionary<string, string?> Evidence { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
public sealed record ImportPreviewRequest(string WorkbookPath);
public sealed record ProblemDetailsResponse(int Status, string Detail);
public sealed class ReviewHistoricalQualityIssueRequest
{
    public string Code { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string Reviewer { get; init; } = "";
    public string Notes { get; init; } = "";
    public string? ProposedPurchaseOrder { get; init; }
    public string? ProposedIpNumber { get; init; }
}
