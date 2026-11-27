using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Contracts;
using DisputePortal.Modules.Transactions.Domain;

namespace DisputePortal.UnitTests.Disputes;

/// <summary>
/// Modules exchange enum values as text, because each module keeps its enums internal. These tests fail when
/// one side renames a value the other side depends on.
/// </summary>
public class ContractValueTests
{
    [Fact]
    public void The_dispute_rules_use_the_names_the_transactions_module_sends()
    {
        TransactionValues.DebiCheck.ShouldBe(nameof(DebitOrderType.DebiCheck));
        TransactionValues.Visa.ShouldBe(nameof(CardScheme.Visa));
        TransactionValues.Mastercard.ShouldBe(nameof(CardScheme.Mastercard));
        TransactionValues.ECommerce.ShouldBe(nameof(CardEntryMode.ECommerce));
        TransactionValues.Recurring.ShouldBe(nameof(CardEntryMode.Recurring));
        TransactionValues.Pin.ShouldBe(nameof(CardholderVerification.Pin));
        TransactionValues.ThreeDSecure.ShouldBe(nameof(CardholderVerification.ThreeDSecure));
    }

    [Fact]
    public void Every_card_scheme_has_dispute_reason_codes()
    {
        foreach (var scheme in Enum.GetNames<CardScheme>())
        {
            DisputeReasons.OfferedFor(DisputeTestData.Card(scheme: scheme)).ShouldNotBeEmpty();
        }
    }

    [Fact]
    public void Transaction_types_have_the_same_names_in_every_module()
    {
        string[] expected = [nameof(TransactionType.DebitOrder), nameof(TransactionType.Card)];

        Enum.GetNames<TransactionType>().ShouldBe(expected);
        Enum.GetNames<DisputedTransactionType>().ShouldBe(expected);
        new[] { TransactionTypeNames.DebitOrder, TransactionTypeNames.Card }.ShouldBe(expected);
        new[] { DisputedTransactionTypes.DebitOrder, DisputedTransactionTypes.Card }.ShouldBe(expected);
    }
}
