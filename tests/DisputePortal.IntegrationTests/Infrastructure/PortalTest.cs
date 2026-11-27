using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>
/// Base of every integration test. Each test starts from the same data and the same moment, and counts
/// the business metrics recorded while it runs.
/// </summary>
public abstract class PortalTest(PortalFixture portal) : IAsyncLifetime
{
    private protected PortalFixture Portal { get; } = portal;

    private protected TestClock Clock => Portal.Clock;

    private protected MetricProbe Metrics { get; } = new();

    public virtual Task InitializeAsync() => Portal.ResetAsync();

    public Task DisposeAsync()
    {
        Metrics.Dispose();
        return Task.CompletedTask;
    }

    /// <summary>Runs one cycle of the worker that approves automatically (when switched on) and applies the scheme's answers.</summary>
    private protected Task ProcessDisputesAsync(DisputeProcessingOptions? options = null, PortalApplication? application = null) =>
        RunAsync<DisputeProcessorWorker>(worker => worker.RunOnceAsync(CancellationToken.None), options, application);

    /// <summary>Runs one cycle of the worker that delivers outbox messages.</summary>
    private protected Task DispatchOutboxAsync(DisputeProcessingOptions? options = null, PortalApplication? application = null) =>
        RunAsync<OutboxDispatcherWorker>(worker => worker.RunOnceAsync(CancellationToken.None), options, application);

    /// <summary>Runs one pass of the worker that deletes expired idempotency keys.</summary>
    private protected Task CleanUpIdempotencyKeysAsync() =>
        RunAsync<IdempotencyKeyCleanupWorker>(worker => worker.DeleteExpiredAsync(CancellationToken.None), options: null, application: null);

    /// <summary>
    /// Creates the worker as the host would, from the application's services, without starting its timer.
    /// Each call is a separate instance, as each replica of the service has its own.
    /// </summary>
    private async Task RunAsync<TWorker>(Func<TWorker, Task> run, DisputeProcessingOptions? options, PortalApplication? application)
        where TWorker : IDisposable
    {
        var services = (application ?? Portal.App).Services;
        using var worker = options is null
            ? ActivatorUtilities.CreateInstance<TWorker>(services)
            : ActivatorUtilities.CreateInstance<TWorker>(services, Options.Create(options));

        await run(worker);
    }
}
