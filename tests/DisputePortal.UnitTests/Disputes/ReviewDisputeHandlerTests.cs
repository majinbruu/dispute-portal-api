using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Disputes;

public class ReviewDisputeHandlerTests
{
    private static readonly Guid Agent = DisputeTestData.Agent;
    private static readonly DateTimeOffset ReviewedAt = DisputeTestData.Now.AddHours(1);

    private readonly InMemoryDisputeRepository _disputes = new();
    private readonly FakeTimeProvider _clock = new(ReviewedAt);

    private ReviewDisputeHandler Handler => new(
        _disputes,
        new DisputeProcessingService(new SimulatedSchemeGateway(Options.Create(new SchemeSimulatorOptions())), _clock),
        _clock);

    [Fact]
    public async Task Approving_a_debit_order_dispute_sends_it_for_processing_with_a_pacs004()
    {
        var dispute = Add(DisputeTestData.Submitted());

        var result = await Handler.ApproveAsync(Agent, dispute.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(DisputeStatus.SentToScheme);
        result.Value.SentToSchemeAt.ShouldBe(ReviewedAt);
        result.Value.SchemePayload!.ShouldContain("pacs.004");
        _disputes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Approving_a_card_dispute_stores_a_chargeback_record()
    {
        var dispute = Add(DisputeTestData.Submitted(DisputeTestData.Card(), "duplicate"));

        var result = await Handler.ApproveAsync(Agent, dispute.Id, CancellationToken.None);

        result.Value.Status.ShouldBe(DisputeStatus.SentToScheme);
        result.Value.SchemePayload!.ShouldContain("Chargeback");
    }

    [Fact]
    public async Task An_approval_is_recorded_against_the_agent_who_made_it()
    {
        var dispute = Add(DisputeTestData.Submitted());

        await Handler.ApproveAsync(Agent, dispute.Id, CancellationToken.None);

        var approved = dispute.Events.Last();
        approved.Status.ShouldBe(DisputeStatus.SentToScheme);
        approved.ActorType.ShouldBe(DisputeActorType.Agent);
        approved.ActorSubjectId.ShouldBe(Agent);
        approved.OccurredAt.ShouldBe(ReviewedAt);
    }

    [Fact]
    public async Task Rejecting_declines_the_dispute_with_the_agents_reason_and_sends_nothing()
    {
        var dispute = Add(DisputeTestData.Submitted());

        var result = await Handler.RejectAsync(Agent, dispute.Id, "The mandate shows this amount was agreed.", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(DisputeStatus.Declined);
        result.Value.OutcomeNote.ShouldBe("The mandate shows this amount was agreed.");
        result.Value.CompletedAt.ShouldBe(ReviewedAt);
        result.Value.SchemePayload.ShouldBeNull();
        result.Value.Events.Last().ActorType.ShouldBe(DisputeActorType.Agent);
        result.Value.Events.Last().ActorSubjectId.ShouldBe(Agent);
        _disputes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task A_decision_raises_the_status_change_that_notifies_the_customer()
    {
        var approved = Add(DisputeTestData.Submitted());
        var rejected = Add(DisputeTestData.Submitted());
        approved.ClearDomainEvents();
        rejected.ClearDomainEvents();

        await Handler.ApproveAsync(Agent, approved.Id, CancellationToken.None);
        await Handler.RejectAsync(Agent, rejected.Id, "Not valid.", CancellationToken.None);

        approved.DomainEvents.ShouldBe([new DisputeStatusChangedDomainEvent(DisputeStatus.SentToScheme, ReviewedAt)]);
        rejected.DomainEvents.ShouldBe([new DisputeStatusChangedDomainEvent(DisputeStatus.Declined, ReviewedAt)]);
    }

    [Fact]
    public async Task An_agent_reviews_the_dispute_of_any_customer()
    {
        var dispute = Add(Dispute.Submit(
            Guid.NewGuid(), "DSP-20261122-XYZ789", Guid.NewGuid(), DisputeTestData.DebitOrder(), "duplicate", comment: null, DisputeTestData.Now));

        var result = await Handler.ApproveAsync(Agent, dispute.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task An_agent_cannot_approve_or_reject_a_dispute_they_raised_themselves()
    {
        // One user can hold both roles. Their agent ID and their customer ID are then the same token subject.
        var dispute = Add(DisputeTestData.Submitted());
        var eventsBefore = dispute.Events.Count;

        var approve = await Handler.ApproveAsync(DisputeTestData.Customer, dispute.Id, CancellationToken.None);
        var reject = await Handler.RejectAsync(DisputeTestData.Customer, dispute.Id, "Not valid.", CancellationToken.None);

        approve.Error!.Type.ShouldBe(ErrorType.Forbidden);
        approve.Error.Code.ShouldBe("own_dispute");
        reject.Error!.Type.ShouldBe(ErrorType.Forbidden);
        reject.Error.Code.ShouldBe("own_dispute");
        dispute.Status.ShouldBe(DisputeStatus.Submitted);
        dispute.Events.Count.ShouldBe(eventsBefore);
        _disputes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Reviewing_your_own_dispute_is_refused_whatever_its_status()
    {
        var dispute = Add(WithdrawDisputeHandlerTests.InStatus(DisputeStatus.Refunded));

        var approve = await Handler.ApproveAsync(DisputeTestData.Customer, dispute.Id, CancellationToken.None);

        approve.Error!.Code.ShouldBe("own_dispute");
    }

    [Fact]
    public async Task A_dispute_that_does_not_exist_is_not_found()
    {
        var approve = await Handler.ApproveAsync(Agent, Guid.NewGuid(), CancellationToken.None);
        var reject = await Handler.RejectAsync(Agent, Guid.NewGuid(), "Not valid.", CancellationToken.None);

        approve.Error!.Code.ShouldBe("dispute_not_found");
        approve.Error.Type.ShouldBe(ErrorType.NotFound);
        reject.Error!.Code.ShouldBe("dispute_not_found");
    }

    [Theory]
    [InlineData("SentToScheme")]
    [InlineData("Refunded")]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public async Task A_dispute_that_is_no_longer_waiting_for_review_cannot_be_approved_or_rejected(string statusName)
    {
        var status = Enum.Parse<DisputeStatus>(statusName);
        var dispute = Add(WithdrawDisputeHandlerTests.InStatus(status));
        var eventsBefore = dispute.Events.Count;

        var approve = await Handler.ApproveAsync(Agent, dispute.Id, CancellationToken.None);
        var reject = await Handler.RejectAsync(Agent, dispute.Id, "Not valid.", CancellationToken.None);

        approve.Error!.Type.ShouldBe(ErrorType.Conflict);
        approve.Error.Code.ShouldBe("dispute_not_awaiting_review");
        reject.Error!.Type.ShouldBe(ErrorType.Conflict);
        reject.Error.Code.ShouldBe("dispute_not_awaiting_review");
        dispute.Status.ShouldBe(status);
        dispute.Events.Count.ShouldBe(eventsBefore);
        _disputes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Losing_the_race_to_a_withdrawal_or_another_agent_is_a_conflict()
    {
        var approved = Add(DisputeTestData.Submitted());
        var rejected = Add(DisputeTestData.Submitted());

        _disputes.FailNextSaveAsConflict = true;
        var approve = await Handler.ApproveAsync(Agent, approved.Id, CancellationToken.None);
        _disputes.FailNextSaveAsConflict = true;
        var reject = await Handler.RejectAsync(Agent, rejected.Id, "Not valid.", CancellationToken.None);

        approve.Error!.Type.ShouldBe(ErrorType.Conflict);
        approve.Error.Code.ShouldBe("dispute_not_awaiting_review");
        reject.Error!.Code.ShouldBe("dispute_not_awaiting_review");
        _disputes.Saves.ShouldBe(0);
    }

    private Dispute Add(Dispute dispute)
    {
        _disputes.Disputes.Add(dispute);
        return dispute;
    }
}

public class AgentDisputeQueriesTests
{
    private static readonly Guid OtherCustomer = Guid.Parse("00000000-0000-4000-8000-0000000000cc");

    private readonly InMemoryDisputeRepository _disputes = new();

    private AgentDisputeQueries Queries => new(_disputes);

    [Fact]
    public async Task The_list_spans_all_customers()
    {
        Add("DSP-20261120-AAAAAA", DisputeTestData.Customer, daysAgo: 2);
        Add("DSP-20261121-BBBBBB", OtherCustomer, daysAgo: 1);

        var page = await Queries.ListAsync(new AgentDisputeListQuery(), CancellationToken.None);

        page.TotalItems.ShouldBe(2);
        page.Items.Select(d => d.CustomerId).ShouldBe([DisputeTestData.Customer, OtherCustomer]);
    }

    [Fact]
    public async Task By_default_disputes_waiting_for_review_come_first_and_the_oldest_first()
    {
        var oldRefunded = Add("DSP-20261101-AAAAAA", DisputeTestData.Customer, daysAgo: 20);
        oldRefunded.MarkSentToScheme("{}", DisputeActor.Agent(DisputeTestData.Agent), DisputeTestData.Now);
        oldRefunded.MarkRefunded(DisputeTestData.Now);
        Add("DSP-20261121-BBBBBB", OtherCustomer, daysAgo: 1);
        Add("DSP-20261119-CCCCCC", DisputeTestData.Customer, daysAgo: 3);
        var withdrawn = Add("DSP-20261110-DDDDDD", DisputeTestData.Customer, daysAgo: 12);
        withdrawn.Withdraw(DisputeTestData.Now);

        var page = await Queries.ListAsync(new AgentDisputeListQuery(), CancellationToken.None);

        page.Items.Select(d => d.Reference).ShouldBe(
            ["DSP-20261119-CCCCCC", "DSP-20261121-BBBBBB", "DSP-20261101-AAAAAA", "DSP-20261110-DDDDDD"]);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_status()
    {
        Add("DSP-20261120-AAAAAA", DisputeTestData.Customer, daysAgo: 2);
        var rejected = Add("DSP-20261121-BBBBBB", OtherCustomer, daysAgo: 1);
        rejected.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);

        var submitted = await Queries.ListAsync(new AgentDisputeListQuery { Status = DisputeStatus.Submitted }, CancellationToken.None);
        var declined = await Queries.ListAsync(new AgentDisputeListQuery { Status = DisputeStatus.Declined }, CancellationToken.None);

        submitted.Items.Select(d => d.Reference).ShouldBe(["DSP-20261120-AAAAAA"]);
        declined.Items.Select(d => d.Reference).ShouldBe(["DSP-20261121-BBBBBB"]);
    }

    [Fact]
    public async Task The_list_can_be_filtered_by_transaction_type()
    {
        Add("DSP-20261120-AAAAAA", DisputeTestData.Customer, daysAgo: 2);
        Add("DSP-20261121-BBBBBB", OtherCustomer, daysAgo: 1, DisputeTestData.Card(), "duplicate");

        var cards = await Queries.ListAsync(new AgentDisputeListQuery { TransactionType = DisputedTransactionType.Card }, CancellationToken.None);
        var debitOrders = await Queries.ListAsync(new AgentDisputeListQuery { TransactionType = DisputedTransactionType.DebitOrder }, CancellationToken.None);

        cards.Items.Select(d => d.Reference).ShouldBe(["DSP-20261121-BBBBBB"]);
        cards.Items[0].CounterpartyName.ShouldBe("Takealot");
        debitOrders.Items.Select(d => d.Reference).ShouldBe(["DSP-20261120-AAAAAA"]);
    }

    [Theory]
    [InlineData("DSP-20261121-BBBBBB")]
    [InlineData("bbbb")]
    [InlineData("  20261121  ")]
    public async Task A_dispute_is_found_by_part_of_its_reference_in_any_letter_case(string search)
    {
        Add("DSP-20261120-AAAAAA", DisputeTestData.Customer, daysAgo: 2);
        Add("DSP-20261121-BBBBBB", OtherCustomer, daysAgo: 1);

        var page = await Queries.ListAsync(new AgentDisputeListQuery { Reference = search }, CancellationToken.None);

        page.Items.Select(d => d.Reference).ShouldBe(["DSP-20261121-BBBBBB"]);
    }

    [Fact]
    public async Task A_blank_reference_search_is_no_filter()
    {
        Add("DSP-20261120-AAAAAA", DisputeTestData.Customer, daysAgo: 2);

        var page = await Queries.ListAsync(new AgentDisputeListQuery { Reference = "   " }, CancellationToken.None);

        page.TotalItems.ShouldBe(1);
    }

    [Theory]
    [InlineData("submittedAt", "DSP-20261101-AAAAAA,DSP-20261119-CCCCCC,DSP-20261121-BBBBBB")]
    [InlineData("-submittedAt", "DSP-20261121-BBBBBB,DSP-20261119-CCCCCC,DSP-20261101-AAAAAA")]
    [InlineData("amount", "DSP-20261101-AAAAAA,DSP-20261119-CCCCCC,DSP-20261121-BBBBBB")]
    [InlineData("-amount", "DSP-20261121-BBBBBB,DSP-20261119-CCCCCC,DSP-20261101-AAAAAA")]
    public async Task The_list_can_be_sorted_by_submission_time_or_amount(string sort, string expectedOrder)
    {
        // R100 twenty days ago, R599 three days ago, and a card purchase of R1,899 yesterday.
        Add("DSP-20261101-AAAAAA", DisputeTestData.Customer, daysAgo: 20, DisputeTestData.DebitOrder() with { Amount = Money.Zar(100m) });
        Add("DSP-20261119-CCCCCC", DisputeTestData.Customer, daysAgo: 3);
        Add("DSP-20261121-BBBBBB", OtherCustomer, daysAgo: 1, DisputeTestData.Card(), "duplicate");

        var page = await Queries.ListAsync(new AgentDisputeListQuery { Sort = sort }, CancellationToken.None);

        page.Items.Select(d => d.Reference).ShouldBe(expectedOrder.Split(','));
    }

    [Fact]
    public async Task The_list_is_paged()
    {
        for (var day = 1; day <= 5; day++)
        {
            Add($"DSP-2026111{day}-AAAAAA", DisputeTestData.Customer, daysAgo: 10 - day);
        }

        var page = await Queries.ListAsync(new AgentDisputeListQuery { Page = 2, PageSize = 2 }, CancellationToken.None);

        page.TotalItems.ShouldBe(5);
        page.TotalPages.ShouldBe(3);
        page.Items.Select(d => d.Reference).ShouldBe(["DSP-20261113-AAAAAA", "DSP-20261114-AAAAAA"]);
    }

    [Fact]
    public async Task Detail_shows_the_disputed_transaction_and_who_did_what()
    {
        var dispute = Add("DSP-20261120-AAAAAA", OtherCustomer, daysAgo: 2);
        dispute.MarkSentToScheme("{}", DisputeActor.Agent(DisputeTestData.Agent), DisputeTestData.Now);
        dispute.MarkRefunded(DisputeTestData.Now.AddSeconds(20));

        var detail = (await Queries.GetAsync(dispute.Id, CancellationToken.None)).Value;

        detail.CustomerId.ShouldBe(OtherCustomer);
        detail.Transaction.Id.ShouldBe(dispute.TransactionId);
        detail.Transaction.Type.ShouldBe(DisputedTransactionType.DebitOrder);
        detail.Transaction.SchemeReference.ShouldBe("ACME20261101000001");
        detail.Transaction.CounterpartyName.ShouldBe("Vodacom");
        detail.Transaction.Amount.ShouldBe(599m);
        detail.Transaction.AccountMasked.ShouldBe("******7890");
        detail.Transaction.MandateType.ShouldBe("Registered");
        detail.Transaction.MandateReference.ShouldBe("MRN000000001");
        detail.Transaction.CardScheme.ShouldBeNull();
        detail.ReasonLabel.ShouldBe("I did not authorise this debit order");
        detail.SentToSchemeAt.ShouldBe(DisputeTestData.Now);
        detail.Events.Select(e => (e.Status, e.Actor, e.ActorSubjectId)).ShouldBe(
        [
            (DisputeStatus.Submitted, DisputeActorType.Customer, OtherCustomer),
            (DisputeStatus.SentToScheme, DisputeActorType.Agent, DisputeTestData.Agent),
            (DisputeStatus.Refunded, DisputeActorType.System, (Guid?)null),
        ]);
    }

    [Fact]
    public async Task Detail_of_a_card_dispute_shows_the_card_details()
    {
        var dispute = Add("DSP-20261120-AAAAAA", DisputeTestData.Customer, daysAgo: 2, DisputeTestData.Card(), "not_received");

        var detail = (await Queries.GetAsync(dispute.Id, CancellationToken.None)).Value;

        detail.Transaction.Type.ShouldBe(DisputedTransactionType.Card);
        detail.Transaction.CardScheme.ShouldBe("Visa");
        detail.Transaction.PostingDate.ShouldBe(new DateOnly(2026, 11, 19));
        detail.Transaction.MandateType.ShouldBeNull();
        detail.ReasonCode.ShouldBe("13.1");
    }

    [Fact]
    public async Task Detail_of_a_dispute_that_does_not_exist_is_not_found()
    {
        var result = await Queries.GetAsync(Guid.NewGuid(), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("dispute_not_found");
    }

    [Fact]
    public async Task The_customers_timeline_names_the_kind_of_actor_but_never_the_agent()
    {
        var dispute = Add("DSP-20261120-AAAAAA", DisputeTestData.Customer, daysAgo: 2);
        dispute.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);
        var customerQueries = new DisputeQueries(_disputes, new FakeTransactionDirectory());

        var detail = (await customerQueries.GetAsync(DisputeTestData.Customer, dispute.Id, CancellationToken.None)).Value;

        detail.Events.Select(e => e.Actor).ShouldBe([DisputeActorType.Customer, DisputeActorType.Agent]);
        System.Text.Json.JsonSerializer.Serialize(detail).ShouldNotContain(DisputeTestData.Agent.ToString());
    }

    private Dispute Add(string reference, Guid customer, int daysAgo, DisputedTransaction? transaction = null, string reasonId = "not_authorised")
    {
        var dispute = Dispute.Submit(
            Guid.NewGuid(), reference, customer, transaction ?? DisputeTestData.DebitOrder(), reasonId, comment: null, DisputeTestData.Now.AddDays(-daysAgo));
        _disputes.Disputes.Add(dispute);
        return dispute;
    }
}

public class AgentRequestValidatorTests
{
    private readonly AgentDisputeListQueryValidator _listValidator = new();
    private readonly RejectDisputeRequestValidator _rejectValidator = new();

    [Theory]
    [InlineData(null)]
    [InlineData("submittedAt")]
    [InlineData("-submittedAt")]
    [InlineData("amount")]
    [InlineData("-amount")]
    public void The_list_accepts_the_known_sorts_and_no_sort(string? sort)
    {
        _listValidator.TestValidate(new AgentDisputeListQuery { Sort = sort }).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void The_list_rejects_an_unknown_sort_bad_paging_and_an_overlong_reference()
    {
        var result = _listValidator.TestValidate(new AgentDisputeListQuery { Sort = "reference", Page = 0, PageSize = 101, Reference = new string('A', 31) });

        result.ShouldHaveValidationErrorFor(q => q.Sort);
        result.ShouldHaveValidationErrorFor(q => q.Page);
        result.ShouldHaveValidationErrorFor(q => q.PageSize);
        result.ShouldHaveValidationErrorFor(q => q.Reference);
    }

    [Fact]
    public void No_sort_means_the_review_queue()
    {
        AgentDisputeSorts.Parse(null).ShouldBe(AgentDisputeSort.ReviewQueue);
        AgentDisputeSorts.Parse("-amount").ShouldBe(AgentDisputeSort.AmountDescending);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("The mandate shows this amount was agreed.")]
    public void A_rejection_with_a_reason_is_accepted(string reason)
    {
        _rejectValidator.TestValidate(new RejectDisputeRequest(reason)).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void A_rejection_reason_of_exactly_500_characters_is_accepted_after_trimming()
    {
        _rejectValidator.TestValidate(new RejectDisputeRequest($"  {new string('x', 500)}  ")).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_without_a_reason_is_refused(string? reason)
    {
        _rejectValidator.TestValidate(new RejectDisputeRequest(reason!)).ShouldHaveValidationErrorFor(r => r.Reason);
    }

    [Fact]
    public void A_rejection_reason_over_500_characters_is_refused()
    {
        _rejectValidator.TestValidate(new RejectDisputeRequest(new string('x', 501))).ShouldHaveValidationErrorFor(r => r.Reason);
    }
}

public class AutoApproveOptionTests
{
    [Fact]
    public void Automatic_approval_is_off_by_default_so_disputes_wait_for_an_agent()
    {
        var options = new DisputeProcessingOptions();

        options.AutoApproveAfterSeconds.ShouldBe(0);
        options.AutoApproveCutoff(DisputeTestData.Now).ShouldBeNull();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void Zero_or_less_switches_automatic_approval_off(int seconds)
    {
        new DisputeProcessingOptions { AutoApproveAfterSeconds = seconds }.AutoApproveCutoff(DisputeTestData.Now).ShouldBeNull();
    }

    [Fact]
    public void When_switched_on_disputes_that_have_waited_long_enough_are_approved()
    {
        var options = new DisputeProcessingOptions { AutoApproveAfterSeconds = 30 };

        options.AutoApproveCutoff(DisputeTestData.Now).ShouldBe(DisputeTestData.Now.AddSeconds(-30));
    }
}
