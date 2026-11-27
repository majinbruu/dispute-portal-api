using System.Diagnostics;
using System.Text.Json.Serialization;
using DisputePortal.Api.Hosting;
using DisputePortal.Modules.Disputes;
using DisputePortal.Modules.Notifications;
using DisputePortal.Modules.Transactions;
using DisputePortal.SharedKernel.Web;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.OpenApi;
using Prometheus;

if (args.Contains(HealthProbe.Argument))
{
    return await HealthProbe.RunAsync();
}

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;
var connectionString = configuration.GetConnectionString("Postgres") ?? string.Empty;

// Structured JSON logs to stdout, each line carrying the W3C trace ID of the request that produced it.
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(options =>
{
    options.IncludeScopes = true;
    options.UseUtcTimestamp = true;
    options.TimestampFormat = "O";
});
builder.Logging.Configure(options =>
    options.ActivityTrackingOptions = ActivityTrackingOptions.TraceId | ActivityTrackingOptions.SpanId);

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// Every error response is an RFC 9457 problem document with the trace ID, including unhandled exceptions, 401s and 404s.
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions.TryAdd("traceId", Activity.Current?.Id ?? context.HttpContext.TraceIdentifier));

// Each module contributes its controllers. They are internal to the module and discovered through its assembly.
builder.Services.AddPortalControllers(
    typeof(TransactionsModule).Assembly,
    typeof(DisputesModule).Assembly,
    typeof(NotificationsModule).Assembly);

builder.Services.AddPortalAuthentication(configuration);
builder.Services.AddPortalRateLimiting(configuration);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "ACME Bank Dispute Portal API";
    document.Info.Version = "v1";
    document.Info.Description =
        "Transactions (debit orders and card), mandates, disputes and notifications for the signed-in customer, " +
        "and the dispute review queue for bank agents. All endpoints under /api require a Keycloak access token: " +
        "with the 'agent' role under /api/v1/agent, and with the 'customer' role everywhere else. " +
        "Errors are RFC 9457 problem documents with a stable 'code' and a 'traceId'.";

    // Declares bearer authentication, which gives the Swagger page an Authorize button for pasting an access token.
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["bearer"] = new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "A Keycloak access token for a user with the 'customer' role, or the 'agent' role for the agent endpoints.",
    };
    document.Security ??= [];
    document.Security.Add(new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("bearer", document)] = [] });
    return Task.CompletedTask;
}));

builder.Services
    .AddTransactionsModule(connectionString)
    .AddDisputesModule(configuration, connectionString)
    .AddNotificationsModule(connectionString);

builder.Services.AddHealthChecks()
    .AddTransactionsHealthChecks()
    .AddDisputesHealthChecks()
    .AddNotificationsHealthChecks();

// Business and runtime metrics are published from .NET meters. The Npgsql meter is left out because its labels
// carry the connection string (host, database and user name), and the runtime's internal diagnostics are noise.
Metrics.ConfigureMeterAdapter(options => options.InstrumentFilterPredicate = instrument =>
    instrument.Meter.Name != "Npgsql" &&
    !instrument.Meter.Name.StartsWith("Private.InternalDiagnostics", StringComparison.Ordinal));

var app = builder.Build();

// The meter listener starts on the first collection. Collect once now so measurements made during
// start-up, before the first scrape, are not lost.
await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(Stream.Null);

// Schema migrations run at start-up only where configured (Docker Compose). In Kubernetes a Job runs
// the same image with --migrate-only before the deployment rolls, so replicas never race to migrate.
var migrateOnly = args.Contains("--migrate-only");
if (migrateOnly || configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.Services.MigrateTransactionsAsync();
    await app.Services.MigrateDisputesAsync();
    await app.Services.MigrateNotificationsAsync();
}

if (configuration.GetValue("Seed:Enabled", false))
{
    await app.Services.SeedTransactionsAsync();
    await app.Services.SeedDisputesAsync();
}

if (migrateOnly)
{
    return 0;
}

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    await next(context);
});

app.UseRouting();
app.UseHttpMetrics();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapControllers();

// Liveness says the process is up. Readiness says it can reach its database, so traffic is only routed when it can serve.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapMetrics();

// Off unless a deployment asks for it: the API surface is not advertised by default.
if (configuration.GetValue("ApiDocs:Enabled", false))
{
    app.MapOpenApi();
    app.UseSwaggerUI(options =>
    {
        options.RoutePrefix = "swagger";
        options.SwaggerEndpoint("/openapi/v1.json", "ACME Bank Dispute Portal API v1");
        options.DocumentTitle = "ACME Bank Dispute Portal API";
    });
}

await app.RunAsync();
return 0;
