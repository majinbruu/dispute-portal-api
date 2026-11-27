using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.UnitTests.Transactions;

public class DebitOrderDisputabilityPolicyTests
{
    [Fact]
    public void A_registered_mandate_collection_is_disputable()
    {
        var decision = DebitOrderDisputabilityPolicy.Evaluate(TestData.RegisteredMandate(), Money.Zar(599m), new DateOnly(2026, 11, 1), accepted: true);

        decision.IsDisputable.ShouldBeTrue();
        decision.Reason.ShouldBe(DisputabilityReason.RegisteredMandate);
    }

    [Theory]
    [InlineData(449.00)]  // the instalment
    [InlineData(673.50)]  // exactly the maximum
    public void A_debicheck_collection_within_the_mandate_terms_is_not_disputable(decimal amount)
    {
        var mandate = TestData.DebiCheckMandate(maximum: 673.50m, collectionDay: 15);

        var decision = DebitOrderDisputabilityPolicy.Evaluate(mandate, Money.Zar(amount), new DateOnly(2026, 11, 15), accepted: true);

        decision.IsDisputable.ShouldBeFalse();
        decision.Reason.ShouldBe(DisputabilityReason.WithinMandateTerms);
    }

    [Fact]
    public void A_debicheck_collection_above_the_mandate_maximum_is_disputable()
    {
        var mandate = TestData.DebiCheckMandate(maximum: 673.50m, collectionDay: 15);

        var decision = DebitOrderDisputabilityPolicy.Evaluate(mandate, Money.Zar(673.51m), new DateOnly(2026, 11, 15), accepted: true);

        decision.IsDisputable.ShouldBeTrue();
        decision.Reason.ShouldBe(DisputabilityReason.AmountAboveMandateMaximum);
    }

    [Fact]
    public void A_debicheck_collection_on_the_wrong_day_is_disputable_when_date_adjustment_is_not_allowed()
    {
        var mandate = TestData.DebiCheckMandate(collectionDay: 15, dateAdjustmentAllowed: false);

        var decision = DebitOrderDisputabilityPolicy.Evaluate(mandate, Money.Zar(449m), new DateOnly(2026, 11, 18), accepted: true);

        decision.IsDisputable.ShouldBeTrue();
        decision.Reason.ShouldBe(DisputabilityReason.DateOutsideMandateTerms);
    }

    [Fact]
    public void A_debicheck_collection_on_a_different_day_is_not_disputable_when_date_adjustment_is_allowed()
    {
        var mandate = TestData.DebiCheckMandate(collectionDay: 15, dateAdjustmentAllowed: true);

        var decision = DebitOrderDisputabilityPolicy.Evaluate(mandate, Money.Zar(449m), new DateOnly(2026, 11, 18), accepted: true);

        decision.IsDisputable.ShouldBeFalse();
    }

    [Fact]
    public void A_debicheck_collection_before_the_first_collection_date_is_disputable()
    {
        var mandate = TestData.DebiCheckMandate(collectionDay: 15, firstCollectionDate: new DateOnly(2026, 12, 15));

        var decision = DebitOrderDisputabilityPolicy.Evaluate(mandate, Money.Zar(449m), new DateOnly(2026, 11, 15), accepted: true);

        decision.IsDisputable.ShouldBeTrue();
        decision.Reason.ShouldBe(DisputabilityReason.DateOutsideMandateTerms);
    }

    [Theory]
    [InlineData("2026-11-16", false)]  // six weeks after the first collection
    [InlineData("2026-11-17", true)]   // a day late
    public void A_weekly_debicheck_collection_is_checked_against_the_weekly_schedule(string actionDate, bool disputable)
    {
        var mandate = TestData.DebiCheckMandate(frequency: CollectionFrequency.Weekly, firstCollectionDate: new DateOnly(2026, 10, 5));

        var decision = DebitOrderDisputabilityPolicy.Evaluate(mandate, Money.Zar(449m), DateOnly.Parse(actionDate), accepted: true);

        decision.IsDisputable.ShouldBe(disputable);
    }

    [Fact]
    public void A_rejected_collection_is_not_disputable_whatever_the_mandate_says()
    {
        var decision = DebitOrderDisputabilityPolicy.Evaluate(TestData.RegisteredMandate(), Money.Zar(599m), new DateOnly(2026, 11, 1), accepted: false);

        decision.IsDisputable.ShouldBeFalse();
        decision.Reason.ShouldBe(DisputabilityReason.CollectionRejected);
    }

    [Fact]
    public void The_last_dispute_date_is_sixty_days_after_the_action_date()
    {
        // Day 1 is the day after the debit, so day 60 is the action date plus 60.
        DebitOrderDisputabilityPolicy.LastDisputeDate(new DateOnly(2026, 9, 23)).ShouldBe(new DateOnly(2026, 11, 22));
    }
}

public class DebitOrderEligibilityTests
{
    [Fact]
    public void Disputability_is_decided_and_stored_when_the_collection_is_recorded()
    {
        var debitOrder = TestData.DebitOrder(TestData.DebiCheckMandate(maximum: 673.50m), new DateOnly(2026, 11, 15), amount: 899m);

        debitOrder.IsDisputable.ShouldBeTrue();
        debitOrder.DisputabilityReason.ShouldBe(DisputabilityReason.AmountAboveMandateMaximum);
        debitOrder.LastDisputeDate.ShouldBe(new DateOnly(2027, 1, 14));
    }

    [Fact]
    public void A_successful_registered_debit_order_inside_the_window_is_eligible()
    {
        var debitOrder = TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-5));

        debitOrder.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.Eligible);
    }

    [Fact]
    public void Day_sixty_is_the_last_day_a_dispute_is_allowed()
    {
        var debitOrder = TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-60));

        debitOrder.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.Eligible);
    }

    [Fact]
    public void Day_sixty_one_is_outside_the_dispute_window()
    {
        var debitOrder = TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-61));

        debitOrder.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.DisputeWindowClosed);
    }

    [Fact]
    public void A_rejected_collection_cannot_be_disputed_because_nothing_was_debited()
    {
        var debitOrder = TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-5), response: CollectionResponse.Rejected("AM04"));

        debitOrder.Status.ShouldBe(DebitOrderStatus.Rejected);
        debitOrder.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.CollectionRejected);
    }

    [Fact]
    public void A_debit_order_that_already_has_a_dispute_cannot_be_disputed_again()
    {
        var debitOrder = TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today.AddDays(-5));

        debitOrder.EvaluateEligibility(TestData.Today, alreadyDisputed: true).ShouldBe(DisputeEligibility.AlreadyDisputed);
    }

    [Fact]
    public void A_debicheck_debit_order_within_terms_is_not_eligible()
    {
        var debitOrder = TestData.DebitOrder(TestData.DebiCheckMandate(collectionDay: 15), new DateOnly(2026, 11, 15), amount: 449m);

        debitOrder.EvaluateEligibility(TestData.Today, alreadyDisputed: false).ShouldBe(DisputeEligibility.WithinMandateTerms);
    }

    [Fact]
    public void A_rejected_collection_must_carry_a_reason_code()
    {
        Should.Throw<ArgumentException>(() =>
            TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today, response: new CollectionResponse(false)));
    }

    [Fact]
    public void Only_the_last_four_digits_of_the_account_number_are_kept()
    {
        var debitOrder = TestData.DebitOrder(TestData.RegisteredMandate(), TestData.Today);

        debitOrder.AccountMasked.ShouldBe("******7890");
        NumberMask.KeepLastFour("62 1234 5678").ShouldBe("******5678");
        Should.Throw<ArgumentException>(() => NumberMask.KeepLastFour("123"));
    }
}

public class MandateTests
{
    [Fact]
    public void The_maximum_collection_amount_cannot_be_below_the_instalment()
    {
        Should.Throw<ArgumentException>(() => TestData.DebiCheckMandate(instalment: 500m, maximum: 499.99m));
    }

    [Fact]
    public void A_debicheck_mandate_must_record_its_authentication()
    {
        var details = new MandateDetails(
            Guid.NewGuid(), TestData.Customer, "MRN1", "C-1", MandateType.DebiCheck, "MTN South Africa", "MTN SA",
            Money.Zar(100m), Money.Zar(150m), CollectionFrequency.Monthly, 15, new DateOnly(2025, 1, 15),
            DateAdjustmentAllowed: false, TrackingEnabled: true);

        Should.Throw<ArgumentException>(() => Mandate.Create(details, TestData.Now));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(32)]
    public void The_collection_day_must_be_a_day_of_the_month(int day)
    {
        var details = new MandateDetails(
            Guid.NewGuid(), TestData.Customer, "MRN1", "C-1", MandateType.Registered, "Vodacom", "VODACOM",
            Money.Zar(100m), Money.Zar(150m), CollectionFrequency.Monthly, day, new DateOnly(2025, 1, 15),
            DateAdjustmentAllowed: true, TrackingEnabled: true);

        Should.Throw<ArgumentOutOfRangeException>(() => Mandate.Create(details, TestData.Now));
    }
}
