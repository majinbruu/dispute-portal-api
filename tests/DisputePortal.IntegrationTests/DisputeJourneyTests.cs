using System.Net;
using System.Text.Json;
using DisputePortal.IntegrationTests.Infrastructure;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Notifications.Application;
using DisputePortal.Modules.Notifications.Domain;
using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// A dispute from the customer's first click to the outcome in their inbox, through the HTTP API only,
/// with the workers' cycles run between the steps. Time moves a minute at a time so the order of events is fixed.
/// </summary>
[Collection(PortalCollection.Name)]
public class DisputeJourneyTests(PortalFixture portal) : PortalTest(portal)
{
    private const string Disputes = "/api/v1/disputes";
    private const string AgentDisputes = "/api/v1/agent/disputes";
    private const string Inbox = "/api/v1/notifications";

    private static readonly TimeSpan SchemeAnswerTime = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task A_debit_order_dispute_is_raised_discussed_approved_and_refunded()
    {
        var customer = Portal.Thandi();
        var agent = Portal.Agent();

        // The customer finds the debit order and the reasons offered for it.
        var listed = await customer.GetAsync<PagedResult<TransactionDto>>("/api/v1/transactions?type=DebitOrder&mandateType=Registered&pageSize=100");
        var debitOrder = listed.Items.Single(t => t.Id == DemoData.RefundedDebitOrderId);
        debitOrder.CounterpartyName.ShouldBe("Virgin Active South Africa");
        debitOrder.Eligibility.CanDispute.ShouldBeTrue();

        var reasons = await customer.GetAsync<List<DisputeReasonDto>>($"/api/v1/dispute-reasons?transactionId={debitOrder.Id}");
        reasons.Select(r => (r.Id, r.Code)).ShouldBe(
        [
            ("not_authorised", "MD01"),
            ("incorrect_amount", "AM09"),
            ("wrong_date", "DT01"),
            ("cancelled", "MD06"),
            ("duplicate", "AM05"),
        ]);

        // The customer raises the dispute with a comment.
        var submitted = await (await customer.SubmitDisputeAsync(debitOrder.Id, "not_authorised", "  I cancelled this membership in June.  "))
            .ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        submitted.Status.ShouldBe(DisputeStatus.Submitted);
        submitted.TransactionType.ShouldBe(DisputedTransactionType.DebitOrder);
        submitted.ReasonCode.ShouldBe("MD01");
        submitted.Amount.ShouldBe(875.00m);
        submitted.MandateType.ShouldBe("Registered");
        submitted.Comments.ShouldHaveSingleItem().Body.ShouldBe("I cancelled this membership in June.");
        var disputePath = $"{Disputes}/{submitted.Id}";
        var agentPath = $"{AgentDisputes}/{submitted.Id}";

        // The transaction now points at its dispute and cannot be disputed again.
        var disputed = await customer.GetAsync<TransactionDto>($"/api/v1/transactions/{debitOrder.Id}");
        disputed.Dispute!.Reference.ShouldBe(submitted.Reference);
        disputed.Eligibility.CanDispute.ShouldBeFalse();

        // An agent finds it at the head of the review queue, asks a question, and the customer answers.
        Clock.Advance(TimeSpan.FromMinutes(1));
        var queue = await agent.GetAsync<PagedResult<AgentDisputeSummaryDto>>(AgentDisputes);
        queue.Items.ShouldHaveSingleItem().Id.ShouldBe(submitted.Id);

        var afterQuestion = await (await agent.PostAsync($"{agentPath}/comments", new AddCommentRequest("When did you cancel, and how?")))
            .ReadAsync<AgentDisputeDetailDto>();
        afterQuestion.Comments.Select(c => (c.AuthorType, c.AuthorSubjectId)).ShouldBe(
        [
            (DisputeActorType.Customer, DemoData.Thandi),
            (DisputeActorType.Agent, DemoData.Zanele),
        ]);

        Clock.Advance(TimeSpan.FromMinutes(1));
        (await customer.PostAsync($"{disputePath}/comments", new AddCommentRequest("By email on 12 June."))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The agent approves. The pacs.004 return is generated and kept with the dispute.
        Clock.Advance(TimeSpan.FromMinutes(1));
        var approved = await (await agent.PostAsync($"{agentPath}/approve")).ReadAsync<AgentDisputeDetailDto>();
        approved.Status.ShouldBe(DisputeStatus.SentToScheme);
        approved.SentToSchemeAt.ShouldBe(Clock.Now);

        var returnMessage = JsonDocument.Parse((await StoredAsync(submitted.Id)).SchemePayload!).RootElement;
        returnMessage.GetProperty("MsgDefIdr").GetString().ShouldBe("pacs.004.001.11");
        returnMessage.GetProperty("TxInf").GetProperty("RtrRsnInf").GetProperty("Rsn").GetProperty("Cd").GetString().ShouldBe("MD01");

        // The simulated scheme answers after its delay, and the outbox delivers everything that happened.
        Clock.Advance(SchemeAnswerTime);
        await ProcessDisputesAsync();
        await DispatchOutboxAsync();

        var final = await customer.GetAsync<DisputeDetailDto>(disputePath);
        final.Status.ShouldBe(DisputeStatus.Refunded);
        final.State.ShouldBe(DisputeState.Completed);
        final.CompletedAt.ShouldBe(Clock.Now);
        final.OutcomeNote.ShouldBe("Your account has been credited.");
        final.Events.Select(e => (e.Status, e.Actor)).ShouldBe(
        [
            (DisputeStatus.Submitted, DisputeActorType.Customer),
            (DisputeStatus.SentToScheme, DisputeActorType.Agent),
            (DisputeStatus.Refunded, DisputeActorType.System),
        ]);
        final.Comments.Select(c => (c.AuthorType, c.Body)).ShouldBe(
        [
            (DisputeActorType.Customer, "I cancelled this membership in June."),
            (DisputeActorType.Agent, "When did you cancel, and how?"),
            (DisputeActorType.Customer, "By email on 12 June."),
        ]);

        // The audit trail names the agent. The customer's view above never does.
        var audit = await agent.GetAsync<AgentDisputeDetailDto>(agentPath);
        audit.Events.Select(e => e.ActorSubjectId).ShouldBe([DemoData.Thandi, DemoData.Zanele, null]);

        // The inbox, newest first: one message per status change, and one for the agent's comment.
        var inbox = await customer.GetAsync<PagedResult<NotificationDto>>(Inbox);
        inbox.Items.Select(n => n.Category).ShouldBe(
        [
            NotificationCategory.DisputeRefunded,
            NotificationCategory.DisputeInProgress,
            NotificationCategory.DisputeComment,
            NotificationCategory.DisputeReceived,
        ]);
        inbox.Items.ShouldAllBe(n => n.RelatedDisputeId == submitted.Id && !n.IsRead);
        inbox.Items[0].Body.ShouldBe($"Dispute {submitted.Reference} is successful. Your account has been credited with R875.00.");
        inbox.Items[2].Body.ShouldBe($"An agent added a comment to dispute {submitted.Reference}. Open the dispute to read it and reply.");
        inbox.Items[3].Body.ShouldBe($"Your dispute {submitted.Reference} for R875.00 collected by Virgin Active South Africa has been received.");
        (await customer.GetAsync<UnreadCountDto>($"{Inbox}/unread-count")).Unread.ShouldBe(4);

        // A closed dispute takes no more comments and cannot be withdrawn.
        await (await customer.PostAsync($"{disputePath}/comments", new AddCommentRequest("Thank you."))).ShouldBeProblemAsync(HttpStatusCode.Conflict, "dispute_closed");
        await (await customer.PostAsync($"{disputePath}/withdraw")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "dispute_not_withdrawable");
    }

    [Fact]
    public async Task A_card_purchase_dispute_is_raised_discussed_approved_and_refunded()
    {
        var customer = Portal.Thandi();
        var agent = Portal.Agent();

        // An online purchase without 3-D Secure, so "I did not make this transaction" is offered, with Visa's code for it.
        var listed = await customer.GetAsync<PagedResult<TransactionDto>>("/api/v1/transactions?type=Card&cardStatus=Settled&pageSize=100");
        var purchase = listed.Items.Single(t => t.Id == DemoData.PendingCardId);
        purchase.CounterpartyName.ShouldBe("Takealot");
        purchase.Card!.Scheme.ShouldBe(CardScheme.Visa);
        purchase.Eligibility.CanDispute.ShouldBeTrue();

        var reasons = await customer.GetAsync<List<DisputeReasonDto>>($"/api/v1/dispute-reasons?transactionId={purchase.Id}");
        reasons.Select(r => (r.Id, r.Code)).ShouldBe(
        [
            ("not_authorised", "10.4"),
            ("duplicate", "12.6"),
            ("incorrect_amount", "12.5"),
            ("not_received", "13.1"),
            ("credit_not_processed", "13.6"),
        ]);

        var submitted = await (await customer.SubmitDisputeAsync(purchase.Id, "not_authorised", "I have never ordered from Takealot with this card."))
            .ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        submitted.TransactionType.ShouldBe(DisputedTransactionType.Card);
        submitted.ReasonCode.ShouldBe("10.4");
        submitted.CardScheme.ShouldBe("Visa");
        submitted.MandateType.ShouldBeNull();
        submitted.Amount.ShouldBe(1899.00m);
        var agentPath = $"{AgentDisputes}/{submitted.Id}";

        Clock.Advance(TimeSpan.FromMinutes(1));
        var review = await agent.GetAsync<AgentDisputeDetailDto>(agentPath);
        review.CustomerId.ShouldBe(DemoData.Thandi);
        review.Transaction.Type.ShouldBe(DisputedTransactionType.Card);
        review.Transaction.SchemeReference.ShouldBe(purchase.Card.AcquirerReferenceNumber);
        review.Transaction.PostingDate.ShouldBe(purchase.Card.PostingDate);

        (await agent.PostAsync($"{agentPath}/comments", new AddCommentRequest("Do you still have the card with you?"))).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The agent approves. A chargeback record takes the place of the pacs.004.
        Clock.Advance(TimeSpan.FromMinutes(1));
        (await agent.PostAsync($"{agentPath}/approve")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var chargeback = JsonDocument.Parse((await StoredAsync(submitted.Id)).SchemePayload!).RootElement;
        chargeback.GetProperty("recordType").GetString().ShouldBe("Chargeback");
        chargeback.GetProperty("scheme").GetString().ShouldBe("Visa");
        chargeback.GetProperty("reasonCode").GetString().ShouldBe("10.4");
        chargeback.GetProperty("acquirerReferenceNumber").GetString().ShouldBe(purchase.Card.AcquirerReferenceNumber);

        Clock.Advance(SchemeAnswerTime);
        await ProcessDisputesAsync();
        await DispatchOutboxAsync();

        var final = await customer.GetAsync<DisputeDetailDto>($"{Disputes}/{submitted.Id}");
        final.Status.ShouldBe(DisputeStatus.Refunded);
        final.Events.Select(e => (e.Status, e.Actor)).ShouldBe(
        [
            (DisputeStatus.Submitted, DisputeActorType.Customer),
            (DisputeStatus.SentToScheme, DisputeActorType.Agent),
            (DisputeStatus.Refunded, DisputeActorType.System),
        ]);
        final.Comments.Select(c => c.AuthorType).ShouldBe([DisputeActorType.Customer, DisputeActorType.Agent]);

        var inbox = await customer.GetAsync<PagedResult<NotificationDto>>(Inbox);
        inbox.Items.Select(n => n.Category).ShouldBe(
        [
            NotificationCategory.DisputeRefunded,
            NotificationCategory.DisputeInProgress,
            NotificationCategory.DisputeComment,
            NotificationCategory.DisputeReceived,
        ]);
        inbox.Items[0].Body.ShouldBe($"Dispute {submitted.Reference} is successful. Your account has been credited with R1,899.00.");
        inbox.Items[3].Body.ShouldBe($"Your dispute {submitted.Reference} for R1,899.00 at Takealot has been received.");

        // Reading a notification is remembered.
        var read = await (await customer.PostAsync($"{Inbox}/{inbox.Items[0].Id}/read")).ReadAsync<NotificationDto>();
        read.IsRead.ShouldBeTrue();
        (await customer.GetAsync<UnreadCountDto>($"{Inbox}/unread-count")).Unread.ShouldBe(3);
    }

    [Fact]
    public async Task A_card_dispute_the_scheme_declines_ends_declined_with_the_merchants_answer()
    {
        var customer = Portal.Thandi();
        var submitted = await (await customer.SubmitDisputeAsync(DemoData.DeclinedCardId, "not_received")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        (await Portal.Agent().PostAsync($"{AgentDisputes}/{submitted.Id}/approve")).StatusCode.ShouldBe(HttpStatusCode.OK);

        Clock.Advance(SchemeAnswerTime);
        await ProcessDisputesAsync();
        await DispatchOutboxAsync();

        var final = await customer.GetAsync<DisputeDetailDto>($"{Disputes}/{submitted.Id}");
        final.Status.ShouldBe(DisputeStatus.Declined);
        final.OutcomeNote.ShouldBe("The merchant provided proof that the charge is valid. Contact the merchant.");

        var inbox = await customer.GetAsync<PagedResult<NotificationDto>>(Inbox);
        inbox.Items[0].Category.ShouldBe(NotificationCategory.DisputeDeclined);
        inbox.Items[0].Body.ShouldBe(
            $"Dispute {submitted.Reference} for R2,499.00 was declined. The merchant provided proof that the charge is valid. Contact the merchant.");
    }

    [Fact]
    public async Task An_agents_rejection_declines_the_dispute_with_the_agents_reason_and_sends_nothing_to_the_scheme()
    {
        var customer = Portal.Thandi();
        var submitted = await (await customer.SubmitDisputeAsync(DemoData.PendingDebitOrderId, "incorrect_amount")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);

        Clock.Advance(TimeSpan.FromMinutes(1));
        var rejected = await (await Portal.Agent().PostAsync(
            $"{AgentDisputes}/{submitted.Id}/reject", new RejectDisputeRequest("  The amount matches your package.  "))).ReadAsync<AgentDisputeDetailDto>();
        await DispatchOutboxAsync();

        rejected.Status.ShouldBe(DisputeStatus.Declined);
        rejected.Events.Last().ActorSubjectId.ShouldBe(DemoData.Zanele);

        var final = await customer.GetAsync<DisputeDetailDto>($"{Disputes}/{submitted.Id}");
        final.Status.ShouldBe(DisputeStatus.Declined);
        final.OutcomeNote.ShouldBe("The amount matches your package.");
        (await StoredAsync(submitted.Id)).SchemePayload.ShouldBeNull();

        // Nothing is left for the worker, however long it waits.
        Clock.Advance(TimeSpan.FromDays(1));
        await ProcessDisputesAsync();
        (await StoredAsync(submitted.Id)).Status.ShouldBe(DisputeStatus.Declined);

        var inbox = await customer.GetAsync<PagedResult<NotificationDto>>(Inbox);
        inbox.Items.Select(n => n.Category).ShouldBe([NotificationCategory.DisputeDeclined, NotificationCategory.DisputeReceived]);
        inbox.Items[0].Body.ShouldBe($"Dispute {submitted.Reference} for R929.00 was declined. The amount matches your package.");

        // A second decision on the same dispute is refused.
        await (await Portal.Agent().PostAsync($"{AgentDisputes}/{submitted.Id}/approve")).ShouldBeProblemAsync(HttpStatusCode.Conflict, "dispute_not_awaiting_review");
    }

    [Fact]
    public async Task A_withdrawal_is_confirmed_in_the_inbox_and_nothing_is_sent_to_the_scheme()
    {
        var customer = Portal.Thandi();
        var submitted = await (await customer.SubmitDisputeAsync(DemoData.PendingCardId, "duplicate")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);

        Clock.Advance(TimeSpan.FromMinutes(1));
        var withdrawn = await (await customer.PostAsync($"{Disputes}/{submitted.Id}/withdraw")).ReadAsync<DisputeDetailDto>();
        await DispatchOutboxAsync();

        withdrawn.Status.ShouldBe(DisputeStatus.Withdrawn);
        withdrawn.Events.Last().Actor.ShouldBe(DisputeActorType.Customer);
        (await StoredAsync(submitted.Id)).SchemePayload.ShouldBeNull();

        var inbox = await customer.GetAsync<PagedResult<NotificationDto>>(Inbox);
        inbox.Items[0].Category.ShouldBe(NotificationCategory.DisputeWithdrawn);
        inbox.Items[0].Body.ShouldBe($"Dispute {submitted.Reference} for R1,899.00 at Takealot was withdrawn. Nothing was sent for processing.");
    }

    [Fact]
    public async Task The_agents_queue_spans_customers_and_can_be_filtered_searched_and_sorted()
    {
        var card = await (await Portal.Sipho().SubmitDisputeAsync(DemoData.SiphoPendingCardId, "not_received")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var approved = await (await Portal.Thandi().SubmitDisputeAsync(DemoData.RefundedDebitOrderId, "not_authorised")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        Clock.Advance(TimeSpan.FromMinutes(1));
        var waiting = await (await Portal.Thandi().SubmitDisputeAsync(DemoData.PendingDebitOrderId, "incorrect_amount")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        var agent = Portal.Agent();
        (await agent.PostAsync($"{AgentDisputes}/{approved.Id}/approve")).StatusCode.ShouldBe(HttpStatusCode.OK);

        // The review queue: disputes waiting for review first, oldest first, then the rest.
        (await QueueAsync(string.Empty)).ShouldBe([card.Id, waiting.Id, approved.Id]);
        (await QueueAsync("?status=Submitted")).ShouldBe([card.Id, waiting.Id]);
        (await QueueAsync("?transactionType=Card")).ShouldBe([card.Id]);
        (await QueueAsync($"?reference={waiting.Reference[^6..].ToLowerInvariant()}")).ShouldBe([waiting.Id]);
        (await QueueAsync("?sort=-amount")).ShouldBe([waiting.Id, approved.Id, card.Id]);
        (await QueueAsync("?sort=-submittedAt&pageSize=1&page=2")).ShouldBe([approved.Id]);

        async Task<List<Guid>> QueueAsync(string query) =>
            (await agent.GetAsync<PagedResult<AgentDisputeSummaryDto>>($"{AgentDisputes}{query}")).Items.Select(d => d.Id).ToList();
    }

    private Task<Dispute> StoredAsync(Guid disputeId) =>
        Portal.WithDisputesAsync(db => db.Disputes.AsNoTracking().SingleAsync(d => d.Id == disputeId));
}
