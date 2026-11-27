using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DisputePortal.Modules.Disputes.Infrastructure;

/// <summary>
/// Base for the polling workers. Each cycle runs in its own scope and transaction. Rows are claimed with
/// FOR UPDATE SKIP LOCKED, so any number of replicas can run the same worker without processing a row twice.
/// </summary>
internal abstract class PollingWorker(IServiceScopeFactory scopeFactory, IOptions<DisputeProcessingOptions> options, ILogger logger)
    : BackgroundService
{
    protected DisputeProcessingOptions Options { get; } = options.Value;

    protected ILogger Logger { get; } = logger;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(Options.PollIntervalSeconds, 1)));

        // On shutdown the timer is cancelled between cycles, so a cycle in progress finishes its transaction first.
        while (await WaitAsync(timer, stoppingToken))
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // A failed cycle rolls back and is retried on the next tick. The worker itself must not die.
                Logger.LogError(exception, "{Worker} cycle failed", GetType().Name);
            }
        }
    }

    /// <summary>Runs one cycle in a scope of its own. The loop above does this on every tick; the integration tests call it directly.</summary>
    internal async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        await RunCycleAsync(scope.ServiceProvider, cancellationToken);
    }

    protected abstract Task RunCycleAsync(IServiceProvider services, CancellationToken cancellationToken);

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken stoppingToken)
    {
        try
        {
            return await timer.WaitForNextTickAsync(stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}

/// <summary>
/// The simulated scheme: applies the scheme's answer to disputes that were sent for processing. A submitted dispute
/// waits for an agent. Only when automatic approval is switched on does this worker also approve disputes that
/// have waited long enough, which lets the demo run end to end without an agent.
/// </summary>
internal sealed class DisputeProcessorWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DisputeProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<DisputeProcessorWorker> logger)
    : PollingWorker(scopeFactory, options, logger)
{
    protected override async Task RunCycleAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<DisputesDbContext>();
        var processing = services.GetRequiredService<DisputeProcessingService>();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // The row lock makes a customer's withdrawal or an agent's decision on the same dispute wait for this
        // transaction. Its save then finds a newer row version and fails, so the automatic approval stands.
        List<Guid> toApprove = [];
        if (Options.AutoApproveCutoff(now) is { } submittedBefore)
        {
            toApprove = await db.Database.SqlQuery<Guid>($"""
                SELECT id AS "Value" FROM disputes.disputes
                WHERE status = 'Submitted' AND submitted_at <= {submittedBefore}
                ORDER BY submitted_at
                LIMIT {Options.BatchSize}
                FOR UPDATE SKIP LOCKED
                """).ToListAsync(cancellationToken);
        }

        var sentBefore = now.AddSeconds(-Options.OutcomeDelaySeconds);
        var toComplete = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM disputes.disputes
            WHERE status = 'SentToScheme' AND sent_to_scheme_at <= {sentBefore}
            ORDER BY sent_to_scheme_at
            LIMIT {Options.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        if (toApprove.Count == 0 && toComplete.Count == 0)
        {
            return;
        }

        var claimed = toApprove.Concat(toComplete).ToList();
        var disputes = await db.Disputes.Include(d => d.Events).Where(d => claimed.Contains(d.Id)).ToListAsync(cancellationToken);

        foreach (var dispute in disputes)
        {
            if (dispute.Status == DisputeStatus.Submitted)
            {
                processing.SendToScheme(dispute, DisputeActor.System);
            }
            else
            {
                processing.ApplySchemeOutcome(dispute);
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Counted and logged only after the commit, as the request handlers do. A cycle that fails rolls back
        // and is retried, and must not be counted for work that was never saved.
        foreach (var dispute in disputes)
        {
            if (dispute.Status == DisputeStatus.SentToScheme)
            {
                ReviewMetrics.Approved(DisputeActorType.System);
            }
            else
            {
                PortalMetrics.DisputesCompleted.Add(1, new KeyValuePair<string, object?>("outcome", dispute.Status.ToString()));
            }

            Logger.LogInformation("Dispute {Reference} moved to {Status}", dispute.Reference, dispute.Status);
        }
    }
}

/// <summary>
/// Delivers outbox messages to their consumers, at least once. Each cycle takes the oldest waiting messages first,
/// but order is not guaranteed: a message that failed is retried later, behind newer ones, and several
/// dispatchers each deliver their own batch at the same time. Consumers must not depend on the order.
/// </summary>
internal sealed class OutboxDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DisputeProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxDispatcherWorker> logger)
    : PollingWorker(scopeFactory, options, logger)
{
    protected override async Task RunCycleAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var db = services.GetRequiredService<DisputesDbContext>();
        var processor = services.GetRequiredService<OutboxProcessor>();
        var now = timeProvider.GetUtcNow();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var claimed = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM disputes.outbox_messages
            WHERE processed_at IS NULL AND attempts < {OutboxMessage.MaxAttempts} AND next_attempt_at <= {now}
            ORDER BY occurred_at
            LIMIT {Options.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        if (claimed.Count == 0)
        {
            return;
        }

        var messages = await db.OutboxMessages.Where(m => claimed.Contains(m.Id)).OrderBy(m => m.OccurredAt).ToListAsync(cancellationToken);
        foreach (var message in messages)
        {
            await processor.ProcessAsync(message, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        // Counted only after the commit, as the dispute worker does. A cycle that fails rolls back and is retried.
        foreach (var message in messages)
        {
            processor.RecordCommitted(message);
        }
    }
}

/// <summary>
/// Deletes idempotency keys past their retention period so the table does not grow for ever.
/// A key only has to outlive the client's retries. After that the unique index on the transaction
/// still stops a second dispute. The delete is safe to run from several replicas at once.
/// </summary>
internal sealed class IdempotencyKeyCleanupWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<DisputeProcessingOptions> options,
    TimeProvider timeProvider,
    ILogger<IdempotencyKeyCleanupWorker> logger)
    : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval, timeProvider);

        try
        {
            do
            {
                await DeleteExpiredAsync(stoppingToken);
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>One clean-up pass. The loop above runs it every hour; the integration tests call it directly.</summary>
    internal async Task DeleteExpiredAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DisputesDbContext>();
            var cutoff = IdempotencyRecord.ExpiryCutoff(timeProvider.GetUtcNow(), options.Value.IdempotencyKeyRetentionHours);

            var deleted = await db.IdempotencyRecords.Where(r => r.CreatedAt < cutoff).ExecuteDeleteAsync(cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Deleted {Count} idempotency keys created before {Cutoff}", deleted, cutoff);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A failed clean-up is retried on the next tick. The worker itself must not die.
            logger.LogError(exception, "Idempotency key clean-up failed");
        }
    }
}
