using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using FluentValidation.TestHelper;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Transactions;

public class DebitOrderServiceTests
{
    private readonly InMemoryDebitOrderStore _store = new();
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
        _disputes.Statuses[debitOrder.Id] = new DebitOrderDisputeStatus(Guid.NewGuid(), "DSP-1", "Submitted");

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
    public async Task The_directory_reports_ineligibility_to_other_modules()
    {
        var directory = new DebitOrderDirectory(_store, _clock);
        var open = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-5)));
        var closed = _store.Add(TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-61)));

        (await directory.FindAsync(open.Id, TestData.Customer, CancellationToken.None))!.IsEligible.ShouldBeTrue();
        (await directory.FindAsync(closed.Id, TestData.Customer, CancellationToken.None))!.IneligibilityCode.ShouldBe("dispute_window_closed");
        (await directory.FindAsync(open.Id, Guid.NewGuid(), CancellationToken.None)).ShouldBeNull();
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

internal sealed class InMemoryDebitOrderStore : IDebitOrderStore
{
    private readonly List<DebitOrder> _debitOrders = [];
    private readonly List<Mandate> _mandates = [];

    public DebitOrder Add(DebitOrder debitOrder)
    {
        _debitOrders.Add(debitOrder);
        return debitOrder;
    }

    public Task<PagedResult<DebitOrder>> ListAsync(Guid customerId, DebitOrderFilter filter, PageRequest page, CancellationToken cancellationToken)
    {
        var items = _debitOrders.Where(d => d.CustomerId == customerId).OrderByDescending(d => d.ActionDate).ToList();
        return Task.FromResult(new PagedResult<DebitOrder>(items.Skip(page.Skip).Take(page.PageSize).ToList(), page.Page, page.PageSize, items.Count));
    }

    public Task<DebitOrder?> FindAsync(Guid debitOrderId, Guid customerId, CancellationToken cancellationToken) =>
        Task.FromResult(_debitOrders.FirstOrDefault(d => d.Id == debitOrderId && d.CustomerId == customerId));

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
    public Dictionary<Guid, DebitOrderDisputeStatus> Statuses { get; } = [];

    public Task<IReadOnlyDictionary<Guid, DebitOrderDisputeStatus>> GetForDebitOrdersAsync(IReadOnlyCollection<Guid> debitOrderIds, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, DebitOrderDisputeStatus>>(
            Statuses.Where(pair => debitOrderIds.Contains(pair.Key)).ToDictionary(pair => pair.Key, pair => pair.Value));
}
