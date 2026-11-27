using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.UnitTests.Transactions;

public class CardDisputabilityPolicyTests
{
    [Fact]
    public void A_settled_purchase_is_disputable()
    {
        var decision = Evaluate();

        decision.IsDisputable.ShouldBeTrue();
        decision.Reason.ShouldBe(DisputabilityReason.CardPurchase);
    }

    [Fact]
    public void A_cash_withdrawal_is_not_disputable()
    {
        var decision = Evaluate(kind: CardTransactionKind.CashWithdrawal);

        decision.IsDisputable.ShouldBeFalse();
        decision.Reason.ShouldBe(DisputabilityReason.NotAPurchase);
    }

    [Theory]
    [InlineData(nameof(CardSettlementStatus.Pending))]
    [InlineData(nameof(CardSettlementStatus.Declined))]
    [InlineData(nameof(CardSettlementStatus.Reversed))]
    public void A_purchase_that_is_not_settled_is_not_disputable(string status)
    {
        var decision = Evaluate(status: Enum.Parse<CardSettlementStatus>(status));

        decision.IsDisputable.ShouldBeFalse();
        decision.Reason.ShouldBe(DisputabilityReason.NotSettled);
    }

    [Fact]
    public void A_fully_refunded_purchase_is_not_disputable()
    {
        var decision = Evaluate(amount: 500m, refunded: 500m);

        decision.IsDisputable.ShouldBeFalse();
        decision.Reason.ShouldBe(DisputabilityReason.FullyRefunded);
    }

    [Fact]
    public void A_partly_refunded_purchase_is_still_disputable()
    {
        Evaluate(amount: 500m, refunded: 499.99m).IsDisputable.ShouldBeTrue();
    }

    [Fact]
    public void The_last_dispute_date_is_one_hundred_and_twenty_days_after_the_posting_date()
    {
        // Day 1 is the day after posting, so day 120 is the posting date plus 120.
        CardDisputabilityPolicy.LastDisputeDate(new DateOnly(2026, 7, 25)).ShouldBe(new DateOnly(2026, 11, 22));
    }

    private static DisputabilityDecision Evaluate(
        CardTransactionKind kind = CardTransactionKind.Purchase,
        CardSettlementStatus status = CardSettlementStatus.Settled,
        decimal amount = 500m,
        decimal refunded = 0m) =>
        CardDisputabilityPolicy.Evaluate(kind, status, Money.Zar(amount), Money.Zar(refunded));
}

public class CardTransactionTests
{
    [Fact]
    public void Disputability_is_decided_and_stored_when_the_transaction_is_recorded()
    {
        var card = TestData.Card(TestData.CardDetails(postingDate: new DateOnly(2026, 11, 10)));

        card.Type.ShouldBe(TransactionType.Card);
        card.CounterpartyName.ShouldBe("Takealot");
        card.EffectiveDate.ShouldBe(new DateOnly(2026, 11, 10));
        card.IsDisputable.ShouldBeTrue();
        card.DisputabilityReason.ShouldBe(DisputabilityReason.CardPurchase);
        card.LastDisputeDate.ShouldBe(new DateOnly(2027, 3, 10));
    }

    [Fact]
    public void Day_one_hundred_and_twenty_is_the_last_day_a_dispute_is_allowed()
    {
        var card = TestData.Card(TestData.CardDetails(postingDate: TestData.Today.AddDays(-120)));

        card.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.Eligible);
    }

    [Fact]
    public void Day_one_hundred_and_twenty_one_is_outside_the_dispute_window()
    {
        var card = TestData.Card(TestData.CardDetails(postingDate: TestData.Today.AddDays(-121)));

        card.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.DisputeWindowClosed);
    }

    [Fact]
    public void A_purchase_that_already_has_a_dispute_cannot_be_disputed_again()
    {
        TestData.Card().EvaluateEligibility(TestData.Today, alreadyDisputed: true).ShouldBe(DisputeEligibility.AlreadyDisputed);
    }

    [Fact]
    public void A_cash_withdrawal_is_refused_as_not_a_purchase()
    {
        var card = TestData.Card(TestData.CardDetails() with { Kind = CardTransactionKind.CashWithdrawal });

        card.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.NotAPurchase);
    }

    [Fact]
    public void A_pending_purchase_is_refused_as_not_settled_and_counts_from_its_transaction_date()
    {
        var details = TestData.CardDetails() with { Status = CardSettlementStatus.Pending, PostingDate = null };

        var card = TestData.Card(details);

        card.EffectiveDate.ShouldBe(details.TransactionDate);
        card.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.NotSettled);
    }

    [Fact]
    public void A_fully_refunded_purchase_is_refused_and_has_nothing_outstanding()
    {
        var card = TestData.Card(TestData.CardDetails(amount: 300m) with { RefundedAmount = Money.Zar(300m) });

        card.OutstandingAmount.ShouldBe(0m);
        card.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.FullyRefunded);
    }

    [Fact]
    public void A_partly_refunded_purchase_can_be_disputed_for_what_is_outstanding()
    {
        var card = TestData.Card(TestData.CardDetails(amount: 300m) with { RefundedAmount = Money.Zar(120m) });

        card.OutstandingAmount.ShouldBe(180m);
        card.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.Eligible);
    }

    [Fact]
    public void Only_the_last_four_digits_of_the_card_number_are_kept()
    {
        TestData.Card().CardNumberMasked.ShouldBe("************4821");
    }

    [Fact]
    public void A_settled_transaction_must_have_a_posting_date()
    {
        Should.Throw<ArgumentException>(() => TestData.Card(TestData.CardDetails() with { PostingDate = null }));
    }

    [Fact]
    public void The_refund_cannot_be_more_than_the_amount()
    {
        Should.Throw<ArgumentException>(() => TestData.Card(TestData.CardDetails(amount: 300m) with { RefundedAmount = Money.Zar(300.01m) }));
    }

    [Theory]
    [InlineData("ZAF", "5311")]
    [InlineData("ZA", "531")]
    [InlineData("ZA", "53A1")]
    public void The_merchant_country_and_category_code_must_be_well_formed(string country, string categoryCode)
    {
        var details = TestData.CardDetails() with { MerchantCountry = country, MerchantCategoryCode = categoryCode };

        Should.Throw<ArgumentException>(() => TestData.Card(details));
    }
}
