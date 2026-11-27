using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.SharedKernel;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Disputes;

public class SubmitDisputeHandlerTests
{
    private static readonly Guid Customer = DisputeTestData.Customer;

    private readonly FakeTransactionDirectory _transactions = new();
    private readonly InMemoryDisputeRepository _disputes = new();
    private readonly FakeTimeProvider _clock = new(DisputeTestData.Now);

    private SubmitDisputeHandler Handler => new(_transactions, _disputes, _clock);

    [Fact]
    public async Task Raises_a_dispute_for_an_eligible_debit_order()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder());

        var result = await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Replayed.ShouldBeFalse();
        result.Value.Dispute.Status.ShouldBe(DisputeStatus.Submitted);
        result.Value.Dispute.TransactionType.ShouldBe(DisputedTransactionType.DebitOrder);
        result.Value.Dispute.Amount.ShouldBe(debitOrder.Amount);
        result.Value.Dispute.SchemeReference.ShouldBe(debitOrder.SchemeReference);
        result.Value.Dispute.ReasonCode.ShouldBe("MD01");
        _disputes.Disputes.Count.ShouldBe(1);
        _disputes.IdempotencyRecords.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Raises_a_dispute_for_an_eligible_card_purchase_with_the_code_of_its_scheme()
    {
        var card = _transactions.Add(ContractData.Card());

        var result = await Handler.HandleAsync(Command(card.Id, reasonId: "not_received"), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Dispute.TransactionType.ShouldBe(DisputedTransactionType.Card);
        result.Value.Dispute.TransactionId.ShouldBe(card.Id);
        result.Value.Dispute.SchemeReference.ShouldBe(card.SchemeReference);
        result.Value.Dispute.CounterpartyName.ShouldBe("Takealot");
        result.Value.Dispute.ReasonId.ShouldBe("not_received");
        result.Value.Dispute.ReasonCode.ShouldBe("13.1");
        result.Value.Dispute.CardScheme.ShouldBe("Visa");
    }

    [Fact]
    public async Task A_transaction_that_does_not_exist_or_belongs_to_someone_else_is_not_found()
    {
        var theirs = _transactions.Add(ContractData.DebitOrder() with { CustomerId = Guid.NewGuid() });

        var result = await Handler.HandleAsync(Command(theirs.Id), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("transaction_not_found");
        _disputes.Disputes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("within_mandate_terms")]
    [InlineData("collection_rejected")]
    [InlineData("dispute_window_closed")]
    public async Task A_debit_order_that_is_not_disputable_is_refused_with_its_reason(string code)
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder() with { IneligibilityCode = code, IneligibilityMessage = "Not disputable." });

        var result = await Handler.HandleAsync(Command(debitOrder.Id, reasonId: "incorrect_amount"), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Unprocessable);
        result.Error.Code.ShouldBe(code);
        _disputes.Disputes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("not_a_purchase")]
    [InlineData("not_settled")]
    [InlineData("fully_refunded")]
    [InlineData("dispute_window_closed")]
    public async Task A_card_transaction_that_is_not_disputable_is_refused_with_its_reason(string code)
    {
        var card = _transactions.Add(ContractData.Card() with { IneligibilityCode = code, IneligibilityMessage = "Not disputable." });

        var result = await Handler.HandleAsync(Command(card.Id, reasonId: "duplicate"), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Unprocessable);
        result.Error.Code.ShouldBe(code);
        _disputes.Disputes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Not_authorised_is_refused_for_a_debicheck_debit_order()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder(mandateType: "DebiCheck"));

        var result = await Handler.HandleAsync(Command(debitOrder.Id, reasonId: "not_authorised"), CancellationToken.None);

        result.Error!.Code.ShouldBe("reason_not_allowed");
        result.Error.Type.ShouldBe(ErrorType.Unprocessable);
    }

    [Fact]
    public async Task Not_authorised_is_refused_for_a_card_purchase_the_cardholder_verified()
    {
        var card = _transactions.Add(ContractData.Card(verification: "ThreeDSecure"));

        var result = await Handler.HandleAsync(Command(card.Id, reasonId: "not_authorised"), CancellationToken.None);

        result.Error!.Code.ShouldBe("reason_not_allowed");
        _disputes.Disputes.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_second_dispute_for_the_same_transaction_is_a_conflict()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder());
        await Handler.HandleAsync(Command(debitOrder.Id, idempotencyKey: "key-1"), CancellationToken.None);

        var second = await Handler.HandleAsync(Command(debitOrder.Id, idempotencyKey: "key-2"), CancellationToken.None);

        second.Error!.Code.ShouldBe("already_disputed");
        second.Error.Type.ShouldBe(ErrorType.Conflict);
        _disputes.Disputes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Retrying_with_the_same_key_and_body_returns_the_original_dispute()
    {
        var card = _transactions.Add(ContractData.Card());
        var first = await Handler.HandleAsync(Command(card.Id), CancellationToken.None);

        var retry = await Handler.HandleAsync(Command(card.Id), CancellationToken.None);

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Replayed.ShouldBeTrue();
        retry.Value.Dispute.Id.ShouldBe(first.Value.Dispute.Id);
        _disputes.Disputes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_a_conflict()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder());
        var other = _transactions.Add(ContractData.DebitOrder());
        await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        var result = await Handler.HandleAsync(Command(other.Id), CancellationToken.None);

        result.Error!.Code.ShouldBe("idempotency_key_reused");
        _disputes.Disputes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reusing_a_key_with_a_different_reason_is_a_conflict()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder());
        await Handler.HandleAsync(Command(debitOrder.Id, reasonId: "duplicate"), CancellationToken.None);

        var result = await Handler.HandleAsync(Command(debitOrder.Id, reasonId: "cancelled"), CancellationToken.None);

        result.Error!.Code.ShouldBe("idempotency_key_reused");
    }

    [Fact]
    public async Task Losing_a_race_to_another_request_for_the_same_transaction_is_a_conflict_not_an_error()
    {
        var debitOrder = _transactions.Add(ContractData.DebitOrder());
        _disputes.FailNextAddAsDuplicate = true;

        var result = await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        result.Error!.Code.ShouldBe("already_disputed");
    }

    private static SubmitDisputeCommand Command(Guid transactionId, string reasonId = "not_authorised", string idempotencyKey = "key-1") =>
        new(Customer, transactionId, reasonId, idempotencyKey);
}

public class SubmitDisputeRequestValidatorTests
{
    private readonly SubmitDisputeRequestValidator _validator = new();

    [Theory]
    [InlineData("incorrect_amount")]
    [InlineData("credit_not_processed")]
    public void Accepts_a_valid_request(string reasonId)
    {
        _validator.TestValidate(new SubmitDisputeRequest(Guid.NewGuid(), reasonId)).ShouldNotHaveAnyValidationErrors();
    }

    [Theory]
    [InlineData("NOPE")]
    [InlineData("AM09")]  // the scheme code is not the id
    [InlineData("")]
    public void Rejects_a_missing_transaction_and_an_unknown_reason(string reasonId)
    {
        var result = _validator.TestValidate(new SubmitDisputeRequest(Guid.Empty, reasonId));

        result.ShouldHaveValidationErrorFor(r => r.TransactionId);
        result.ShouldHaveValidationErrorFor(r => r.ReasonId);
    }
}

/// <summary>Transactions as the Transactions module hands them to this module.</summary>
internal static class ContractData
{
    public static TransactionForDispute DebitOrder(string mandateType = "Registered") => new(
        Guid.NewGuid(), DisputeTestData.Customer, TransactionTypeNames.DebitOrder, $"ACME{Guid.NewGuid():N}"[..20], 599m, "ZAR",
        new DateOnly(2026, 11, 1), "Vodacom", "******7890",
        new DebitOrderForDispute("VOD-1-20261101", mandateType, "MRN000000001"),
        Card: null,
        IneligibilityCode: null,
        IneligibilityMessage: null);

    public static TransactionForDispute Card(string scheme = "Visa", string entryMode = "ECommerce", string verification = "None") => new(
        Guid.NewGuid(), DisputeTestData.Customer, TransactionTypeNames.Card, $"7{Guid.NewGuid():N}"[..23], 1899m, "ZAR",
        new DateOnly(2026, 11, 18), "Takealot", "******7890",
        DebitOrder: null,
        new CardForDispute(scheme, entryMode, verification, new DateOnly(2026, 11, 19)),
        IneligibilityCode: null,
        IneligibilityMessage: null);
}

internal sealed class FakeTransactionDirectory : ITransactionDirectory
{
    private readonly List<TransactionForDispute> _transactions = [];

    public TransactionForDispute Add(TransactionForDispute transaction)
    {
        _transactions.Add(transaction);
        return transaction;
    }

    public Task<TransactionForDispute?> FindAsync(Guid transactionId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_transactions.FirstOrDefault(t => t.Id == transactionId && t.CustomerId == customerId));
}

internal sealed class InMemoryDisputeRepository : IDisputeRepository
{
    public List<Dispute> Disputes { get; } = [];

    public List<IdempotencyRecord> IdempotencyRecords { get; } = [];

    public bool FailNextAddAsDuplicate { get; set; }

    public Task<IdempotencyRecord?> FindIdempotencyRecordAsync(Guid customerId, string key, CancellationToken cancellationToken) =>
        Task.FromResult(IdempotencyRecords.FirstOrDefault(r => r.CustomerId == customerId && r.Key == key));

    public Task<Dispute?> FindAsync(Guid disputeId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(Disputes.FirstOrDefault(d => d.Id == disputeId && d.CustomerId == customerId));

    public Task<bool> ExistsForTransactionAsync(Guid transactionId, CancellationToken cancellationToken) =>
        Task.FromResult(Disputes.Any(d => d.TransactionId == transactionId && d.Status != DisputeStatus.Withdrawn));

    public Task<bool> TryAddAsync(Dispute dispute, IdempotencyRecord idempotencyRecord, CancellationToken cancellationToken)
    {
        if (FailNextAddAsDuplicate || Disputes.Any(d => d.TransactionId == dispute.TransactionId && d.Status != DisputeStatus.Withdrawn))
        {
            FailNextAddAsDuplicate = false;
            return Task.FromResult(false);
        }

        Disputes.Add(dispute);
        IdempotencyRecords.Add(idempotencyRecord);
        return Task.FromResult(true);
    }

    public Task<PagedResult<Dispute>> ListAsync(Guid customerId, DisputeState? state, PageRequest page, CancellationToken cancellationToken)
    {
        var items = Disputes
            .Where(d => d.CustomerId == customerId)
            .Where(d => state is null || DisputeMapper.StateOf(d) == state)
            .OrderByDescending(d => d.SubmittedAt)
            .ToList();
        return Task.FromResult(new PagedResult<Dispute>(items.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, items.Count));
    }
}
