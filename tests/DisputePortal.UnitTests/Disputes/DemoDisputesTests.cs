using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Disputes.Infrastructure;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Disputes;

/// <summary>The demo data is what a reviewer sees first, so what it promises to show is checked here.</summary>
public class DemoDisputesTests
{
    private static readonly DateTimeOffset Now = DisputeTestData.Now;

    private readonly IReadOnlyList<Dispute> _disputes = DemoDisputes.Build(SeededTransactions(), Now);

    [Fact]
    public void There_is_one_dispute_for_each_demo_transaction_and_it_belongs_to_that_transactions_customer()
    {
        _disputes.Select(d => (d.TransactionId, d.CustomerId)).ShouldBe(DemoDisputes.Transactions, ignoreOrder: true);
    }

    [Theory]
    [InlineData("DebitOrder")]
    [InlineData("Card")]
    public void Each_transaction_type_starts_with_a_dispute_waiting_one_in_processing_one_refunded_and_one_withdrawn(string type)
    {
        var statuses = OfType(type).Select(d => d.Status).ToList();

        statuses.ShouldContain(DisputeStatus.Submitted);
        statuses.ShouldContain(DisputeStatus.SentToScheme);
        statuses.ShouldContain(DisputeStatus.Refunded);
        statuses.ShouldContain(DisputeStatus.Withdrawn);
    }

    [Theory]
    [InlineData("DebitOrder")]
    [InlineData("Card")]
    public void The_dispute_left_in_processing_is_declined_by_the_simulated_scheme_so_every_status_is_shown(string type)
    {
        var processing = new DisputeProcessingService(
            new SimulatedSchemeGateway(Options.Create(new SchemeSimulatorOptions
            {
                DeclineSchemeReferences = [DemoData.DeclinedTransactionId, DemoData.DeclinedCardReference],
            })),
            new FakeTimeProvider(Now.AddSeconds(20)));
        var sent = OfType(type).Single(d => d.Status == DisputeStatus.SentToScheme);

        processing.ApplySchemeOutcome(sent);

        sent.Status.ShouldBe(DisputeStatus.Declined);
        sent.Events.Last().ActorType.ShouldBe(DisputeActorType.System);
    }

    [Fact]
    public void One_dispute_was_rejected_by_the_demo_agent_with_a_reason()
    {
        var rejected = _disputes.Single(d => d.Status == DisputeStatus.Declined);

        rejected.TransactionId.ShouldBe(DemoData.AgentRejectedDebitOrderId);
        rejected.OutcomeNote.ShouldNotBeNullOrWhiteSpace();
        rejected.Events.Last().ActorType.ShouldBe(DisputeActorType.Agent);
        rejected.Events.Last().ActorSubjectId.ShouldBe(DemoData.Zanele);
    }

    [Fact]
    public void One_dispute_has_a_conversation_between_the_customer_and_the_agent()
    {
        var pending = _disputes.Single(d => d.TransactionId == DemoData.PendingDebitOrderId);

        pending.Status.ShouldBe(DisputeStatus.Submitted);
        pending.Comments.OrderBy(c => c.CreatedAt).Select(c => (c.AuthorType, c.AuthorSubjectId)).ShouldBe(
        [
            (DisputeActorType.Customer, DemoData.Thandi),
            (DisputeActorType.Agent, DemoData.Zanele),
            (DisputeActorType.Customer, DemoData.Thandi),
        ]);
        pending.DomainEvents.OfType<AgentCommentAddedDomainEvent>().Count().ShouldBe(1);
    }

    [Fact]
    public void The_agents_queue_has_disputes_from_more_than_one_customer_waiting()
    {
        _disputes.Where(d => d.IsAwaitingReview).Select(d => d.CustomerId).Distinct().ShouldBe([DemoData.Thandi, DemoData.Sipho], ignoreOrder: true);
    }

    [Fact]
    public void Every_timeline_runs_forward_starts_with_the_customer_and_ends_no_later_than_now()
    {
        foreach (var dispute in _disputes)
        {
            var events = dispute.Events.ToList();

            events[0].Status.ShouldBe(DisputeStatus.Submitted);
            events[0].ActorType.ShouldBe(DisputeActorType.Customer);
            events[0].ActorSubjectId.ShouldBe(dispute.CustomerId);
            events.Select(e => e.OccurredAt).ShouldBeInOrder();
            events[^1].OccurredAt.ShouldBeLessThanOrEqualTo(Now);
            dispute.Comments.ShouldAllBe(c => c.CreatedAt >= dispute.SubmittedAt && c.CreatedAt <= Now);
        }
    }

    [Fact]
    public void Every_approval_was_made_by_the_demo_agent_and_stored_what_was_sent()
    {
        var approvals = _disputes.SelectMany(d => d.Events).Where(e => e.Status == DisputeStatus.SentToScheme).ToList();

        approvals.Count.ShouldBe(4);
        approvals.ShouldAllBe(e => e.ActorType == DisputeActorType.Agent && e.ActorSubjectId == DemoData.Zanele);
        _disputes.Where(d => d.SentToSchemeAt is not null).ShouldAllBe(d => d.SchemePayload != null);
    }

    [Fact]
    public void A_demo_transaction_that_is_missing_is_skipped_rather_than_failing_start_up()
    {
        var onlyOne = SeededTransactions().Where(t => t.Key == DemoData.PendingCardId).ToDictionary();

        DemoDisputes.Build(onlyOne, Now).Select(d => d.TransactionId).ShouldBe([DemoData.PendingCardId]);
        DemoDisputes.Build(new Dictionary<Guid, TransactionForDispute>(), Now).ShouldBeEmpty();
    }

    private IEnumerable<Dispute> OfType(string type) => _disputes.Where(d => d.TransactionType.ToString() == type);

    /// <summary>The demo transactions as the Transactions module's seeder creates them, reduced to what a dispute needs.</summary>
    private static Dictionary<Guid, TransactionForDispute> SeededTransactions()
    {
        TransactionForDispute[] transactions =
        [
            ContractData.DebitOrder() with { Id = DemoData.RefundedDebitOrderId, CustomerId = DemoData.Thandi },
            ContractData.DebitOrder() with { Id = DemoData.PendingDebitOrderId, CustomerId = DemoData.Thandi },
            ContractData.DebitOrder() with { Id = DemoData.DeclinedDebitOrderId, CustomerId = DemoData.Thandi, SchemeReference = DemoData.DeclinedTransactionId },
            ContractData.DebitOrder() with { Id = DemoData.WithdrawnDebitOrderId, CustomerId = DemoData.Thandi },
            ContractData.DebitOrder() with
            {
                Id = DemoData.AgentRejectedDebitOrderId,
                CustomerId = DemoData.Thandi,
                DebitOrder = new DebitOrderForDispute("COJ-550218734-20261020", "Eft", null),
            },
            ContractData.Card() with { Id = DemoData.PendingCardId, CustomerId = DemoData.Thandi },
            ContractData.Card(entryMode: "Recurring") with { Id = DemoData.RefundedCardId, CustomerId = DemoData.Thandi },
            ContractData.Card(entryMode: "Contactless") with { Id = DemoData.WithdrawnCardId, CustomerId = DemoData.Thandi },
            ContractData.Card(verification: "ThreeDSecure") with
            {
                Id = DemoData.DeclinedCardId,
                CustomerId = DemoData.Thandi,
                SchemeReference = DemoData.DeclinedCardReference,
            },
            ContractData.Card(scheme: "Mastercard") with { Id = DemoData.SiphoPendingCardId, CustomerId = DemoData.Sipho },
        ];

        return transactions.ToDictionary(t => t.Id);
    }
}
