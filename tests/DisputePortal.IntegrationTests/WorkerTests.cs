using DisputePortal.IntegrationTests.Infrastructure;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Notifications.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The background workers against the real database. Their timers are not running: each test runs the cycles
/// itself and moves the clock, so nothing here sleeps. Every cycle is a new worker instance with its own
/// database connection and transaction, which is what two replicas of the service are to PostgreSQL.
/// </summary>
[Collection(PortalCollection.Name)]
public class WorkerTests(PortalFixture portal) : PortalTest(portal)
{
    private const int Many = 40;

    /// <summary>Long enough for a cycle, short enough to fail quickly when a cycle waits for a lock it should have skipped.</summary>
    private static readonly TimeSpan CycleTimeout = TimeSpan.FromSeconds(15);

    private static readonly TimeSpan SchemeAnswerTime = TimeSpan.FromSeconds(20);

    private DisputeProcessingOptions SmallBatches
    {
        get
        {
            // The configured options with a small batch, so that the work takes several cycles and the workers overlap.
            var configured = Portal.App.Services.GetRequiredService<IOptions<DisputeProcessingOptions>>().Value;
            return new DisputeProcessingOptions
            {
                BatchSize = 3,
                OutcomeDelaySeconds = configured.OutcomeDelaySeconds,
                AutoApproveAfterSeconds = configured.AutoApproveAfterSeconds,
                IdempotencyKeyRetentionHours = configured.IdempotencyKeyRetentionHours,
            };
        }
    }

    [Fact]
    public async Task The_scheme_answer_is_applied_once_the_scheme_has_had_time_to_answer()
    {
        var disputes = await Portal.AddDisputesAsync(
            TestDisputes.SentToScheme(Clock.Now),
            TestDisputes.SentToScheme(Clock.Now, schemeReference: DemoData.DeclinedTransactionId));

        Clock.Advance(SchemeAnswerTime - TimeSpan.FromSeconds(1));
        await ProcessDisputesAsync();
        (await StatusesAsync()).Values.ShouldAllBe(status => status == DisputeStatus.SentToScheme);

        Clock.Advance(TimeSpan.FromSeconds(1));
        await ProcessDisputesAsync();

        var stored = await Portal.WithDisputesAsync(db => db.Disputes.Include(d => d.Events).ToDictionaryAsync(d => d.Id));
        var refunded = stored[disputes[0].Id];
        refunded.Status.ShouldBe(DisputeStatus.Refunded);
        refunded.CompletedAt.ShouldBe(Clock.Now);
        refunded.Events.Single(e => e.Status == DisputeStatus.Refunded).ActorType.ShouldBe(DisputeActorType.System);

        // The simulator declines disputes of the transactions on its decline list.
        var declined = stored[disputes[1].Id];
        declined.Status.ShouldBe(DisputeStatus.Declined);
        declined.OutcomeNote.ShouldBe(SimulatedSchemeGateway.DebitOrderDeclineNote);
    }

    [Fact]
    public async Task A_submitted_dispute_waits_for_an_agent_however_long_it_takes()
    {
        await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now));

        Clock.Advance(TimeSpan.FromDays(30));
        await ProcessDisputesAsync();

        (await StatusesAsync()).Values.ShouldBe([DisputeStatus.Submitted]);
    }

    [Fact]
    public async Task With_automatic_approval_switched_on_a_dispute_is_approved_by_the_system_after_the_delay()
    {
        var options = SmallBatches;
        options.AutoApproveAfterSeconds = 60;
        var dispute = (await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now)))[0];

        Clock.Advance(TimeSpan.FromSeconds(59));
        await ProcessDisputesAsync(options);
        (await StatusesAsync()).Values.ShouldBe([DisputeStatus.Submitted]);

        Clock.Advance(TimeSpan.FromSeconds(1));
        await ProcessDisputesAsync(options);

        var stored = await Portal.WithDisputesAsync(db => db.Disputes.Include(d => d.Events).SingleAsync(d => d.Id == dispute.Id));
        stored.Status.ShouldBe(DisputeStatus.SentToScheme);
        stored.SchemePayload.ShouldNotBeNull();
        var approval = stored.Events.Single(e => e.Status == DisputeStatus.SentToScheme);
        approval.ActorType.ShouldBe(DisputeActorType.System);
        approval.ActorSubjectId.ShouldBeNull();
    }

    [Fact]
    public async Task A_dispute_another_worker_has_locked_is_skipped_and_not_waited_for()
    {
        var disputes = await Portal.AddDisputesAsync(Enumerable.Range(0, 4).Select(_ => TestDisputes.SentToScheme(Clock.Now)).ToArray());
        var locked = disputes.Take(2).Select(d => d.Id).ToArray();
        var free = disputes.Skip(2).Select(d => d.Id).ToArray();
        Clock.Advance(SchemeAnswerTime);

        // Another worker is in the middle of its cycle: it has claimed two of the disputes and has not committed yet.
        await using (var otherWorker = await LockAsync("disputes.disputes", locked))
        {
            await ProcessDisputesAsync().WaitAsync(CycleTimeout);

            var statuses = await StatusesAsync();
            free.ShouldAllBe(id => statuses[id] == DisputeStatus.Refunded);
            locked.ShouldAllBe(id => statuses[id] == DisputeStatus.SentToScheme);
        }

        // The other worker gave up without finishing. Its disputes are picked up on the next cycle.
        await ProcessDisputesAsync();

        (await StatusesAsync()).Values.ShouldAllBe(status => status == DisputeStatus.Refunded);
        Metrics.Count(PortalMetrics.DisputesCompleted).ShouldBe(4);
    }

    [Fact]
    public async Task Two_workers_running_at_once_complete_each_dispute_exactly_once()
    {
        var disputes = await Portal.AddDisputesAsync(Enumerable.Range(0, Many).Select(_ => TestDisputes.SentToScheme(Clock.Now)).ToArray());
        Clock.Advance(SchemeAnswerTime);

        // A dispute completed twice would fail its second save on the row version, and that would fail the cycle here.
        await Task.WhenAll(
            Task.Run(() => RunUntilAsync(() => ProcessDisputesAsync(SmallBatches), NoDisputeIsWithTheSchemeAsync)),
            Task.Run(() => RunUntilAsync(() => ProcessDisputesAsync(SmallBatches), NoDisputeIsWithTheSchemeAsync)));

        var stored = await Portal.WithDisputesAsync(db => db.Disputes.Include(d => d.Events).ToListAsync());
        stored.Count.ShouldBe(Many);
        stored.ShouldAllBe(d => d.Status == DisputeStatus.Refunded && d.Events.Count(e => e.Status == DisputeStatus.Refunded) == 1);
        Metrics.Count(PortalMetrics.DisputesCompleted).ShouldBe(Many);

        // One message per status change: submitted, sent and refunded, for each dispute.
        var messages = await Portal.WithDisputesAsync(db => db.OutboxMessages.Where(m => m.Type == nameof(DisputeStatusChanged)).ToListAsync());
        messages.Count.ShouldBe(Many * 3);
        disputes.ShouldAllBe(d => messages.Count(m => m.Payload.Contains(d.Id.ToString()) && m.Payload.Contains("\"Refunded\"")) == 1);
    }

    [Fact]
    public async Task Two_dispatchers_running_at_once_deliver_each_outbox_message_exactly_once()
    {
        await Portal.AddDisputesAsync(Enumerable.Range(0, Many).Select(_ => TestDisputes.Submitted(Clock.Now)).ToArray());

        await Task.WhenAll(
            Task.Run(() => RunUntilAsync(() => DispatchOutboxAsync(SmallBatches), NoMessageIsWaitingAsync)),
            Task.Run(() => RunUntilAsync(() => DispatchOutboxAsync(SmallBatches), NoMessageIsWaitingAsync)));

        // A message delivered twice would show a second attempt, and a second count on the delivery counter.
        var messages = await Portal.WithDisputesAsync(db => db.OutboxMessages.ToListAsync());
        messages.Count.ShouldBe(Many);
        messages.ShouldAllBe(m => m.ProcessedAt != null && m.Attempts == 1);
        Metrics.Count(PortalMetrics.OutboxMessagesDispatched).ShouldBe(Many);
        Metrics.Count(PortalMetrics.OutboxMessagesFailed).ShouldBe(0);

        var notifications = await Portal.WithNotificationsAsync(db => db.Notifications.ToListAsync());
        notifications.Select(n => n.SourceMessageId).ShouldBe(messages.Select(m => m.Id), ignoreOrder: true);
    }

    [Fact]
    public async Task An_outbox_message_another_dispatcher_has_locked_is_skipped_and_not_waited_for()
    {
        await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now), TestDisputes.Submitted(Clock.Now));
        var messages = await Portal.WithDisputesAsync(db => db.OutboxMessages.Select(m => m.Id).ToListAsync());

        await using (var otherDispatcher = await LockAsync("disputes.outbox_messages", [messages[0]]))
        {
            await DispatchOutboxAsync().WaitAsync(CycleTimeout);

            (await Portal.WithDisputesAsync(db => db.OutboxMessages.Where(m => m.ProcessedAt != null).Select(m => m.Id).ToListAsync())).ShouldBe([messages[1]]);
        }

        await DispatchOutboxAsync();

        (await Portal.WithDisputesAsync(db => db.OutboxMessages.ToListAsync())).ShouldAllBe(m => m.ProcessedAt != null && m.Attempts == 1);
        (await Portal.WithNotificationsAsync(db => db.Notifications.CountAsync())).ShouldBe(2);
    }

    [Fact]
    public async Task A_message_that_was_delivered_but_not_marked_is_not_delivered_to_the_inbox_twice()
    {
        // Delivery is at least once: a dispatcher can stop after the consumer saved and before the message is marked.
        await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now));
        await DispatchOutboxAsync();
        await Portal.WithDisputesAsync(db => db.Database.ExecuteSqlRawAsync("UPDATE disputes.outbox_messages SET processed_at = NULL, attempts = 0"));

        await DispatchOutboxAsync();

        (await Portal.WithDisputesAsync(db => db.OutboxMessages.SingleAsync())).ProcessedAt.ShouldNotBeNull();
        (await Portal.WithNotificationsAsync(db => db.Notifications.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_delivery_is_retried_with_backoff_and_abandoned_after_the_last_attempt()
    {
        // No consumer exists for this type, so every delivery fails, as it would while a consumer is down.
        var failing = new OutboxMessage(Guid.NewGuid(), "SomethingNobodyConsumes", "{}", Clock.Now);
        await Portal.WithDisputesAsync(db =>
        {
            db.OutboxMessages.Add(failing);
            return db.SaveChangesAsync();
        });
        var firstAttemptAt = Clock.Now;

        // Attempt 1 fails at once. Each retry waits twice as long as the one before: 10, 20, 40 and 80 seconds.
        await DispatchOutboxAsync();
        var message = await FindAsync(failing.Id);
        message.Attempts.ShouldBe(1);
        message.ProcessedAt.ShouldBeNull();
        message.LastError.ShouldBe("There is no consumer for outbox messages of type 'SomethingNobodyConsumes'.");
        message.NextAttemptAt.ShouldBe(firstAttemptAt.AddSeconds(10));

        foreach (var (wait, attempt) in new[] { (10, 2), (20, 3), (40, 4), (80, 5) })
        {
            // One second early nothing happens.
            Clock.Advance(TimeSpan.FromSeconds(wait - 1));
            await DispatchOutboxAsync();
            (await FindAsync(failing.Id)).Attempts.ShouldBe(attempt - 1);
            Metrics.Count(PortalMetrics.OutboxMessagesAbandoned).ShouldBe(0);

            Clock.Advance(TimeSpan.FromSeconds(1));
            await DispatchOutboxAsync();
            (await FindAsync(failing.Id)).Attempts.ShouldBe(attempt);
        }

        // The fifth failure was the last attempt. The message is counted as abandoned once, which is what the alert watches.
        message = await FindAsync(failing.Id);
        message.IsAbandoned.ShouldBeTrue();
        Metrics.Count(PortalMetrics.OutboxMessagesAbandoned).ShouldBe(1);
        Metrics.Count(PortalMetrics.OutboxMessagesFailed).ShouldBe(5);

        // It is never picked up again, and is not counted again.
        Clock.Advance(TimeSpan.FromDays(1));
        await DispatchOutboxAsync();
        (await FindAsync(failing.Id)).Attempts.ShouldBe(OutboxMessage.MaxAttempts);
        Metrics.Count(PortalMetrics.OutboxMessagesAbandoned).ShouldBe(1);
        Metrics.Count(PortalMetrics.OutboxMessagesFailed).ShouldBe(5);
    }

    [Fact]
    public async Task A_dispatch_cycle_that_cannot_save_counts_nothing_and_the_retry_counts_once()
    {
        await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now));

        // The delivery itself works. Saving its result does not, as when the database connection drops before the commit.
        await Portal.WithDisputesAsync(db => db.Database.ExecuteSqlRawAsync(
            "ALTER TABLE disputes.outbox_messages ADD CONSTRAINT test_save_fails CHECK (attempts = 0) NOT VALID"));
        try
        {
            await Should.ThrowAsync<DbUpdateException>(() => DispatchOutboxAsync());
        }
        finally
        {
            await Portal.WithDisputesAsync(db => db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE disputes.outbox_messages DROP CONSTRAINT test_save_fails"));
        }

        // The cycle rolled back: the message still waits, and nothing was counted for it.
        (await Portal.WithDisputesAsync(db => db.OutboxMessages.SingleAsync())).Attempts.ShouldBe(0);
        Metrics.Count(PortalMetrics.OutboxMessagesDispatched).ShouldBe(0);
        Metrics.Count(PortalMetrics.OutboxMessagesFailed).ShouldBe(0);

        await DispatchOutboxAsync();

        (await Portal.WithDisputesAsync(db => db.OutboxMessages.SingleAsync())).ProcessedAt.ShouldNotBeNull();
        Metrics.Count(PortalMetrics.OutboxMessagesDispatched).ShouldBe(1);
    }

    [Fact]
    public async Task A_failing_message_does_not_hold_up_the_messages_behind_it()
    {
        await Portal.WithDisputesAsync(db =>
        {
            db.OutboxMessages.Add(new OutboxMessage(Guid.NewGuid(), "SomethingNobodyConsumes", "{}", Clock.Now.AddMinutes(-1)));
            return db.SaveChangesAsync();
        });
        var dispute = (await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now)))[0];

        await DispatchOutboxAsync();

        var notification = (await Portal.WithNotificationsAsync(db => db.Notifications.ToListAsync())).ShouldHaveSingleItem();
        notification.RelatedDisputeId.ShouldBe(dispute.Id);
        notification.Category.ShouldBe(NotificationCategory.DisputeReceived);
        Metrics.Count(PortalMetrics.OutboxMessagesDispatched).ShouldBe(1);
        Metrics.Count(PortalMetrics.OutboxMessagesFailed).ShouldBe(1);
    }

    [Fact]
    public async Task The_idempotency_key_clean_up_deletes_only_keys_past_their_retention_period()
    {
        // Keys are kept for 24 hours.
        var now = Clock.Now;
        await Portal.WithDisputesAsync(db =>
        {
            db.IdempotencyRecords.AddRange(
                Key("two-days-old", now.AddDays(-2)),
                Key("just-expired", now.AddHours(-24).AddSeconds(-1)),
                Key("on-the-limit", now.AddHours(-24)),
                Key("an-hour-old", now.AddHours(-1)),
                Key("new", now));
            return db.SaveChangesAsync();
        });

        await CleanUpIdempotencyKeysAsync();

        var kept = await Portal.WithDisputesAsync(db => db.IdempotencyRecords.Select(r => r.Key).ToListAsync());
        kept.ShouldBe(["on-the-limit", "an-hour-old", "new"], ignoreOrder: true);

        // Time passes, and the next pass takes the keys that have expired since.
        Clock.Advance(TimeSpan.FromHours(23) + TimeSpan.FromSeconds(1));
        await CleanUpIdempotencyKeysAsync();

        kept = await Portal.WithDisputesAsync(db => db.IdempotencyRecords.Select(r => r.Key).ToListAsync());
        kept.ShouldBe(["new"]);

        static IdempotencyRecord Key(string key, DateTimeOffset createdAt) =>
            new(DemoData.Thandi, key, new string('A', 64), Guid.NewGuid(), createdAt);
    }

    /// <summary>
    /// Holds row locks the way a worker does in the middle of a cycle, on a connection of its own.
    /// Disposing the returned connection rolls back and releases them.
    /// </summary>
    private async Task<NpgsqlConnection> LockAsync(string table, Guid[] ids)
    {
        var connection = new NpgsqlConnection(Portal.ConnectionString);
        await connection.OpenAsync();
        await connection.BeginTransactionAsync();

        await using var command = new NpgsqlCommand($"SELECT id FROM {table} WHERE id = ANY($1) FOR UPDATE", connection);
        command.Parameters.Add(new NpgsqlParameter { Value = ids });
        await command.ExecuteNonQueryAsync();
        return connection;
    }

    private static async Task RunUntilAsync(Func<Task> cycle, Func<Task<bool>> done)
    {
        // Far more cycles than the work needs. A worker that never finishes fails the test instead of hanging it.
        for (var cycles = 0; cycles < Many * 5; cycles++)
        {
            if (await done())
            {
                return;
            }

            await cycle();
        }

        throw new TimeoutException("The work was not finished after the expected number of cycles.");
    }

    private Task<bool> NoDisputeIsWithTheSchemeAsync() =>
        Portal.WithDisputesAsync(async db => !await db.Disputes.AnyAsync(d => d.Status == DisputeStatus.SentToScheme));

    private Task<bool> NoMessageIsWaitingAsync() =>
        Portal.WithDisputesAsync(async db => !await db.OutboxMessages.AnyAsync(m => m.ProcessedAt == null));

    private Task<Dictionary<Guid, DisputeStatus>> StatusesAsync() =>
        Portal.WithDisputesAsync(db => db.Disputes.ToDictionaryAsync(d => d.Id, d => d.Status));

    private Task<OutboxMessage> FindAsync(Guid messageId) =>
        Portal.WithDisputesAsync(db => db.OutboxMessages.AsNoTracking().SingleAsync(m => m.Id == messageId));
}
