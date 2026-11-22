using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
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
                await using var scope = scopeFactory.CreateAsyncScope();
                await RunCycleAsync(scope.ServiceProvider, stoppingToken);
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

/// <summary>The simulated back office: sends submitted disputes to the scheme, then applies the scheme's answer.</summary>
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

        var submittedBefore = now.AddSeconds(-Options.SendDelaySeconds);
        var toSend = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM disputes.disputes
            WHERE status = 'Submitted' AND submitted_at <= {submittedBefore}
            ORDER BY submitted_at
            LIMIT {Options.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        var sentBefore = now.AddSeconds(-Options.OutcomeDelaySeconds);
        var toComplete = await db.Database.SqlQuery<Guid>($"""
            SELECT id AS "Value" FROM disputes.disputes
            WHERE status = 'SentToScheme' AND sent_to_scheme_at <= {sentBefore}
            ORDER BY sent_to_scheme_at
            LIMIT {Options.BatchSize}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(cancellationToken);

        if (toSend.Count == 0 && toComplete.Count == 0)
        {
            return;
        }

        var claimed = toSend.Concat(toComplete).ToList();
        var disputes = await db.Disputes.Include(d => d.Events).Where(d => claimed.Contains(d.Id)).ToListAsync(cancellationToken);

        foreach (var dispute in disputes)
        {
            if (dispute.Status == DisputeStatus.Submitted)
            {
                processing.SendToScheme(dispute);
            }
            else
            {
                processing.ApplySchemeOutcome(dispute);
            }

            Logger.LogInformation("Dispute {Reference} moved to {Status}", dispute.Reference, dispute.Status);
        }

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}

/// <summary>Delivers outbox messages to their consumers, at least once and in the order they occurred.</summary>
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
    }
}
