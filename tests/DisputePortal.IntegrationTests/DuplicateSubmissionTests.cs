using System.Net;
using DisputePortal.IntegrationTests.Infrastructure;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// One dispute per transaction, however the requests arrive. The check in the handler is not enough when
/// requests arrive together; the unique index on the transaction and the idempotency key decide.
/// </summary>
[Collection(PortalCollection.Name)]
public class DuplicateSubmissionTests(PortalFixture portal) : PortalTest(portal)
{
    private const int SimultaneousRequests = 12;

    private static readonly Guid Transaction = DemoData.RefundedDebitOrderId;

    [Fact]
    public async Task Simultaneous_submissions_for_one_transaction_create_exactly_one_dispute()
    {
        var customer = Portal.Thandi();

        // Each request has its own idempotency key, as a second browser tab or a second device would.
        var responses = await Task.WhenAll(Enumerable.Range(0, SimultaneousRequests)
            .Select(_ => Task.Run(() => customer.SubmitDisputeAsync(Transaction, "not_authorised"))));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).ShouldBe(1);
        foreach (var refused in responses.Where(r => r.StatusCode != HttpStatusCode.Created))
        {
            await refused.ShouldBeProblemAsync(HttpStatusCode.Conflict, "already_disputed");
        }

        // Nothing of the refused requests was written: one dispute, one key, one message to deliver.
        (await Portal.WithDisputesAsync(db => db.Disputes.CountAsync(d => d.TransactionId == Transaction))).ShouldBe(1);
        (await Portal.WithDisputesAsync(db => db.IdempotencyRecords.CountAsync())).ShouldBe(1);
        (await Portal.WithDisputesAsync(db => db.OutboxMessages.CountAsync(m => m.Type == nameof(DisputeStatusChanged)))).ShouldBe(1);
        Metrics.Count(PortalMetrics.DisputesSubmitted).ShouldBe(1);
    }

    [Fact]
    public async Task A_request_replayed_with_the_same_idempotency_key_returns_the_same_dispute()
    {
        var customer = Portal.Thandi();
        var first = await customer.SubmitDisputeAsync(Transaction, "not_authorised", "It was not me.", idempotencyKey: "key-1");
        var original = await first.ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);

        var replay = await customer.SubmitDisputeAsync(Transaction, "not_authorised", "It was not me.", idempotencyKey: "key-1");

        (await replay.ReadAsync<DisputeDetailDto>(HttpStatusCode.Created)).Id.ShouldBe(original.Id);
        replay.Headers.GetValues("Idempotent-Replayed").ShouldBe(["true"]);
        first.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
        (await Portal.WithDisputesAsync(db => db.Disputes.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task The_same_request_sent_simultaneously_with_one_idempotency_key_returns_one_dispute_to_every_caller()
    {
        var customer = Portal.Thandi();

        // A client that timed out and retried while its first attempt was still running.
        var responses = await Task.WhenAll(Enumerable.Range(0, SimultaneousRequests)
            .Select(_ => Task.Run(() => customer.SubmitDisputeAsync(Transaction, "not_authorised", idempotencyKey: "key-1"))));

        var disputes = new List<DisputeDetailDto>();
        foreach (var response in responses)
        {
            disputes.Add(await response.ReadAsync<DisputeDetailDto>(HttpStatusCode.Created));
        }

        disputes.Select(d => d.Id).Distinct().ShouldHaveSingleItem();
        (await Portal.WithDisputesAsync(db => db.Disputes.CountAsync())).ShouldBe(1);
        responses.Count(r => !r.Headers.Contains("Idempotent-Replayed")).ShouldBe(1);
    }

    [Fact]
    public async Task An_idempotency_key_reused_for_a_different_request_is_refused()
    {
        var customer = Portal.Thandi();
        (await customer.SubmitDisputeAsync(Transaction, "not_authorised", idempotencyKey: "key-1")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var differentReason = await customer.SubmitDisputeAsync(Transaction, "duplicate", idempotencyKey: "key-1");
        var differentTransaction = await customer.SubmitDisputeAsync(DemoData.PendingCardId, "not_authorised", idempotencyKey: "key-1");
        var differentComment = await customer.SubmitDisputeAsync(Transaction, "not_authorised", "One more thing.", idempotencyKey: "key-1");

        await differentReason.ShouldBeProblemAsync(HttpStatusCode.Conflict, "idempotency_key_reused");
        await differentTransaction.ShouldBeProblemAsync(HttpStatusCode.Conflict, "idempotency_key_reused");
        await differentComment.ShouldBeProblemAsync(HttpStatusCode.Conflict, "idempotency_key_reused");
        (await Portal.WithDisputesAsync(db => db.Disputes.CountAsync())).ShouldBe(1);
    }

    [Fact]
    public async Task Two_customers_may_use_the_same_idempotency_key()
    {
        var thandi = await Portal.Thandi().SubmitDisputeAsync(Transaction, "not_authorised", idempotencyKey: "key-1");
        var sipho = await Portal.Sipho().SubmitDisputeAsync(DemoData.SiphoPendingCardId, "not_received", idempotencyKey: "key-1");

        thandi.StatusCode.ShouldBe(HttpStatusCode.Created);
        sipho.StatusCode.ShouldBe(HttpStatusCode.Created);
        sipho.Headers.Contains("Idempotent-Replayed").ShouldBeFalse();
    }

    [Fact]
    public async Task A_submission_without_an_idempotency_key_is_refused()
    {
        var response = await Portal.Thandi().PostAsync("/api/v1/disputes", new SubmitDisputeRequest(Transaction, "not_authorised"));

        await response.ShouldBeProblemAsync(HttpStatusCode.BadRequest, "validation_failed");
        (await Portal.WithDisputesAsync(db => db.Disputes.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task A_second_dispute_for_a_disputed_transaction_is_refused()
    {
        var customer = Portal.Thandi();
        (await customer.SubmitDisputeAsync(Transaction, "not_authorised")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var second = await customer.SubmitDisputeAsync(Transaction, "duplicate");

        await second.ShouldBeProblemAsync(HttpStatusCode.Conflict, "already_disputed");
    }
}
