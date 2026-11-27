using System.Text.Json;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Disputes.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Disputes;

public class DisputeProcessingServiceTests
{
    private const string DeclinedDebitOrder = "ACME-TX-RETURN-REJECT-0001";
    private const string DeclinedCard = "74027000000000000000017";

    private readonly FakeTimeProvider _clock = new(DisputeTestData.Now);

    private DisputeProcessingService Service => new(
        new SimulatedSchemeGateway(Options.Create(new SchemeSimulatorOptions { DeclineSchemeReferences = [DeclinedDebitOrder, DeclinedCard] })),
        _clock);

    [Fact]
    public void Sending_a_debit_order_dispute_stores_a_pacs004_payment_return()
    {
        var dispute = DisputeTestData.Submitted();

        Service.SendToScheme(dispute);

        dispute.Status.ShouldBe(DisputeStatus.SentToScheme);
        dispute.SentToSchemeAt.ShouldBe(DisputeTestData.Now);
        dispute.SchemePayload.ShouldNotBeNull();
        dispute.SchemePayload.ShouldContain("pacs.004");
        dispute.SchemePayload.ShouldContain(dispute.SchemeReference);
    }

    [Fact]
    public void Sending_a_card_dispute_stores_a_chargeback_record_instead_of_a_pacs004()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.Card(), "duplicate");

        Service.SendToScheme(dispute);

        dispute.Status.ShouldBe(DisputeStatus.SentToScheme);
        dispute.SchemePayload.ShouldNotBeNull();
        dispute.SchemePayload.ShouldContain("Chargeback");
        dispute.SchemePayload.ShouldContain(dispute.SchemeReference);
        dispute.SchemePayload.ShouldNotContain("pacs.004");
    }

    [Fact]
    public void A_card_dispute_the_scheme_accepts_ends_in_a_refund()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.Card(), "duplicate");
        Service.SendToScheme(dispute);

        Service.ApplySchemeOutcome(dispute);

        dispute.Status.ShouldBe(DisputeStatus.Refunded);
    }

    [Fact]
    public void A_card_dispute_on_the_decline_list_ends_declined_because_the_merchant_proved_the_charge()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.Card(acquirerReferenceNumber: DeclinedCard), "duplicate");
        Service.SendToScheme(dispute);

        Service.ApplySchemeOutcome(dispute);

        dispute.Status.ShouldBe(DisputeStatus.Declined);
        dispute.OutcomeNote.ShouldBe(SimulatedSchemeGateway.CardDeclineNote);
        dispute.OutcomeNote!.ShouldContain("merchant provided proof");
    }

    [Fact]
    public void A_return_the_scheme_accepts_ends_in_a_refund()
    {
        var dispute = DisputeTestData.Submitted();
        Service.SendToScheme(dispute);

        Service.ApplySchemeOutcome(dispute);

        dispute.Status.ShouldBe(DisputeStatus.Refunded);
    }

    [Fact]
    public void A_return_the_scheme_rejects_ends_declined_with_an_explanation()
    {
        var dispute = DisputeTestData.Submitted(transactionId: DeclinedDebitOrder);
        Service.SendToScheme(dispute);

        Service.ApplySchemeOutcome(dispute);

        dispute.Status.ShouldBe(DisputeStatus.Declined);
        dispute.OutcomeNote.ShouldBe(SimulatedSchemeGateway.DebitOrderDeclineNote);
    }
}

public class OutboxProcessorTests
{
    private readonly FakeTimeProvider _clock = new(DisputeTestData.Now);
    private readonly RecordingHandler _handler = new();

    private OutboxProcessor Processor => new([_handler], _clock, NullLogger<OutboxProcessor>.Instance);

    [Fact]
    public async Task Delivers_the_event_to_every_handler_and_marks_the_message_processed()
    {
        var message = Message();

        await Processor.ProcessAsync(message, CancellationToken.None);

        _handler.Received.Count.ShouldBe(1);
        _handler.Received[0].MessageId.ShouldBe(message.Id);
        _handler.Received[0].Message.Reference.ShouldBe("DSP-1");
        message.ProcessedAt.ShouldBe(DisputeTestData.Now);
    }

    [Fact]
    public async Task A_failing_handler_leaves_the_message_unprocessed_and_scheduled_for_retry()
    {
        _handler.Failure = new InvalidOperationException("inbox unavailable");
        var message = Message();

        await Processor.ProcessAsync(message, CancellationToken.None);

        message.ProcessedAt.ShouldBeNull();
        message.Attempts.ShouldBe(1);
        message.LastError.ShouldBe("inbox unavailable");
        message.NextAttemptAt.ShouldBeGreaterThan(DisputeTestData.Now);
    }

    [Fact]
    public async Task A_message_that_keeps_failing_is_abandoned_after_the_last_attempt()
    {
        _handler.Failure = new InvalidOperationException("inbox unavailable");
        var message = Message();

        for (var attempt = 1; attempt < OutboxMessage.MaxAttempts; attempt++)
        {
            await Processor.ProcessAsync(message, CancellationToken.None);
            message.IsAbandoned.ShouldBeFalse();
        }

        await Processor.ProcessAsync(message, CancellationToken.None);

        message.IsAbandoned.ShouldBeTrue();
        message.ProcessedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(24, 24)]
    [InlineData(0, 1)]
    [InlineData(-5, 1)]
    public void Idempotency_keys_expire_after_the_retention_period_which_is_at_least_one_hour(int configuredHours, int effectiveHours)
    {
        IdempotencyRecord.ExpiryCutoff(DisputeTestData.Now, configuredHours).ShouldBe(DisputeTestData.Now.AddHours(-effectiveHours));
    }

    [Fact]
    public async Task A_payload_that_cannot_be_read_is_recorded_as_a_failure_not_thrown()
    {
        var message = new OutboxMessage(Guid.NewGuid(), nameof(DisputeStatusChanged), "not json", DisputeTestData.Now);

        await Processor.ProcessAsync(message, CancellationToken.None);

        message.ProcessedAt.ShouldBeNull();
        message.Attempts.ShouldBe(1);
        _handler.Received.ShouldBeEmpty();
    }

    [Fact]
    public async Task Cancellation_is_not_swallowed_as_a_delivery_failure()
    {
        _handler.Failure = new OperationCanceledException();
        var message = Message();

        await Should.ThrowAsync<OperationCanceledException>(() => Processor.ProcessAsync(message, CancellationToken.None));

        message.Attempts.ShouldBe(0);
    }

    private static OutboxMessage Message() => new(
        Guid.NewGuid(),
        nameof(DisputeStatusChanged),
        JsonSerializer.Serialize(new DisputeStatusChanged(
            Guid.NewGuid(), DisputeTestData.Customer, "DSP-1", "Refunded", DisputedTransactionTypes.DebitOrder, 599m, "ZAR", "Vodacom", "Refunded.", DisputeTestData.Now)),
        DisputeTestData.Now);

    private sealed class RecordingHandler : IDisputeStatusChangedHandler
    {
        public List<(Guid MessageId, DisputeStatusChanged Message)> Received { get; } = [];

        public Exception? Failure { get; set; }

        public Task HandleAsync(Guid messageId, DisputeStatusChanged message, CancellationToken cancellationToken)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Received.Add((messageId, message));
            return Task.CompletedTask;
        }
    }
}

public class DisputeQueriesTests
{
    private readonly InMemoryDisputeRepository _repository = new();
    private readonly FakeTransactionDirectory _transactions = new();

    private DisputeQueries Queries => new(_repository, _transactions);

    [Fact]
    public async Task History_separates_pending_from_completed_disputes()
    {
        var pending = DisputeTestData.Submitted();
        var completed = DisputeTestData.Submitted();
        completed.MarkSentToScheme("{}", DisputeTestData.Now);
        completed.MarkRefunded(DisputeTestData.Now);
        _repository.Disputes.AddRange([pending, completed]);

        var pendingPage = await Queries.ListAsync(DisputeTestData.Customer, new DisputeListQuery { State = DisputeState.Pending }, CancellationToken.None);
        var completedPage = await Queries.ListAsync(DisputeTestData.Customer, new DisputeListQuery { State = DisputeState.Completed }, CancellationToken.None);

        pendingPage.Items.Select(d => d.Id).ShouldBe([pending.Id]);
        completedPage.Items.Select(d => d.Id).ShouldBe([completed.Id]);
        completedPage.Items[0].State.ShouldBe(DisputeState.Completed);
    }

    [Fact]
    public async Task Detail_returns_the_timeline_in_order_and_hides_other_customers_disputes()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.MarkSentToScheme("{}", DisputeTestData.Now.AddSeconds(10));
        _repository.Disputes.Add(dispute);

        var mine = await Queries.GetAsync(DisputeTestData.Customer, dispute.Id, CancellationToken.None);
        var theirs = await Queries.GetAsync(Guid.NewGuid(), dispute.Id, CancellationToken.None);

        mine.Value.Events.Select(e => e.Status).ShouldBe([DisputeStatus.Submitted, DisputeStatus.SentToScheme]);
        mine.Value.ReasonId.ShouldBe("not_authorised");
        mine.Value.ReasonCode.ShouldBe("MD01");
        mine.Value.ReasonLabel.ShouldBe("I did not authorise this debit order");
        theirs.Error!.Code.ShouldBe("dispute_not_found");
    }

    [Fact]
    public async Task A_card_dispute_is_listed_and_shown_with_its_card_details()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.Card(), "not_authorised");
        _repository.Disputes.Add(dispute);

        var page = await Queries.ListAsync(DisputeTestData.Customer, new DisputeListQuery(), CancellationToken.None);
        var detail = await Queries.GetAsync(DisputeTestData.Customer, dispute.Id, CancellationToken.None);

        page.Items[0].TransactionType.ShouldBe(DisputedTransactionType.Card);
        page.Items[0].CounterpartyName.ShouldBe("Takealot");
        page.Items[0].ReasonLabel.ShouldBe("I did not make this transaction");
        detail.Value.CardScheme.ShouldBe("Visa");
        detail.Value.ReasonCode.ShouldBe("10.4");
        detail.Value.MandateReference.ShouldBeNull();
    }

    [Fact]
    public async Task Reasons_are_those_offered_for_the_transaction_with_their_resolved_codes()
    {
        var debiCheck = _transactions.Add(ContractData.DebitOrder(mandateType: "DebiCheck"));
        var mastercard = _transactions.Add(ContractData.Card(scheme: "Mastercard", entryMode: "Recurring"));

        var forDebiCheck = await Queries.ReasonsForAsync(DisputeTestData.Customer, debiCheck.Id, CancellationToken.None);
        var forCard = await Queries.ReasonsForAsync(DisputeTestData.Customer, mastercard.Id, CancellationToken.None);

        forDebiCheck.Value.Select(r => r.Code).ShouldBe(["AM09", "DT01", "MD06", "AM05"]);
        forCard.Value.Select(r => (r.Id, r.Code)).ShouldBe(
        [
            ("not_authorised", "4837"), ("duplicate", "4834"), ("incorrect_amount", "4834"),
            ("not_received", "4853"), ("cancelled_recurring", "4853"), ("credit_not_processed", "4853"),
        ]);
        forCard.Value[0].Label.ShouldBe("I did not make this transaction");
    }

    [Fact]
    public async Task Reasons_for_a_transaction_that_belongs_to_someone_else_are_not_found()
    {
        var theirs = _transactions.Add(ContractData.Card() with { CustomerId = Guid.NewGuid() });

        var result = await Queries.ReasonsForAsync(DisputeTestData.Customer, theirs.Id, CancellationToken.None);

        result.Error!.Type.ShouldBe(DisputePortal.SharedKernel.ErrorType.NotFound);
        result.Error.Code.ShouldBe("transaction_not_found");
    }
}
