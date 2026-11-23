using System.Text.Json;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.UnitTests.Disputes;

internal static class DisputeTestData
{
    public static readonly Guid Customer = Guid.Parse("00000000-0000-4000-8000-0000000000aa");
    public static readonly DateTimeOffset Now = new(2026, 11, 22, 8, 0, 0, TimeSpan.Zero);

    public static DisputedDebitOrder DebitOrder(string mandateType = DisputeReason.Registered, string transactionId = "ACME20261101000001") => new(
        Guid.NewGuid(), transactionId, "VOD-1-20261101", Money.Zar(599m), new DateOnly(2026, 11, 1),
        mandateType, "MRN000000001", "Vodacom", "******7890");

    public static Dispute Submitted(string reasonCode = "MD01", string mandateType = DisputeReason.Registered, string transactionId = "ACME20261101000001") =>
        Dispute.Submit(Guid.NewGuid(), "DSP-20261122-ABC234", Customer, DebitOrder(mandateType, transactionId), DisputeReasons.Find(reasonCode)!, Now);
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
    public void A_dispute_moves_from_submitted_to_sent_to_refunded()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.MarkSentToScheme("{}", DisputeTestData.Now.AddSeconds(10));
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
        dispute.MarkSentToScheme("{}", DisputeTestData.Now);

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
        dispute.MarkSentToScheme("{}", DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => dispute.MarkSentToScheme("{}", DisputeTestData.Now));
    }

    [Fact]
    public void A_completed_dispute_cannot_change_again()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.MarkSentToScheme("{}", DisputeTestData.Now);
        dispute.MarkRefunded(DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => dispute.MarkDeclined("No.", DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => dispute.MarkRefunded(DisputeTestData.Now));
    }

    [Fact]
    public void Not_authorised_cannot_be_used_against_a_debicheck_debit_order()
    {
        Should.Throw<DisputeRuleException>(() => DisputeTestData.Submitted("MD01", DisputeReason.DebiCheck));
    }

    [Fact]
    public void Every_status_change_raises_a_domain_event_for_the_outbox()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.MarkSentToScheme("{}", DisputeTestData.Now);
        dispute.MarkRefunded(DisputeTestData.Now);

        dispute.DomainEvents.Select(e => e.Status).ShouldBe([DisputeStatus.Submitted, DisputeStatus.SentToScheme, DisputeStatus.Refunded]);

        dispute.ClearDomainEvents();
        dispute.DomainEvents.ShouldBeEmpty();
    }
}

public class DisputeReasonTests
{
    [Fact]
    public void A_registered_debit_order_allows_all_five_reasons()
    {
        DisputeReasons.AllowedFor(DisputeReason.Registered).Select(r => r.Code).ShouldBe(["MD01", "AM09", "DT01", "MD06", "AM05"]);
    }

    [Fact]
    public void A_debicheck_debit_order_does_not_allow_not_authorised()
    {
        DisputeReasons.AllowedFor(DisputeReason.DebiCheck).Select(r => r.Code).ShouldBe(["AM09", "DT01", "MD06", "AM05"]);
    }

    [Fact]
    public void An_unknown_reason_code_is_not_found()
    {
        DisputeReasons.Find("XX99").ShouldBeNull();
        DisputeReasons.Find(null).ShouldBeNull();
    }

    [Fact]
    public void The_reasons_endpoint_filters_by_mandate_type()
    {
        DisputeQueries.ReasonsFor(null).Count.ShouldBe(5);
        DisputeQueries.ReasonsFor(MandateKind.DebiCheck).Select(r => r.Code).ShouldNotContain("MD01");
    }
}

public class ReturnMessageFactoryTests
{
    [Fact]
    public void Builds_a_pacs004_payload_that_references_the_original_collection()
    {
        var dispute = DisputeTestData.Submitted("AM09");

        using var payload = JsonDocument.Parse(ReturnMessageFactory.Create(dispute, DisputeTestData.Now));
        var transaction = payload.RootElement.GetProperty("TxInf");

        payload.RootElement.GetProperty("MsgDefIdr").GetString().ShouldStartWith("pacs.004");
        transaction.GetProperty("RtrId").GetString().ShouldBe(dispute.Reference);
        transaction.GetProperty("OrgnlTxId").GetString().ShouldBe("ACME20261101000001");
        transaction.GetProperty("RtrdIntrBkSttlmAmt").GetProperty("Amt").GetString().ShouldBe("599.00");
        transaction.GetProperty("RtrdIntrBkSttlmAmt").GetProperty("Ccy").GetString().ShouldBe("ZAR");
        transaction.GetProperty("RtrRsnInf").GetProperty("Rsn").GetProperty("Cd").GetString().ShouldBe("AM09");
        transaction.GetProperty("OrgnlTxRef").GetProperty("MndtRltdInf").GetProperty("MndtId").GetString().ShouldBe("MRN000000001");
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
