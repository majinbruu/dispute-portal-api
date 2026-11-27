using System.Net;
using System.Text.Json;
using DisputePortal.IntegrationTests.Infrastructure;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Disputes.Infrastructure;
using DisputePortal.Modules.Notifications.Application;
using DisputePortal.Modules.Notifications.Domain;
using DisputePortal.Modules.Notifications.Infrastructure;
using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.Modules.Transactions.Infrastructure;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Old = DisputePortal.IntegrationTests.Infrastructure.VersionOneDatabase;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// The application's own migrate path (Database:MigrateOnStartup, the same code the --migrate-only job runs),
/// against an empty database and against a database that version 1 left behind with data in it.
/// </summary>
[Collection(PortalCollection.Name)]
public class MigrationTests(PortalFixture portal) : PortalTest(portal)
{
    [Fact]
    public async Task Every_module_migrates_an_empty_database_from_nothing()
    {
        var connectionString = await Portal.CreateDatabaseAsync("from_nothing");

        await using var application = Portal.StartApplication(connectionString, migrateOnStartup: true);

        await ShouldBeFullyMigratedAsync<TransactionsDbContext>(application);
        await ShouldBeFullyMigratedAsync<DisputesDbContext>(application);
        await ShouldBeFullyMigratedAsync<NotificationsDbContext>(application);
        (await application.CreateClient().GetAsync("/health/ready")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Migrating_a_second_time_changes_nothing()
    {
        var connectionString = await Portal.CreateDatabaseAsync("migrated_twice");
        await using (Portal.StartApplication(connectionString, migrateOnStartup: true))
        {
        }

        await using var application = Portal.StartApplication(connectionString, migrateOnStartup: true);

        await ShouldBeFullyMigratedAsync<TransactionsDbContext>(application);
        await ShouldBeFullyMigratedAsync<DisputesDbContext>(application);
        await ShouldBeFullyMigratedAsync<NotificationsDbContext>(application);
    }

    [Fact]
    public async Task A_version_1_database_is_upgraded_with_its_data()
    {
        var connectionString = await Portal.CreateDatabaseAsync("from_version_1");
        await Old.MigrateAsync(connectionString);
        await Old.InsertRowsAsync(connectionString);

        await using var application = Portal.StartApplication(connectionString, migrateOnStartup: true);

        // Every migration is recorded once, and the old schema is gone with nothing left behind in it.
        await ShouldBeFullyMigratedAsync<TransactionsDbContext>(application);
        await ShouldBeFullyMigratedAsync<DisputesDbContext>(application);
        await ShouldBeFullyMigratedAsync<NotificationsDbContext>(application);
        (await ScalarAsync<bool>(connectionString, "SELECT EXISTS (SELECT 1 FROM pg_namespace WHERE nspname = 'debit_orders')")).ShouldBeFalse();

        // Debit orders: the shared columns moved to the transaction base, and a rejected collection is now refused by the stored decision.
        var debitOrders = await PortalFixture.WithAsync(application, (TransactionsDbContext db) => db.DebitOrders.ToDictionaryAsync(d => d.Id));
        debitOrders.Count.ShouldBe(8);

        var accepted = debitOrders[Old.AcceptedRegistered];
        accepted.Type.ShouldBe(TransactionType.DebitOrder);
        accepted.CustomerId.ShouldBe(Old.Customer);
        accepted.CounterpartyName.ShouldBe("Vodacom");
        accepted.AccountMasked.ShouldBe("******7890");
        accepted.Amount.ShouldBe(599.00m);
        accepted.Currency.ShouldBe("ZAR");
        accepted.EffectiveDate.ShouldBe(Old.ActionDate(daysAgo: 7));
        accepted.LastDisputeDate.ShouldBe(Old.ActionDate(daysAgo: 7).AddDays(60));
        accepted.CreatedAt.ShouldBe(Old.CreatedAt);
        accepted.MandateType.ShouldBe(DebitOrderType.Registered);
        accepted.MandateId.ShouldBe(Old.RegisteredMandate);
        accepted.MandateReference.ShouldBe("MRN000000001");
        accepted.ContractReference.ShouldBe("VOD-7741203");
        accepted.TransactionId.ShouldBe("ACME-V1-0001");
        accepted.Status.ShouldBe(DebitOrderStatus.Accepted);

        ShouldBeDecided(accepted, disputable: true, DisputabilityReason.RegisteredMandate);
        ShouldBeDecided(debitOrders[Old.RejectedRegistered], disputable: false, DisputabilityReason.CollectionRejected);
        ShouldBeDecided(debitOrders[Old.DebiCheckWithinTerms], disputable: false, DisputabilityReason.WithinMandateTerms);
        ShouldBeDecided(debitOrders[Old.DebiCheckAboveMaximum], disputable: true, DisputabilityReason.AmountAboveMandateMaximum);
        ShouldBeDecided(debitOrders[Old.RejectedDebiCheckWithinTerms], disputable: false, DisputabilityReason.CollectionRejected);
        debitOrders[Old.DebiCheckAboveMaximum].ContractReference.ShouldBe("MTN-C-88120345");

        var mandates = await PortalFixture.WithAsync(application, (TransactionsDbContext db) => db.Mandates.ToListAsync());
        mandates.Select(m => m.Id).ShouldBe([Old.RegisteredMandate, Old.DebiCheckMandate], ignoreOrder: true);

        // Disputes: renamed columns keep their values, and each ISO reason code became its reason ID.
        var disputes = await PortalFixture.WithAsync(application, (DisputesDbContext db) => db.Disputes.Include(d => d.Events).ToDictionaryAsync(d => d.Id));
        disputes.Count.ShouldBe(5);
        disputes.Values.ShouldAllBe(d => d.TransactionType == DisputedTransactionType.DebitOrder && d.CardScheme == null && d.PostingDate == null);
        Old.Disputes.Select(old => (old.ReasonCode, disputes[old.Id].ReasonId)).ShouldBe(
        [
            ("MD01", "not_authorised"),
            ("AM09", "incorrect_amount"),
            ("DT01", "wrong_date"),
            ("MD06", "cancelled"),
            ("AM05", "duplicate"),
        ]);

        var refunded = disputes[Old.RefundedDispute.Id];
        refunded.TransactionId.ShouldBe(Old.DebiCheckAboveMaximum);
        refunded.SchemeReference.ShouldBe("ACME-V1-0004");
        refunded.OriginalEndToEndId.ShouldBe("E2E-ACME-V1-0004");
        refunded.CounterpartyName.ShouldBe("MTN South Africa");
        refunded.AccountMasked.ShouldBe("******7890");
        refunded.TransactionDate.ShouldBe(Old.ActionDate(daysAgo: 20));
        refunded.MandateType.ShouldBe("DebiCheck");
        refunded.MandateReference.ShouldBe("MRN000000002");
        refunded.ReasonCode.ShouldBe("AM09");
        refunded.Status.ShouldBe(DisputeStatus.Refunded);
        refunded.Amount.ShouldBe(899.00m);
        JsonDocument.Parse(refunded.SchemePayload!).RootElement.GetProperty("MsgDefIdr").GetString().ShouldBe("pacs.004.001.11");

        // Events from before actors existed: the customer raised the dispute, and the worker did everything after that.
        refunded.Events.OrderBy(e => e.OccurredAt).Select(e => (e.Status, e.ActorType, e.ActorSubjectId)).ShouldBe(
        [
            (DisputeStatus.Submitted, DisputeActorType.Customer, Old.Customer),
            (DisputeStatus.SentToScheme, DisputeActorType.System, null),
            (DisputeStatus.Refunded, DisputeActorType.System, null),
        ]);

        // Outbox messages still waiting to be delivered are readable by the new consumers.
        await DispatchOutboxAsync(application: application);

        var messages = await PortalFixture.WithAsync(application, (DisputesDbContext db) => db.OutboxMessages.ToDictionaryAsync(m => m.Id));
        messages.Values.ShouldAllBe(m => m.ProcessedAt != null && m.LastError == null);
        messages[Old.UndeliveredSubmittedMessage].Attempts.ShouldBe(1);
        messages[Old.FailedOnceDeclinedMessage].Attempts.ShouldBe(2);
        messages[Old.DeliveredRefundedMessage].Attempts.ShouldBe(1);

        var notifications = await PortalFixture.WithAsync(application, (NotificationsDbContext db) => db.Notifications.ToDictionaryAsync(n => n.SourceMessageId));
        notifications.Count.ShouldBe(3);
        notifications[Old.DeliveredRefundedMessage].Id.ShouldBe(Old.ExistingNotification);
        notifications[Old.UndeliveredSubmittedMessage].Category.ShouldBe(NotificationCategory.DisputeReceived);
        notifications[Old.UndeliveredSubmittedMessage].Body.ShouldBe(
            $"Your dispute {Old.SubmittedDispute.Reference} for R599.00 collected by Vodacom has been received.");
        notifications[Old.FailedOnceDeclinedMessage].Body.ShouldBe(
            $"Dispute {Old.DeclinedDispute.Reference} for R599.00 was declined. {Old.DeclineNote}");

        // The upgraded data works through the API.
        var customerToken = Portal.Identity.TokenFor(Old.Customer, ApiConventions.CustomerRole);
        var customer = Portal.ClientWithToken(customerToken, application);
        var agent = Portal.ClientWithToken(Portal.Identity.TokenFor(DemoData.Zanele, ApiConventions.AgentRole), application);

        var listed = await customer.GetAsync<PagedResult<TransactionDto>>("/api/v1/transactions?type=DebitOrder&mandateType=Registered");
        listed.TotalItems.ShouldBe(5);
        var listedAccepted = listed.Items.Single(t => t.Id == Old.AcceptedRegistered);
        listedAccepted.Dispute!.Reference.ShouldBe(Old.SubmittedDispute.Reference);
        listedAccepted.Eligibility.Code.ShouldBe(DisputeEligibility.AlreadyDisputed);
        listed.Items.Single(t => t.Id == Old.RejectedRegistered).Eligibility.Code.ShouldBe(DisputeEligibility.CollectionRejected);

        var inbox = await customer.GetAsync<PagedResult<NotificationDto>>("/api/v1/notifications");
        inbox.TotalItems.ShouldBe(3);

        var agentView = await agent.GetAsync<AgentDisputeDetailDto>($"/api/v1/agent/disputes/{Old.SubmittedDispute.Id}");
        agentView.ReasonLabel.ShouldBe("I did not authorise this debit order");
        agentView.Events.ShouldHaveSingleItem().ActorSubjectId.ShouldBe(Old.Customer);

        // A request from before the upgrade, retried after it with the same idempotency key, still returns its dispute.
        var replay = await customer.SubmitDisputeAsync(Old.AcceptedRegistered, "not_authorised", idempotencyKey: Old.IdempotencyKey);
        (await replay.ReadAsync<DisputeDetailDto>(HttpStatusCode.Created)).Id.ShouldBe(Old.SubmittedDispute.Id);
        replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);

        // The dispute that was with the scheme during the upgrade is completed by the new worker.
        Clock.Advance(TimeSpan.FromMinutes(1));
        await ProcessDisputesAsync(application: application);
        (await customer.GetAsync<DisputeDetailDto>($"/api/v1/disputes/{Old.SentDispute.Id}")).Status.ShouldBe(DisputeStatus.Refunded);

        // The one-dispute-per-transaction index now leaves out withdrawn disputes, also for a dispute raised by version 1.
        (await customer.PostAsync($"/api/v1/disputes/{Old.SubmittedDispute.Id}/withdraw")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await customer.SubmitDisputeAsync(Old.AcceptedRegistered, "duplicate")).StatusCode.ShouldBe(HttpStatusCode.Created);
        (await customer.SubmitDisputeAsync(Old.AcceptedRegistered, "duplicate")).StatusCode.ShouldBe(HttpStatusCode.Conflict);
    }

    private static void ShouldBeDecided(DebitOrder debitOrder, bool disputable, DisputabilityReason reason)
    {
        debitOrder.IsDisputable.ShouldBe(disputable);
        debitOrder.DisputabilityReason.ShouldBe(reason);
    }

    /// <summary>Every migration in the assembly is applied, and the migrations add up to the model the code uses.</summary>
    private static async Task ShouldBeFullyMigratedAsync<TContext>(PortalApplication application)
        where TContext : DbContext
    {
        await PortalFixture.WithAsync(application, async (TContext db) =>
        {
            var defined = db.Database.GetMigrations().ToList();
            defined.ShouldNotBeEmpty();
            (await db.Database.GetAppliedMigrationsAsync()).ShouldBe(defined);
            db.Database.HasPendingModelChanges().ShouldBeFalse();
            return true;
        });
    }

    private static async Task<T> ScalarAsync<T>(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }
}
