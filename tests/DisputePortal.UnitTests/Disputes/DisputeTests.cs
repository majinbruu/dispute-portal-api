using System.Text.Json;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.UnitTests.Disputes;

internal static class DisputeTestData
{
    public const string Registered = "Registered";
    public const string Eft = "Eft";

    public static readonly Guid Customer = Guid.Parse("00000000-0000-4000-8000-0000000000aa");
    public static readonly Guid Agent = Guid.Parse("00000000-0000-4000-8000-0000000000bb");
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    public static DisputedTransaction DebitOrder(string mandateType = Registered, string transactionId = "ACME20261101000001") => new(
        Guid.NewGuid(), DisputedTransactionType.DebitOrder, transactionId, Money.Zar(599m), new DateOnly(2026, 11, 1),
        "Vodacom", "******7890",
        DebitOrder: new DisputedDebitOrder("VOD-1-20261101", mandateType, mandateType == Eft ? null : "MRN000000001"));

    public static DisputedTransaction Card(
        string scheme = TransactionValues.Visa,
        string entryMode = TransactionValues.ECommerce,
        string verification = "None",
        string acquirerReferenceNumber = "74027000000000000000001") => new(
        Guid.NewGuid(), DisputedTransactionType.Card, acquirerReferenceNumber, Money.Zar(1899m), new DateOnly(2026, 11, 18),
        "Takealot", "******7890",
        Card: new DisputedCard(scheme, entryMode, verification, new DateOnly(2026, 11, 19)));

    public static Dispute Submitted(string reasonId = "not_authorised", string mandateType = Registered, string transactionId = "ACME20261101000001") =>
        Submitted(DebitOrder(mandateType, transactionId), reasonId);

    public static Dispute Submitted(DisputedTransaction transaction, string reasonId = "not_authorised", string? comment = null) =>
        Dispute.Submit(Guid.NewGuid(), "DSP-20261122-ABC234", Customer, transaction, reasonId, comment, Now);

    /// <summary>A dispute an agent approved, so it is with the scheme.</summary>
    public static Dispute SentToScheme()
    {
        var dispute = Submitted();
        dispute.MarkSentToScheme("{}", DisputeActor.Agent(Agent), Now);
        return dispute;
    }
}

public class DisputeLifecycleTests
{
    [Fact]
    public void A_new_dispute_is_submitted_for_the_full_amount_with_a_first_event()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.Status.ShouldBe(DisputeStatus.Submitted);
        dispute.Amount.ShouldBe(599m);
        dispute.IsCompleted.ShouldBeFalse();
        dispute.Events.Select(e => e.Status).ShouldBe([DisputeStatus.Submitted]);
        dispute.DomainEvents.Count.ShouldBe(1);
    }

    [Fact]
    public void A_debit_order_dispute_keeps_the_reason_id_the_iso_code_and_the_mandate()
    {
        var dispute = DisputeTestData.Submitted("incorrect_amount");

        dispute.TransactionType.ShouldBe(DisputedTransactionType.DebitOrder);
        dispute.ReasonId.ShouldBe("incorrect_amount");
        dispute.ReasonCode.ShouldBe("AM09");
        dispute.SchemeReference.ShouldBe("ACME20261101000001");
        dispute.MandateReference.ShouldBe("MRN000000001");
        dispute.CardScheme.ShouldBeNull();
    }

    [Fact]
    public void A_card_dispute_keeps_the_reason_id_the_scheme_code_and_the_card_details()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.Card(scheme: TransactionValues.Mastercard), "not_received");

        dispute.TransactionType.ShouldBe(DisputedTransactionType.Card);
        dispute.ReasonId.ShouldBe("not_received");
        dispute.ReasonCode.ShouldBe("4853");
        dispute.SchemeReference.ShouldBe("74027000000000000000001");
        dispute.CardScheme.ShouldBe("Mastercard");
        dispute.PostingDate.ShouldBe(new DateOnly(2026, 11, 19));
        dispute.CounterpartyName.ShouldBe("Takealot");
        dispute.MandateType.ShouldBeNull();
        dispute.MandateReference.ShouldBeNull();
        dispute.OriginalEndToEndId.ShouldBeNull();
    }

    [Fact]
    public void A_dispute_moves_from_submitted_to_sent_to_refunded()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now.AddSeconds(10));
        dispute.MarkRefunded(DisputeTestData.Now.AddSeconds(30));

        dispute.Status.ShouldBe(DisputeStatus.Refunded);
        dispute.IsCompleted.ShouldBeTrue();
        dispute.SentToSchemeAt.ShouldBe(DisputeTestData.Now.AddSeconds(10));
        dispute.CompletedAt.ShouldBe(DisputeTestData.Now.AddSeconds(30));
        dispute.OutcomeNote.ShouldNotBeNullOrWhiteSpace();
        dispute.Events.Select(e => e.Status).ShouldBe([DisputeStatus.Submitted, DisputeStatus.SentToScheme, DisputeStatus.Refunded]);
    }

    [Fact]
    public void A_dispute_can_be_declined_with_the_reason_given_by_the_scheme()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now);

        dispute.MarkDeclined("Already reversed.", DisputeTestData.Now.AddSeconds(20));

        dispute.Status.ShouldBe(DisputeStatus.Declined);
        dispute.IsCompleted.ShouldBeTrue();
        dispute.OutcomeNote.ShouldBe("Already reversed.");
    }

    [Fact]
    public void A_dispute_cannot_be_refunded_before_it_is_sent_to_the_scheme()
    {
        var dispute = DisputeTestData.Submitted();

        Should.Throw<DisputeRuleException>(() => dispute.MarkRefunded(DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => dispute.MarkDeclined("No.", DisputeTestData.Now));
    }

    [Fact]
    public void A_dispute_cannot_be_sent_to_the_scheme_twice()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now));
    }

    [Fact]
    public void A_completed_dispute_cannot_change_again()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now);
        dispute.MarkRefunded(DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => dispute.MarkDeclined("No.", DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => dispute.MarkRefunded(DisputeTestData.Now));
    }

    [Fact]
    public void Not_authorised_cannot_be_used_against_a_debicheck_debit_order()
    {
        Should.Throw<DisputeRuleException>(() => DisputeTestData.Submitted("not_authorised", TransactionValues.DebiCheck));
    }

    [Fact]
    public void A_card_reason_cannot_be_used_against_a_debit_order()
    {
        Should.Throw<DisputeRuleException>(() => DisputeTestData.Submitted("not_received"));
    }

    [Fact]
    public void A_dispute_must_carry_the_details_of_its_own_transaction_type()
    {
        var cardWithoutDetails = DisputeTestData.Card() with { Card = null };
        var debitOrderWithCardDetails = DisputeTestData.DebitOrder() with { Card = DisputeTestData.Card().Card };

        Should.Throw<DisputeRuleException>(() => DisputeTestData.Submitted(cardWithoutDetails, "duplicate"));
        Should.Throw<DisputeRuleException>(() => DisputeTestData.Submitted(debitOrderWithCardDetails, "duplicate"));
    }

    [Fact]
    public void Every_status_change_raises_a_domain_event_for_the_outbox()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now);
        dispute.MarkRefunded(DisputeTestData.Now);

        dispute.DomainEvents.OfType<DisputeStatusChangedDomainEvent>().Select(e => e.Status).ShouldBe([DisputeStatus.Submitted, DisputeStatus.SentToScheme, DisputeStatus.Refunded]);

        dispute.ClearDomainEvents();
        dispute.DomainEvents.ShouldBeEmpty();
    }
}

public class DebitOrderDisputeReasonTests
{
    [Theory]
    [InlineData("Registered")]
    [InlineData("Eft")]
    public void A_debit_order_the_customer_did_not_authenticate_allows_all_five_reasons(string mandateType)
    {
        var reasons = DisputeReasons.OfferedFor(DisputeTestData.DebitOrder(mandateType));

        reasons.Select(r => r.Id).ShouldBe(["not_authorised", "incorrect_amount", "wrong_date", "cancelled", "duplicate"]);
        reasons.Select(r => r.Code).ShouldBe(["MD01", "AM09", "DT01", "MD06", "AM05"]);
    }

    [Fact]
    public void A_debicheck_debit_order_does_not_allow_not_authorised()
    {
        var reasons = DisputeReasons.OfferedFor(DisputeTestData.DebitOrder(TransactionValues.DebiCheck));

        reasons.Select(r => r.Code).ShouldBe(["AM09", "DT01", "MD06", "AM05"]);
    }

    [Fact]
    public void An_unknown_reason_or_one_that_is_not_offered_does_not_resolve()
    {
        DisputeReasons.Resolve("nonsense", DisputeTestData.DebitOrder()).ShouldBeNull();
        DisputeReasons.Resolve(null, DisputeTestData.DebitOrder()).ShouldBeNull();
        DisputeReasons.Resolve("not_authorised", DisputeTestData.DebitOrder(TransactionValues.DebiCheck)).ShouldBeNull();
    }

    [Fact]
    public void A_stored_reason_is_described_in_the_wording_of_its_transaction_type()
    {
        DisputeReasons.LabelOf(DisputedTransactionType.DebitOrder, "not_authorised").ShouldBe("I did not authorise this debit order");
        DisputeReasons.LabelOf(DisputedTransactionType.Card, "not_authorised").ShouldBe("I did not make this transaction");
        DisputeReasons.LabelOf(DisputedTransactionType.Card, "retired_reason").ShouldBe("retired_reason");
    }
}

public class CardDisputeReasonTests
{
    [Theory]
    [InlineData("None", true)]
    [InlineData("Pin", false)]
    [InlineData("ThreeDSecure", false)]
    public void Not_authorised_is_offered_only_when_the_cardholder_was_not_verified(string verification, bool offered)
    {
        var reasons = DisputeReasons.OfferedFor(DisputeTestData.Card(verification: verification));

        reasons.Any(r => r.Id == "not_authorised").ShouldBe(offered);
    }

    [Theory]
    [InlineData("Recurring", true)]
    [InlineData("ECommerce", false)]
    [InlineData("Chip", false)]
    [InlineData("Contactless", false)]
    public void Cancelled_subscription_is_offered_only_for_a_recurring_transaction(string entryMode, bool offered)
    {
        var reasons = DisputeReasons.OfferedFor(DisputeTestData.Card(entryMode: entryMode));

        reasons.Any(r => r.Id == "cancelled_recurring").ShouldBe(offered);
    }

    [Fact]
    public void A_chip_and_pin_purchase_offers_the_four_reasons_that_always_apply()
    {
        var reasons = DisputeReasons.OfferedFor(DisputeTestData.Card(entryMode: "Chip", verification: "Pin"));

        reasons.Select(r => r.Id).ShouldBe(["duplicate", "incorrect_amount", "not_received", "credit_not_processed"]);
    }

    [Theory]
    [InlineData("not_authorised", "ECommerce", "10.4")]   // card not present
    [InlineData("not_authorised", "Recurring", "10.4")]   // card not present
    [InlineData("not_authorised", "Chip", "10.3")]        // card present
    [InlineData("not_authorised", "Contactless", "10.3")] // card present
    [InlineData("duplicate", "ECommerce", "12.6")]
    [InlineData("incorrect_amount", "ECommerce", "12.5")]
    [InlineData("not_received", "ECommerce", "13.1")]
    [InlineData("cancelled_recurring", "Recurring", "13.2")]
    [InlineData("credit_not_processed", "ECommerce", "13.6")]
    public void A_visa_reason_resolves_to_its_dispute_condition_code(string reasonId, string entryMode, string expectedCode)
    {
        var reason = DisputeReasons.Resolve(reasonId, DisputeTestData.Card(TransactionValues.Visa, entryMode));

        reason!.Code.ShouldBe(expectedCode);
    }

    [Theory]
    [InlineData("not_authorised", "ECommerce", "4837")]
    [InlineData("not_authorised", "Chip", "4837")]
    [InlineData("duplicate", "ECommerce", "4834")]
    [InlineData("incorrect_amount", "ECommerce", "4834")]
    [InlineData("not_received", "ECommerce", "4853")]
    [InlineData("cancelled_recurring", "Recurring", "4853")]
    [InlineData("credit_not_processed", "ECommerce", "4853")]
    public void A_mastercard_reason_resolves_to_its_chargeback_reason_code(string reasonId, string entryMode, string expectedCode)
    {
        var reason = DisputeReasons.Resolve(reasonId, DisputeTestData.Card(TransactionValues.Mastercard, entryMode));

        reason!.Code.ShouldBe(expectedCode);
    }

    [Fact]
    public void A_debit_order_reason_does_not_resolve_for_a_card()
    {
        DisputeReasons.Resolve("wrong_date", DisputeTestData.Card()).ShouldBeNull();
    }

    [Fact]
    public void A_card_scheme_without_reason_codes_is_a_bug_not_a_business_outcome()
    {
        Should.Throw<DisputeRuleException>(() => DisputeReasons.OfferedFor(DisputeTestData.Card(scheme: "Amex")));
    }

    [Fact]
    public void Every_reason_id_is_known_to_the_request_validator()
    {
        DisputeReasons.KnownIds.ShouldBe(
            ["not_authorised", "incorrect_amount", "wrong_date", "cancelled", "duplicate", "not_received", "cancelled_recurring", "credit_not_processed"],
            ignoreOrder: true);
    }
}

public class ReturnMessageFactoryTests
{
    [Fact]
    public void Builds_a_pacs004_payload_that_references_the_original_collection()
    {
        var dispute = DisputeTestData.Submitted("incorrect_amount");

        using var payload = JsonDocument.Parse(ReturnMessageFactory.Create(dispute, DisputeTestData.Now));
        var transaction = payload.RootElement.GetProperty("TxInf");

        payload.RootElement.GetProperty("MsgDefIdr").GetString().ShouldStartWith("pacs.004");
        transaction.GetProperty("RtrId").GetString().ShouldBe(dispute.Reference);
        transaction.GetProperty("OrgnlTxId").GetString().ShouldBe("ACME20261101000001");
        transaction.GetProperty("OrgnlEndToEndId").GetString().ShouldBe("VOD-1-20261101");
        transaction.GetProperty("RtrdIntrBkSttlmAmt").GetProperty("Amt").GetString().ShouldBe("599.00");
        transaction.GetProperty("RtrdIntrBkSttlmAmt").GetProperty("Ccy").GetString().ShouldBe("ZAR");
        transaction.GetProperty("RtrRsnInf").GetProperty("Rsn").GetProperty("Cd").GetString().ShouldBe("AM09");
        transaction.GetProperty("OrgnlTxRef").GetProperty("MndtRltdInf").GetProperty("MndtId").GetString().ShouldBe("MRN000000001");
        transaction.GetProperty("OrgnlTxRef").GetProperty("Cdtr").GetProperty("Nm").GetString().ShouldBe("Vodacom");
    }
}

public class EftReturnMessageTests
{
    [Fact]
    public void An_eft_debit_order_dispute_has_no_mandate_and_its_pacs004_leaves_the_mandate_out()
    {
        var dispute = DisputeTestData.Submitted("not_authorised", DisputeTestData.Eft);

        using var payload = JsonDocument.Parse(ReturnMessageFactory.Create(dispute, DisputeTestData.Now));
        var original = payload.RootElement.GetProperty("TxInf").GetProperty("OrgnlTxRef");

        dispute.ReasonCode.ShouldBe("MD01");
        dispute.MandateType.ShouldBe("Eft");
        dispute.MandateReference.ShouldBeNull();
        original.TryGetProperty("MndtRltdInf", out _).ShouldBeFalse();
        original.GetProperty("Cdtr").GetProperty("Nm").GetString().ShouldBe("Vodacom");
    }
}

public class ChargebackRecordFactoryTests
{
    [Fact]
    public void Builds_a_chargeback_record_that_identifies_the_card_transaction_to_the_scheme()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.Card(), "not_authorised");

        using var payload = JsonDocument.Parse(ChargebackRecordFactory.Create(dispute, DisputeTestData.Now));
        var record = payload.RootElement;

        record.GetProperty("recordType").GetString().ShouldBe("Chargeback");
        record.GetProperty("chargebackReference").GetString().ShouldBe(dispute.Reference);
        record.GetProperty("scheme").GetString().ShouldBe("Visa");
        record.GetProperty("reasonCode").GetString().ShouldBe("10.4");
        record.GetProperty("acquirerReferenceNumber").GetString().ShouldBe("74027000000000000000001");
        record.GetProperty("amount").GetString().ShouldBe("1899.00");
        record.GetProperty("currency").GetString().ShouldBe("ZAR");
        record.GetProperty("merchantName").GetString().ShouldBe("Takealot");
        record.GetProperty("transactionDate").GetString().ShouldBe("2026-11-18");
        record.GetProperty("postingDate").GetString().ShouldBe("2026-11-19");
        record.GetProperty("createdAt").GetDateTimeOffset().ShouldBe(DisputeTestData.Now);
    }
}

public class DisputeReferenceTests
{
    [Fact]
    public void A_reference_carries_the_date_and_six_unambiguous_characters()
    {
        var reference = DisputeReference.New(DisputeTestData.Now);

        reference.ShouldMatch("^DSP-20261122-[A-HJ-NP-Z2-9]{6}$");
    }
}

public class OutboxMessageTests
{
    [Fact]
    public void A_failed_delivery_is_retried_later_with_exponential_backoff()
    {
        var message = new OutboxMessage(Guid.NewGuid(), "Test", "{}", DisputeTestData.Now);

        message.MarkFailed("boom", DisputeTestData.Now);
        message.NextAttemptAt.ShouldBe(DisputeTestData.Now.AddSeconds(10));

        message.MarkFailed("boom", DisputeTestData.Now);
        message.NextAttemptAt.ShouldBe(DisputeTestData.Now.AddSeconds(20));
        message.Attempts.ShouldBe(2);
        message.LastError.ShouldBe("boom");
    }

    [Fact]
    public void A_message_is_abandoned_after_the_attempt_limit()
    {
        var message = new OutboxMessage(Guid.NewGuid(), "Test", "{}", DisputeTestData.Now);

        for (var attempt = 0; attempt < OutboxMessage.MaxAttempts; attempt++)
        {
            message.IsAbandoned.ShouldBeFalse();
            message.MarkFailed("boom", DisputeTestData.Now);
        }

        message.IsAbandoned.ShouldBeTrue();
    }

    [Fact]
    public void A_delivered_message_is_marked_processed_and_its_error_cleared()
    {
        var message = new OutboxMessage(Guid.NewGuid(), "Test", "{}", DisputeTestData.Now);
        message.MarkFailed("boom", DisputeTestData.Now);

        message.MarkProcessed(DisputeTestData.Now.AddSeconds(10));

        message.ProcessedAt.ShouldBe(DisputeTestData.Now.AddSeconds(10));
        message.LastError.ShouldBeNull();
        message.IsAbandoned.ShouldBeFalse();
    }

    [Fact]
    public void A_very_long_error_is_truncated_to_fit_the_column()
    {
        var message = new OutboxMessage(Guid.NewGuid(), "Test", "{}", DisputeTestData.Now);

        message.MarkFailed(new string('x', 5000), DisputeTestData.Now);

        message.LastError!.Length.ShouldBe(2000);
    }
}
