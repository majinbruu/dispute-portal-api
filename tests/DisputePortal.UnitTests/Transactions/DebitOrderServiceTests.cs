using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Transactions;

public class DebitOrderServiceTests
{
    private readonly InMemoryTransactionStore _store = new();
    private readonly FakeDisputeStatusProvider _disputes = new();
    private readonly FakeTimeProvider _clock = new(TestData.Now);

    private DebitOrderService Service => new(_store, _disputes, _clock);

    [Fact]
    public async Task Lists_only_the_customers_own_debit_orders_with_eligibility()
    {
        var mine = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-5)));
        _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(Guid.NewGuid()), TestData.Today.AddDays(-5)));

        var page = await Service.ListAsync(TestData.Customer, new DebitOrderListQuery(), CancellationToken.None);

        page.Items.Count.ShouldBe(1);
        page.Items[0].Id.ShouldBe(mine.Id);
        page.Items[0].Eligibility.CanDispute.ShouldBeTrue();
        page.Items[0].Eligibility.Code.ShouldBe(DisputeEligibility.Eligible);
    }

    [Fact]
    public async Task Shows_the_existing_dispute_and_blocks_a_second_one()
    {
        var debitOrder = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-5)));
        _disputes.Statuses[debitOrder.Id] = new TransactionDisputeStatus(Guid.NewGuid(), "DSP-1", "Submitted");

        var result = await Service.GetAsync(TestData.Customer, debitOrder.Id, CancellationToken.None);

        result.Value.Eligibility.CanDispute.ShouldBeFalse();
        result.Value.Eligibility.Code.ShouldBe(DisputeEligibility.AlreadyDisputed);
        result.Value.Dispute!.Reference.ShouldBe("DSP-1");
    }

    [Fact]
    public async Task Another_customers_debit_order_is_reported_as_not_found()
    {
        var theirs = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(Guid.NewGuid()), TestData.Today));

        var result = await Service.GetAsync(TestData.Customer, theirs.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }

    [Fact]
    public async Task Describes_why_a_rejected_collection_was_rejected()
    {
        var failed = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today, response: CollectionResponse.Rejected("AM04")));

        var result = await Service.GetAsync(TestData.Customer, failed.Id, CancellationToken.None);

        result.Value.StatusReason.ShouldBe("Insufficient funds");
        result.Value.Eligibility.Code.ShouldBe(DisputeEligibility.CollectionRejected);
    }

    [Fact]
    public async Task A_card_transaction_is_not_a_debit_order()
    {
        var card = _store.Add(TestData.Card());

        var result = await Service.GetAsync(TestData.Customer, card.Id, CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
    }
}

public class TransactionDirectoryTests
{
    private readonly InMemoryTransactionStore _store = new();
    private readonly FakeTimeProvider _clock = new(TestData.Now);

    private TransactionDirectory Directory => new(_store, _clock);

    [Fact]
    public async Task Reports_ineligibility_to_other_modules()
    {
        var open = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-5)));
        var closed = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-61)));

        (await Directory.FindAsync(open.Id, TestData.Customer, CancellationToken.None))!.IsEligible.ShouldBeTrue();
        (await Directory.FindAsync(closed.Id, TestData.Customer, CancellationToken.None))!.IneligibilityCode.ShouldBe("dispute_window_closed");
        (await Directory.FindAsync(open.Id, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
    }

    [Fact]
    public async Task Describes_a_debit_order_by_its_pacs003_ids_and_its_mandate()
    {
        var mandate = TestData.RegisteredMandate();
        var debitOrder = _store.Add(TestData.DebitOrder(mandate, TestData.Today.AddDays(-5)));

        var found = (await Directory.FindAsync(debitOrder.Id, TestData.Customer, CancellationToken.None))!;

        found.Type.ShouldBe(TransactionTypeNames.DebitOrder);
        found.SchemeReference.ShouldBe(debitOrder.TransactionId);
        found.Amount.ShouldBe(599m);
        found.TransactionDate.ShouldBe(TestData.Today.AddDays(-5));
        found.CounterpartyName.ShouldBe("Vodacom");
        found.DebitOrder.ShouldBe(new DebitOrderForDispute("E2E-1", "Registered", mandate.MandateReference));
        found.Card.ShouldBeNull();
    }

    [Fact]
    public async Task Describes_an_eft_debit_order_without_a_mandate_reference()
    {
        var debitOrder = _store.Add(TestData.EftDebitOrder(TestData.Today.AddDays(-5)));

        var found = (await Directory.FindAsync(debitOrder.Id, TestData.Customer, CancellationToken.None))!;

        found.IsEligible.ShouldBeTrue();
        found.CounterpartyName.ShouldBe("City of Johannesburg");
        found.DebitOrder.ShouldBe(new DebitOrderForDispute("E2E-1", "Eft", MandateReference: null));
    }

    [Fact]
    public async Task Describes_a_card_purchase_by_its_acquirer_reference_and_how_the_card_was_used()
    {
        var details = TestData.CardDetails(postingDate: TestData.Today.AddDays(-3));
        var card = _store.Add(TestData.Card(details));

        var found = (await Directory.FindAsync(card.Id, TestData.Customer, CancellationToken.None))!;

        found.Type.ShouldBe(TransactionTypeNames.Card);
        found.IsEligible.ShouldBeTrue();
        found.SchemeReference.ShouldBe(details.AcquirerReferenceNumber);
        found.TransactionDate.ShouldBe(details.TransactionDate);
        found.CounterpartyName.ShouldBe("Takealot");
        found.Card.ShouldBe(new CardForDispute("Visa", "ECommerce", "None", details.PostingDate));
        found.DebitOrder.ShouldBeNull();
    }

    [Fact]
    public async Task A_partly_refunded_card_purchase_can_only_be_disputed_for_what_is_outstanding()
    {
        var card = _store.Add(TestData.Card(TestData.CardDetails(amount: 300m) with { RefundedAmount = Money.Zar(120m) }));

        var found = (await Directory.FindAsync(card.Id, TestData.Customer, CancellationToken.None))!;

        found.Amount.ShouldBe(180m);
    }

    [Theory]
    [InlineData(nameof(CardTransactionKind.CashWithdrawal), nameof(CardSettlementStatus.Settled), 0, "not_a_purchase")]
    [InlineData(nameof(CardTransactionKind.Purchase), nameof(CardSettlementStatus.Reversed), 0, "not_settled")]
    [InlineData(nameof(CardTransactionKind.Purchase), nameof(CardSettlementStatus.Settled), 1899, "fully_refunded")]
    public async Task Reports_why_a_card_transaction_cannot_be_disputed(string kind, string status, decimal refunded, string expectedCode)
    {
        var card = _store.Add(TestData.Card(TestData.CardDetails() with
        {
            Kind = Enum.Parse<CardTransactionKind>(kind),
            Status = Enum.Parse<CardSettlementStatus>(status),
            RefundedAmount = Money.Zar(refunded),
        }));

        var found = (await Directory.FindAsync(card.Id, TestData.Customer, CancellationToken.None))!;

        found.IsEligible.ShouldBeFalse();
        found.IneligibilityCode.ShouldBe(expectedCode);
        found.IneligibilityMessage.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task A_card_purchase_outside_the_window_names_the_one_hundred_and_twenty_days()
    {
        var card = _store.Add(TestData.Card(TestData.CardDetails(postingDate: TestData.Today.AddDays(-121))));

        var found = (await Directory.FindAsync(card.Id, TestData.Customer, CancellationToken.None))!;

        found.IneligibilityCode.ShouldBe("dispute_window_closed");
        found.IneligibilityMessage!.ShouldContain("120-day");
    }
}

public class DebitOrderListQueryValidatorTests
{
    private readonly DebitOrderListQueryValidator _validator = new();

    [Fact]
    public void Accepts_an_empty_query()
    {
        _validator.TestValidate(new DebitOrderListQuery()).ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Rejects_out_of_range_paging_an_unknown_sort_and_an_inverted_date_range()
    {
        var result = _validator.TestValidate(new DebitOrderListQuery
        {
            Page = 0,
            PageSize = 101,
            Sort = "creditor",
            From = new DateOnly(2026, 11, 2),
            To = new DateOnly(2026, 11, 1),
        });

        result.ShouldHaveValidationErrorFor(q => q.Page);
        result.ShouldHaveValidationErrorFor(q => q.PageSize);
        result.ShouldHaveValidationErrorFor(q => q.Sort);
        result.ShouldHaveValidationErrorFor(q => q.To);
    }

    [Theory]
    [InlineData("-actionDate", nameof(DebitOrderSort.ActionDateDescending))]
    [InlineData("amount", nameof(DebitOrderSort.AmountAscending))]
    [InlineData(null, nameof(DebitOrderSort.ActionDateDescending))]
    public void Parses_the_sort_parameter(string? value, string expected)
    {
        DebitOrderSorts.Parse(value).ToString().ShouldBe(expected);
    }
}

internal sealed class InMemoryTransactionStore : ITransactionStore
{
    private readonly List<Transaction> _transactions = [];
    private readonly List<Mandate> _mandates = [];

    public T Add<T>(T transaction)
        where T : Transaction
    {
        _transactions.Add(transaction);
        return transaction;
    }

    public Task<PagedResult<DebitOrder>> ListAsync(Guid customerId, DebitOrderFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var items = _transactions.OfType<DebitOrder>().Where(d => d.CustomerId == customerId).OrderByDescending(d => d.EffectiveDate).ToList();
        return Task.FromResult(new PagedResult<DebitOrder>(items.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, items.Count));
    }

    public Task<Transaction?> FindAsync(Guid transactionId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_transactions.FirstOrDefault(t => t.Id == transactionId && t.CustomerId == customerId));

    public Task<PagedResult<Mandate>> ListMandatesAsync(Guid customerId, MandateType? type, PageRequest page, CancellationToken cancellationToken)
    {
        var items = _mandates.Where(m => m.CustomerId == customerId && (type is null || m.Type == type)).ToList();
        return Task.FromResult(new PagedResult<Mandate>(items, page.Page, page.PageSize, items.Count));
    }

    public Task<Mandate?> FindMandateAsync(Guid mandateId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_mandates.FirstOrDefault(m => m.Id == mandateId && m.CustomerId == customerId));
}

internal sealed class FakeDisputeStatusProvider : IDisputeStatusProvider
{
    public Dictionary<Guid, TransactionDisputeStatus> Statuses { get; } = [];

    public Task<IReadOnlyDictionary<Guid, TransactionDisputeStatus>> GetForTransactionsAsync(IReadOnlyCollection<Guid> transactionIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, TransactionDisputeStatus>>(
            Statuses.Where(pair => transactionIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
}
