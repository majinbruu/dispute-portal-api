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
    private const string RejectedTransaction = "ACME-TX-RETURN-REJECT-0001";

    private readonly FakeTimeProvider _clock = new(DisputeTestData.Now);

    private DisputeProcessingService Service => new(
        new SimulatedSchemeGateway(Options.Create(new SchemeSimulatorOptions { RejectReturnsForTransactionIds = [RejectedTransaction] })),
        _clock);

    [Fact]
    public void Sending_to_the_scheme_stores_the_return_payload()
    {
        var dispute = DisputeTestData.Submitted();

        Service.SendToScheme(dispute);

        dispute.Status.ShouldBe(DisputeStatus.SentToScheme);
        dispute.SentToSchemeAt.ShouldBe(DisputeTestData.Now);
        dispute.ReturnPayload.ShouldNotBeNull();
        dispute.ReturnPayload.ShouldContain(dispute.OriginalTransactionId);
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
        var dispute = DisputeTestData.Submitted(transactionId: RejectedTransaction);
        Service.SendToScheme(dispute);

        Service.ApplySchemeOutcome(dispute);

        dispute.Status.ShouldBe(DisputeStatus.Declined);
        dispute.OutcomeNote.ShouldNotBeNullOrWhiteSpace();
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
            Guid.NewGuid(), DisputeTestData.Customer, "DSP-1", "Refunded", 599m, "ZAR", "Vodacom", "Refunded.", DisputeTestData.Now)),
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

    [Fact]
    public async Task History_separates_pending_from_completed_disputes()
    {
        var pending = DisputeTestData.Submitted();
        var completed = DisputeTestData.Submitted();
        completed.MarkSentToScheme("{}", DisputeTestData.Now);
        completed.MarkRefunded(DisputeTestData.Now);
        _repository.Disputes.AddRange([pending, completed]);
        var queries = new DisputeQueries(_repository);

        var pendingPage = await queries.ListAsync(DisputeTestData.Customer, new DisputeListQuery { State = DisputeState.Pending }, CancellationToken.None);
        var completedPage = await queries.ListAsync(DisputeTestData.Customer, new DisputeListQuery { State = DisputeState.Completed }, CancellationToken.None);

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
        var queries = new DisputeQueries(_repository);

        var mine = await queries.GetAsync(DisputeTestData.Customer, dispute.Id, CancellationToken.None);
        var theirs = await queries.GetAsync(Guid.NewGuid(), dispute.Id, CancellationToken.None);

        mine.Value.Events.Select(e => e.Status).ShouldBe([DisputeStatus.Submitted, DisputeStatus.SentToScheme]);
        mine.Value.ReasonDescription.ShouldBe("I did not authorise this debit order");
        theirs.Error!.Code.ShouldBe("dispute_not_found");
    }
}
