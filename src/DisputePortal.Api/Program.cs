using System.Diagnostics;
using System.Text.Json.Serialization;
using DisputePortal.Api.Hosting;
using DisputePortal.Modules.DebitOrders;
using DisputePortal.Modules.Disputes;
using DisputePortal.Modules.Notifications;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Prometheus;
using Scalar.AspNetCore;

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

builder.Services.AddCustomerAuthentication(configuration);
builder.Services.AddPortalRateLimiting(configuration);

builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "ACME Bank Dispute Portal API";
    document.Info.Version = "v1";
    document.Info.Description =
        "Debit orders, mandates, disputes and notifications for the signed-in customer. " +
        "All endpoints under /api require a Keycloak access token with the 'customer' role. " +
        "Errors are RFC 9457 problem documents with a stable 'code' and a 'traceId'.";
    return Task.CompletedTask;
}));

builder.Services
    .AddDebitOrdersModule(connectionString)
    .AddDisputesModule(configuration, connectionString)
    .AddNotificationsModule(connectionString);

builder.Services.AddHealthChecks()
    .AddDebitOrdersHealthChecks()
    .AddDisputesHealthChecks()
    .AddNotificationsHealthChecks();

// Business and runtime metrics are published from .NET meters. The Npgsql meter is left out because
// its labels carry the connection string (host, database and user name).
Metrics.ConfigureMeterAdapter(options => options.InstrumentFilterPredicate = instrument => instrument.Meter.Name != "Npgsql");

var app = builder.Build();

// The meter listener starts on the first collection. Collect once now so measurements made during
// start-up, before the first scrape, are not lost.
await Metrics.DefaultRegistry.CollectAndExportAsTextAsync(Stream.Null);

// Schema migrations run at start-up only where configured (Docker Compose). In Kubernetes a Job runs
// the same image with --migrate-only before the deployment rolls, so replicas never race to migrate.
var migrateOnly = args.Contains("--migrate-only");
if (migrateOnly || configuration.GetValue("Database:MigrateOnStartup", false))
{
    await app.Services.MigrateDebitOrdersAsync();
    await app.Services.MigrateDisputesAsync();
    await app.Services.MigrateNotificationsAsync();
}

if (configuration.GetValue("Seed:Enabled", false))
{
    await app.Services.SeedDebitOrdersAsync();
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

app.MapDebitOrdersEndpoints();
app.MapDisputesEndpoints();
app.MapNotificationsEndpoints();

// Liveness says the process is up. Readiness says it can reach its database, so traffic is only routed when it can serve.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions { Predicate = check => check.Tags.Contains("ready") });
app.MapMetrics();

if (configuration.GetValue("ApiDocs:Enabled", true))
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

await app.RunAsync();
return 0;
