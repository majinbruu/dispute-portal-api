using System.Text.Json;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Disputes.Domain;

namespace DisputePortal.UnitTests.Disputes;

public class DisputeWithdrawalTests
{
    [Fact]
    public void A_submitted_dispute_can_be_withdrawn_and_that_is_final()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.Withdraw(DisputeTestData.Now.AddMinutes(5));

        dispute.Status.ShouldBe(DisputeStatus.Withdrawn);
        dispute.CompletedAt.ShouldBe(DisputeTestData.Now.AddMinutes(5));
        dispute.IsCompleted.ShouldBeTrue();
        dispute.IsOpen.ShouldBeFalse();
        dispute.IsAwaitingReview.ShouldBeFalse();
        dispute.SchemePayload.ShouldBeNull();
        dispute.SentToSchemeAt.ShouldBeNull();
    }

    [Fact]
    public void A_dispute_that_was_sent_for_processing_cannot_be_withdrawn()
    {
        var dispute = DisputeTestData.SentToScheme();

        Should.Throw<DisputeRuleException>(() => dispute.Withdraw(DisputeTestData.Now));
        dispute.Status.ShouldBe(DisputeStatus.SentToScheme);
    }

    [Fact]
    public void A_finished_dispute_cannot_be_withdrawn()
    {
        var refunded = DisputeTestData.SentToScheme();
        refunded.MarkRefunded(DisputeTestData.Now);
        var rejected = DisputeTestData.Submitted();
        rejected.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);
        var withdrawn = DisputeTestData.Submitted();
        withdrawn.Withdraw(DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => refunded.Withdraw(DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => rejected.Withdraw(DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => withdrawn.Withdraw(DisputeTestData.Now));
    }

    [Fact]
    public void A_withdrawn_dispute_cannot_be_approved_rejected_or_completed()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.Withdraw(DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => dispute.MarkSentToScheme("{}", DisputeActor.Agent(DisputeTestData.Agent), DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => dispute.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => dispute.MarkRefunded(DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => dispute.MarkDeclined("No.", DisputeTestData.Now));
    }
}

public class DisputeReviewTests
{
    [Fact]
    public void Approval_sends_a_submitted_dispute_for_processing()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.MarkSentToScheme("{}", DisputeActor.Agent(DisputeTestData.Agent), DisputeTestData.Now.AddMinutes(1));

        dispute.Status.ShouldBe(DisputeStatus.SentToScheme);
        dispute.SentToSchemeAt.ShouldBe(DisputeTestData.Now.AddMinutes(1));
        dispute.IsAwaitingReview.ShouldBeFalse();
        dispute.IsOpen.ShouldBeTrue();
    }

    [Fact]
    public void A_customer_cannot_approve_a_dispute()
    {
        var dispute = DisputeTestData.Submitted();

        Should.Throw<DisputeRuleException>(() =>
            dispute.MarkSentToScheme("{}", DisputeActor.Customer(DisputeTestData.Customer), DisputeTestData.Now));
        dispute.Status.ShouldBe(DisputeStatus.Submitted);
    }

    [Fact]
    public void Rejection_declines_a_submitted_dispute_with_the_agents_reason_as_the_outcome()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.Reject(DisputeTestData.Agent, "  The mandate shows this amount was agreed.  ", DisputeTestData.Now.AddMinutes(1));

        dispute.Status.ShouldBe(DisputeStatus.Declined);
        dispute.OutcomeNote.ShouldBe("The mandate shows this amount was agreed.");
        dispute.CompletedAt.ShouldBe(DisputeTestData.Now.AddMinutes(1));
        dispute.IsCompleted.ShouldBeTrue();
        dispute.SchemePayload.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_rejection_needs_a_reason(string reason)
    {
        var dispute = DisputeTestData.Submitted();

        Should.Throw<DisputeRuleException>(() => dispute.Reject(DisputeTestData.Agent, reason, DisputeTestData.Now));
        dispute.Status.ShouldBe(DisputeStatus.Submitted);
    }

    [Fact]
    public void A_rejection_reason_is_at_most_500_characters()
    {
        var atLimit = DisputeTestData.Submitted();
        var overLimit = DisputeTestData.Submitted();

        atLimit.Reject(DisputeTestData.Agent, new string('x', 500), DisputeTestData.Now);

        atLimit.Status.ShouldBe(DisputeStatus.Declined);
        Should.Throw<DisputeRuleException>(() => overLimit.Reject(DisputeTestData.Agent, new string('x', 501), DisputeTestData.Now));
    }

    [Fact]
    public void Only_a_submitted_dispute_can_be_rejected()
    {
        var sent = DisputeTestData.SentToScheme();
        var rejected = DisputeTestData.Submitted();
        rejected.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => sent.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => rejected.Reject(DisputeTestData.Agent, "Again.", DisputeTestData.Now));
    }

    [Fact]
    public void A_dispute_an_agent_rejected_is_never_sent_to_the_scheme()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);

        Should.Throw<DisputeRuleException>(() => dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now));
    }
}

public class DisputeEventActorTests
{
    [Fact]
    public void Raising_a_dispute_is_recorded_against_the_customer()
    {
        var dispute = DisputeTestData.Submitted();

        var raised = dispute.Events.Single();
        raised.ActorType.ShouldBe(DisputeActorType.Customer);
        raised.ActorSubjectId.ShouldBe(DisputeTestData.Customer);
    }

    [Fact]
    public void An_agents_approval_is_recorded_against_that_agent()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.MarkSentToScheme("{}", DisputeActor.Agent(DisputeTestData.Agent), DisputeTestData.Now);

        var approved = dispute.Events.Last();
        approved.Status.ShouldBe(DisputeStatus.SentToScheme);
        approved.ActorType.ShouldBe(DisputeActorType.Agent);
        approved.ActorSubjectId.ShouldBe(DisputeTestData.Agent);
    }

    [Fact]
    public void An_automatic_approval_is_recorded_against_the_system_with_no_subject()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.MarkSentToScheme("{}", DisputeActor.System, DisputeTestData.Now);

        var approved = dispute.Events.Last();
        approved.ActorType.ShouldBe(DisputeActorType.System);
        approved.ActorSubjectId.ShouldBeNull();
    }

    [Fact]
    public void An_agents_rejection_is_recorded_against_that_agent()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);

        var rejected = dispute.Events.Last();
        rejected.Status.ShouldBe(DisputeStatus.Declined);
        rejected.Description.ShouldBe("Not valid.");
        rejected.ActorType.ShouldBe(DisputeActorType.Agent);
        rejected.ActorSubjectId.ShouldBe(DisputeTestData.Agent);
    }

    [Fact]
    public void A_withdrawal_is_recorded_against_the_customer()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.Withdraw(DisputeTestData.Now);

        var withdrawn = dispute.Events.Last();
        withdrawn.Status.ShouldBe(DisputeStatus.Withdrawn);
        withdrawn.ActorType.ShouldBe(DisputeActorType.Customer);
        withdrawn.ActorSubjectId.ShouldBe(DisputeTestData.Customer);
    }

    [Fact]
    public void The_schemes_answer_is_recorded_against_the_system()
    {
        var refunded = DisputeTestData.SentToScheme();
        var declined = DisputeTestData.SentToScheme();

        refunded.MarkRefunded(DisputeTestData.Now);
        declined.MarkDeclined("Already reversed.", DisputeTestData.Now);

        refunded.Events.Last().ActorType.ShouldBe(DisputeActorType.System);
        refunded.Events.Last().ActorSubjectId.ShouldBeNull();
        declined.Events.Last().ActorType.ShouldBe(DisputeActorType.System);
        declined.Events.Last().ActorSubjectId.ShouldBeNull();
    }

    [Fact]
    public void An_actor_that_is_not_the_system_always_has_a_subject()
    {
        var dispute = DisputeTestData.Submitted();

        // default(DisputeActor) is a customer with no subject, which must never reach the audit trail.
        Should.Throw<DisputeRuleException>(() => dispute.MarkSentToScheme("{}", default, DisputeTestData.Now));
        Should.Throw<DisputeRuleException>(() => dispute.AddComment(default, "Hello", DisputeTestData.Now));
    }
}

public class DisputeCommentTests
{
    private static readonly DisputeActor Customer = DisputeActor.Customer(DisputeTestData.Customer);
    private static readonly DisputeActor Agent = DisputeActor.Agent(DisputeTestData.Agent);

    [Fact]
    public void A_dispute_raised_without_a_comment_has_an_empty_thread()
    {
        DisputeTestData.Submitted().Comments.ShouldBeEmpty();
    }

    [Fact]
    public void A_comment_given_when_raising_the_dispute_is_the_first_comment_in_the_thread()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.DebitOrder(), comment: "  I cancelled this in March.  ");

        var comment = dispute.Comments.Single();
        comment.Body.ShouldBe("I cancelled this in March.");
        comment.AuthorType.ShouldBe(DisputeActorType.Customer);
        comment.AuthorSubjectId.ShouldBe(DisputeTestData.Customer);
        comment.CreatedAt.ShouldBe(DisputeTestData.Now);
        comment.DisputeId.ShouldBe(dispute.Id);
    }

    [Fact]
    public void The_customer_and_an_agent_can_both_comment_while_the_dispute_waits_for_review()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.AddComment(Agent, "Which amount did you agree to?", DisputeTestData.Now.AddMinutes(1));
        dispute.AddComment(Customer, "R599 a month.", DisputeTestData.Now.AddMinutes(2));

        dispute.Comments.Select(c => (c.AuthorType, c.Body)).ShouldBe(
        [
            (DisputeActorType.Agent, "Which amount did you agree to?"),
            (DisputeActorType.Customer, "R599 a month."),
        ]);
        dispute.Comments.First().AuthorSubjectId.ShouldBe(DisputeTestData.Agent);
    }

    [Fact]
    public void Comments_can_still_be_added_while_the_dispute_is_being_processed()
    {
        var dispute = DisputeTestData.SentToScheme();

        dispute.AddComment(Customer, "Any news?", DisputeTestData.Now);
        dispute.AddComment(Agent, "We are waiting for the other bank.", DisputeTestData.Now);

        dispute.Comments.Count.ShouldBe(2);
    }

    [Fact]
    public void Nobody_can_comment_on_a_finished_dispute()
    {
        var refunded = DisputeTestData.SentToScheme();
        refunded.MarkRefunded(DisputeTestData.Now);
        var declined = DisputeTestData.Submitted();
        declined.Reject(DisputeTestData.Agent, "Not valid.", DisputeTestData.Now);
        var withdrawn = DisputeTestData.Submitted();
        withdrawn.Withdraw(DisputeTestData.Now);

        foreach (var dispute in new[] { refunded, declined, withdrawn })
        {
            Should.Throw<DisputeRuleException>(() => dispute.AddComment(Customer, "Hello", DisputeTestData.Now));
            Should.Throw<DisputeRuleException>(() => dispute.AddComment(Agent, "Hello", DisputeTestData.Now));
            dispute.Comments.ShouldBeEmpty();
        }
    }

    [Fact]
    public void Another_customer_cannot_comment_as_the_customer()
    {
        var dispute = DisputeTestData.Submitted();

        Should.Throw<DisputeRuleException>(() => dispute.AddComment(DisputeActor.Customer(Guid.NewGuid()), "Hello", DisputeTestData.Now));
    }

    [Fact]
    public void The_system_does_not_write_comments()
    {
        var dispute = DisputeTestData.Submitted();

        Should.Throw<DisputeRuleException>(() => dispute.AddComment(DisputeActor.System, "Hello", DisputeTestData.Now));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\r\n\t")]
    public void A_comment_cannot_be_empty(string body)
    {
        var dispute = DisputeTestData.Submitted();

        Should.Throw<DisputeRuleException>(() => dispute.AddComment(Customer, body, DisputeTestData.Now));
    }

    [Fact]
    public void A_comment_is_trimmed_and_is_at_most_1000_characters()
    {
        var dispute = DisputeTestData.Submitted();

        var atLimit = dispute.AddComment(Customer, $"  {new string('x', 1000)}  ", DisputeTestData.Now);

        atLimit.Body.Length.ShouldBe(1000);
        Should.Throw<DisputeRuleException>(() => dispute.AddComment(Customer, new string('x', 1001), DisputeTestData.Now));
    }

    [Fact]
    public void An_agents_comment_raises_an_event_for_the_outbox_and_a_customers_comment_does_not()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.DebitOrder(), comment: "At submission.");
        dispute.ClearDomainEvents();

        dispute.AddComment(Customer, "From the customer.", DisputeTestData.Now);
        dispute.DomainEvents.ShouldBeEmpty();

        var comment = dispute.AddComment(Agent, "From the agent.", DisputeTestData.Now.AddMinutes(1));
        dispute.DomainEvents.ShouldBe([new AgentCommentAddedDomainEvent(comment.Id, DisputeTestData.Now.AddMinutes(1))]);
    }

    [Fact]
    public void A_comment_does_not_change_the_status_or_the_timeline()
    {
        var dispute = DisputeTestData.Submitted();

        dispute.AddComment(Agent, "Hello", DisputeTestData.Now);

        dispute.Status.ShouldBe(DisputeStatus.Submitted);
        dispute.Events.Count.ShouldBe(1);
    }
}

public class OutboxMessageFactoryTests
{
    [Fact]
    public void A_status_change_becomes_a_status_changed_message_that_describes_the_dispute()
    {
        var dispute = DisputeTestData.Submitted(DisputeTestData.Card(), "duplicate");

        var message = OutboxMessageFactory.Create(dispute, dispute.DomainEvents.Single());
        var payload = JsonSerializer.Deserialize<DisputeStatusChanged>(message.Payload)!;

        message.Type.ShouldBe(nameof(DisputeStatusChanged));
        message.OccurredAt.ShouldBe(DisputeTestData.Now);
        payload.DisputeId.ShouldBe(dispute.Id);
        payload.CustomerId.ShouldBe(DisputeTestData.Customer);
        payload.Reference.ShouldBe(dispute.Reference);
        payload.Status.ShouldBe("Submitted");
        payload.TransactionType.ShouldBe(DisputedTransactionTypes.Card);
        payload.CounterpartyName.ShouldBe("Takealot");
        payload.OutcomeNote.ShouldBeNull();
    }

    [Fact]
    public void A_rejection_carries_the_agents_reason_to_the_customer()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.Reject(DisputeTestData.Agent, "The mandate shows this amount was agreed.", DisputeTestData.Now);

        var message = OutboxMessageFactory.Create(dispute, dispute.DomainEvents.Last());
        var payload = JsonSerializer.Deserialize<DisputeStatusChanged>(message.Payload)!;

        payload.Status.ShouldBe("Declined");
        payload.OutcomeNote.ShouldBe("The mandate shows this amount was agreed.");
    }

    [Fact]
    public void A_withdrawal_is_published_as_a_status_change()
    {
        var dispute = DisputeTestData.Submitted();
        dispute.Withdraw(DisputeTestData.Now);

        var message = OutboxMessageFactory.Create(dispute, dispute.DomainEvents.Last());

        JsonSerializer.Deserialize<DisputeStatusChanged>(message.Payload)!.Status.ShouldBe("Withdrawn");
    }

    [Fact]
    public void An_agent_comment_becomes_its_own_message_type_without_the_comment_text_or_the_agent()
    {
        var dispute = DisputeTestData.Submitted();
        var comment = dispute.AddComment(DisputeActor.Agent(DisputeTestData.Agent), "Please send proof of cancellation.", DisputeTestData.Now.AddMinutes(1));

        var message = OutboxMessageFactory.Create(dispute, dispute.DomainEvents.Last());
        var payload = JsonSerializer.Deserialize<AgentCommentAdded>(message.Payload)!;

        message.Type.ShouldBe(nameof(AgentCommentAdded));
        message.OccurredAt.ShouldBe(DisputeTestData.Now.AddMinutes(1));
        payload.ShouldBe(new AgentCommentAdded(dispute.Id, DisputeTestData.Customer, dispute.Reference, comment.Id, DisputeTestData.Now.AddMinutes(1)));
        message.Payload.ShouldNotContain("proof of cancellation");
        message.Payload.ShouldNotContain(DisputeTestData.Agent.ToString());
    }

    [Fact]
    public void Each_message_gets_its_own_id_which_is_what_makes_consumers_idempotent()
    {
        var dispute = DisputeTestData.Submitted();

        var first = OutboxMessageFactory.Create(dispute, dispute.DomainEvents.Single());
        var second = OutboxMessageFactory.Create(dispute, dispute.DomainEvents.Single());

        first.Id.ShouldNotBe(second.Id);
    }
}
