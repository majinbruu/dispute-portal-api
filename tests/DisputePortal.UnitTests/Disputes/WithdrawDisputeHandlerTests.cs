using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Disputes;

public class WithdrawDisputeHandlerTests
{
    private static readonly Guid Customer = DisputeTestData.Customer;

    private readonly InMemoryDisputeRepository _disputes = new();
    private readonly FakeTransactionDirectory _transactions = new();
    private readonly FakeTimeProvider _clock = new(DisputeTestData.Now.AddMinutes(5));

    private WithdrawDisputeHandler Handler => new(_disputes, _clock);

    [Fact]
    public async Task The_customer_withdraws_their_own_submitted_dispute()
    {
        var dispute = Add(DisputeTestData.Submitted());

        var result = await Handler.HandleAsync(Customer, dispute.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(DisputeStatus.Withdrawn);
        result.Value.CompletedAt.ShouldBe(DisputeTestData.Now.AddMinutes(5));
        _disputes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task The_withdrawal_is_published_in_the_same_save_as_the_status_change()
    {
        var dispute = Add(DisputeTestData.Submitted());
        dispute.ClearDomainEvents();

        await Handler.HandleAsync(Customer, dispute.Id, CancellationToken.None);

        // The DbContext turns pending domain events into outbox rows when it saves, and the handler saves once.
        dispute.DomainEvents.ShouldBe([new DisputeStatusChangedDomainEvent(DisputeStatus.Withdrawn, DisputeTestData.Now.AddMinutes(5))]);
        _disputes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task Another_customers_dispute_is_not_found_and_is_left_alone()
    {
        var dispute = Add(DisputeTestData.Submitted());

        var result = await Handler.HandleAsync(Guid.NewGuid(), dispute.Id, CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("dispute_not_found");
        dispute.Status.ShouldBe(DisputeStatus.Submitted);
        _disputes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task A_dispute_that_does_not_exist_is_not_found()
    {
        var result = await Handler.HandleAsync(Customer, Guid.NewGuid(), CancellationToken.None);

        result.Error!.Code.ShouldBe("dispute_not_found");
    }

    [Theory]
    [InlineData("SentToScheme")]
    [InlineData("Refunded")]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public async Task A_dispute_that_is_no_longer_waiting_for_review_cannot_be_withdrawn(string statusName)
    {
        var status = Enum.Parse<DisputeStatus>(statusName);
        var dispute = Add(InStatus(status));

        var result = await Handler.HandleAsync(Customer, dispute.Id, CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("dispute_not_withdrawable");
        dispute.Status.ShouldBe(status);
        _disputes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task Losing_the_race_to_an_agents_decision_is_a_conflict()
    {
        var dispute = Add(DisputeTestData.Submitted());
        _disputes.FailNextSaveAsConflict = true;

        var result = await Handler.HandleAsync(Customer, dispute.Id, CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Conflict);
        result.Error.Code.ShouldBe("dispute_not_withdrawable");
        _disputes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task After_a_withdrawal_the_same_transaction_can_be_disputed_again()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder());
        var submit = new SubmitDisputeHandler(_transactions, _disputes, _clock);
        var first = await submit.HandleAsync(new SubmitDisputeCommand(Customer, debitOrder.Id, "duplicate", Comment: null, "key-1"), CancellationToken.None);
        await Handler.HandleAsync(Customer, first.Value.Dispute.Id, CancellationToken.None);

        var second = await submit.HandleAsync(new SubmitDisputeCommand(Customer, debitOrder.Id, "duplicate", Comment: null, "key-2"), CancellationToken.None);

        second.IsSuccess.ShouldBeTrue();
        second.Value.Replayed.ShouldBeFalse();
        second.Value.Dispute.Id.ShouldNotBe(first.Value.Dispute.Id);
        second.Value.Dispute.Status.ShouldBe(DisputeStatus.Submitted);
        _disputes.Disputes.Select(d => d.Status).ShouldBe([DisputeStatus.Withdrawn, DisputeStatus.Submitted]);
    }

    [Fact]
    public async Task A_second_dispute_is_still_a_conflict_while_the_first_one_stands()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder());
        var submit = new SubmitDisputeHandler(_transactions, _disputes, _clock);
        await submit.HandleAsync(new SubmitDisputeCommand(Customer, debitOrder.Id, "duplicate", Comment: null, "key-1"), CancellationToken.None);
        await submit.HandleAsync(new SubmitDisputeCommand(Customer, debitOrder.Id, "duplicate", Comment: null, "key-2"), CancellationToken.None);

        _disputes.Disputes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_withdrawn_dispute_is_listed_as_completed()
    {
        var dispute = Add(DisputeTestData.Submitted());
        await Handler.HandleAsync(Customer, dispute.Id, CancellationToken.None);
        var queries = new DisputeQueries(_disputes, _transactions);

        var pending = await queries.ListAsync(Customer, new DisputeListQuery { State = DisputeState.Pending }, CancellationToken.None);
        var completed = await queries.ListAsync(Customer, new DisputeListQuery { State = DisputeState.Completed }, CancellationToken.None);

        pending.Items.ShouldBeEmpty();
        completed.Items.Single().Status.ShouldBe(DisputeStatus.Withdrawn);
    }

    private Dispute Add(Dispute dispute)
    {
        _disputes.Disputes.Add(dispute);
        return dispute;
    }

    internal static Dispute InStatus(DisputeStatus status)
    {
        var dispute = DisputeTestData.Submitted();
        switch (status)
        {
            case DisputeStatus.SentToScheme:
                dispute.MarkSentToScheme("{}", DisputeActor.Agent(DisputeTestData.Agent), DisputeTestData.Now);
                break;
            case DisputeStatus.Refunded:
                dispute.MarkSentToScheme("{}", DisputeActor.Agent(DisputeTestData.Agent), DisputeTestData.Now);
                dispute.MarkRefunded(DisputeTestData.Now);
                break;
            case DisputeStatus.Declined:
                dispute.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);
                break;
            case DisputeStatus.Withdrawn:
                dispute.Withdraw(DisputeTestData.Now);
                break;
        }

        return dispute;
    }
}
