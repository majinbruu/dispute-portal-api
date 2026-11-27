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

    private readonly FakeDebitOrderDirectory _debitOrders = new();
    private readonly InMemoryDisputeRepository _disputes = new();
    private readonly FakeTimeProvider _clock = new(DisputeTestData.Now);

    private SubmitDisputeHandler Handler => new(_debitOrders, _disputes, _clock);

    [Fact]
    public async Task Raises_a_dispute_for_an_eligible_debit_order()
    {
        var debitOrder = _debitOrders.Add(Eligible());

        var result = await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Replayed.ShouldBeFalse();
        result.Value.Dispute.Status.ShouldBe(DisputeStatus.Submitted);
        result.Value.Dispute.Amount.ShouldBe(debitOrder.Amount);
        result.Value.Dispute.OriginalTransactionId.ShouldBe(debitOrder.TransactionId);
        _disputes.Disputes.Count.ShouldBe(1);
        _disputes.IdempotencyRecords.Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_debit_order_that_does_not_exist_or_belongs_to_someone_else_is_not_found()
    {
        var theirs = _debitOrders.Add(Eligible() with { CustomerId = Guid.NewGuid() });

        var result = await Handler.HandleAsync(Command(theirs.Id), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        _disputes.Disputes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("within_mandate_terms")]
    [InlineData("collection_rejected")]
    [InlineData("dispute_window_closed")]
    public async Task A_debit_order_that_is_not_disputable_is_refused_with_its_reason(string code)
    {
        var debitOrder = _debitOrders.Add(Eligible() with { IneligibilityCode = code, IneligibilityMessage = "Not disputable." });

        var result = await Handler.HandleAsync(Command(debitOrder.Id, reasonCode: "AM09"), CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.Unprocessable);
        result.Error.Code.ShouldBe(code);
        _disputes.Disputes.ShouldBeEmpty();
    }

    [Fact]
    public async Task Not_authorised_is_refused_for_a_debicheck_debit_order()
    {
        var debitOrder = _debitOrders.Add(Eligible() with { MandateType = DisputeReason.DebiCheck });

        var result = await Handler.HandleAsync(Command(debitOrder.Id, reasonCode: "MD01"), CancellationToken.None);

        result.Error!.Code.ShouldBe("reason_not_allowed");
        result.Error.Type.ShouldBe(ErrorType.Unprocessable);
    }

    [Fact]
    public async Task A_second_dispute_for_the_same_debit_order_is_a_conflict()
    {
        var debitOrder = _debitOrders.Add(Eligible());
        await Handler.HandleAsync(Command(debitOrder.Id, idempotencyKey: "key-1"), CancellationToken.None);

        var second = await Handler.HandleAsync(Command(debitOrder.Id, idempotencyKey: "key-2"), CancellationToken.None);

        second.Error!.Code.ShouldBe("already_disputed");
        second.Error.Type.ShouldBe(ErrorType.Conflict);
        _disputes.Disputes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Retrying_with_the_same_key_and_body_returns_the_original_dispute()
    {
        var debitOrder = _debitOrders.Add(Eligible());
        var first = await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        var retry = await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        retry.IsSuccess.ShouldBeTrue();
        retry.Value.Replayed.ShouldBeTrue();
        retry.Value.Dispute.Id.ShouldBe(first.Value.Dispute.Id);
        _disputes.Disputes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Reusing_a_key_for_a_different_request_is_a_conflict()
    {
        var debitOrder = _debitOrders.Add(Eligible());
        var other = _debitOrders.Add(Eligible());
        await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        var result = await Handler.HandleAsync(Command(other.Id), CancellationToken.None);

        result.Error!.Code.ShouldBe("idempotency_key_reused");
        _disputes.Disputes.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Losing_a_race_to_another_request_for_the_same_debit_order_is_a_conflict_not_an_error()
    {
        var debitOrder = _debitOrders.Add(Eligible());
        _disputes.FailNextAddAsDuplicate = true;

        var result = await Handler.HandleAsync(Command(debitOrder.Id), CancellationToken.None);

        result.Error!.Code.ShouldBe("already_disputed");
    }

    private static DebitOrderForDispute Eligible() => new(
        Guid.NewGuid(), Customer, $"ACME{Guid.NewGuid():N}"[..20], "VOD-1-20261101", 599m, "ZAR", new DateOnly(2026, 11, 1),
        DisputeReason.Registered, "MRN000000001", "Vodacom", "******7890", IneligibilityCode: null, IneligibilityMessage: null);

    private static SubmitDisputeCommand Command(Guid debitOrderId, string reasonCode = "MD01", string idempotencyKey = "key-1") =>
        new(Customer, debitOrderId, reasonCode, idempotencyKey);
}

public class SubmitDisputeRequestValidatorTests
{
    private readonly SubmitDisputeRequestValidator _validator = new();

    [Fact]
    public void Accepts_a_valid_request()
    {
        _validator.TestValidate(new SubmitDisputeRequest(Guid.NewGuid(), "AM09")).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Rejects_a_missing_debit_order_and_an_unknown_reason()
    {
        var result = _validator.TestValidate(new SubmitDisputeRequest(Guid.Empty, "NOPE"));

        result.ShouldHaveValidationErrorFor(r => r.DebitOrderId);
        result.ShouldHaveValidationErrorFor(r => r.ReasonCode);
    }
}

internal sealed class FakeDebitOrderDirectory : IDebitOrderDirectory
{
    private readonly List<DebitOrderForDispute> _debitOrders = [];

    public DebitOrderForDispute Add(DebitOrderForDispute debitOrder)
    {
        _debitOrders.Add(debitOrder);
        return debitOrder;
    }

    public Task<DebitOrderForDispute?> FindAsync(Guid debitOrderId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_debitOrders.FirstOrDefault(d => d.Id == debitOrderId && d.CustomerId == customerId));
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

    public Task<bool> ExistsForDebitOrderAsync(Guid debitOrderId, CancellationToken cancellationToken) =>
        Task.FromResult(Disputes.Any(d => d.DebitOrderId == debitOrderId));

    public Task<bool> TryAddAsync(Dispute dispute, IdempotencyRecord idempotencyRecord, CancellationToken cancellationToken)
    {
        if (FailNextAddAsDuplicate || Disputes.Any(d => d.DebitOrderId == dispute.DebitOrderId))
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
