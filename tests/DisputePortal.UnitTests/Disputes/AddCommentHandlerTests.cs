using System.Text.Json;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.Disputes;

public class AddCommentHandlerTests
{
    private static readonly Guid Customer = DisputeTestData.Customer;
    private static readonly Guid Agent = DisputeTestData.Agent;
    private static readonly DateTimeOffset CommentedAt = DisputeTestData.Now.AddHours(2);

    private readonly InMemoryDisputeRepository _disputes = new();
    private readonly FakeTimeProvider _clock = new(CommentedAt);

    private AddCommentHandler Handler => new(_disputes, _clock);

    [Theory]
    [InlineData("Submitted")]
    [InlineData("SentToScheme")]
    public async Task The_customer_comments_on_their_own_open_dispute(string statusName)
    {
        var dispute = Add(WithdrawDisputeHandlerTests.InStatus(Enum.Parse<DisputeStatus>(statusName)));

        var result = await Handler.ByCustomerAsync(Customer, dispute.Id, "  Any news on this?  ", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var comment = result.Value.Comments.Single();
        comment.Body.ShouldBe("Any news on this?");
        comment.AuthorType.ShouldBe(DisputeActorType.Customer);
        comment.AuthorSubjectId.ShouldBe(Customer);
        comment.CreatedAt.ShouldBe(CommentedAt);
        _disputes.Saves.ShouldBe(1);
    }

    [Theory]
    [InlineData("Submitted")]
    [InlineData("SentToScheme")]
    public async Task An_agent_comments_on_an_open_dispute_and_is_recorded_as_the_author(string statusName)
    {
        var dispute = Add(WithdrawDisputeHandlerTests.InStatus(Enum.Parse<DisputeStatus>(statusName)));

        var result = await Handler.ByAgentAsync(Agent, dispute.Id, "Please send proof of cancellation.", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        var comment = result.Value.Comments.Single();
        comment.AuthorType.ShouldBe(DisputeActorType.Agent);
        comment.AuthorSubjectId.ShouldBe(Agent);
        _disputes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task An_agents_comment_is_published_for_the_customers_inbox_in_the_same_save()
    {
        var dispute = Add(DisputeTestData.Submitted());
        dispute.ClearDomainEvents();

        var result = await Handler.ByAgentAsync(Agent, dispute.Id, "Please send proof of cancellation.", CancellationToken.None);

        // The DbContext turns pending domain events into outbox rows when it saves, and the handler saves once.
        var commentId = result.Value.Comments.Single().Id;
        dispute.DomainEvents.ShouldBe([new AgentCommentAddedDomainEvent(commentId, CommentedAt)]);
        _disputes.Saves.ShouldBe(1);
    }

    [Fact]
    public async Task A_customers_comment_publishes_nothing()
    {
        var dispute = Add(DisputeTestData.Submitted());
        dispute.ClearDomainEvents();

        await Handler.ByCustomerAsync(Customer, dispute.Id, "Any news?", CancellationToken.None);

        dispute.DomainEvents.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_customer_cannot_comment_on_another_customers_dispute()
    {
        var dispute = Add(DisputeTestData.Submitted());

        var result = await Handler.ByCustomerAsync(Guid.NewGuid(), dispute.Id, "Hello", CancellationToken.None);

        result.Error!.Type.ShouldBe(ErrorType.NotFound);
        result.Error.Code.ShouldBe("dispute_not_found");
        dispute.Comments.ShouldBeEmpty();
        _disputes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task An_agent_comments_on_the_dispute_of_any_customer()
    {
        var dispute = Add(Dispute.Submit(
            Guid.NewGuid(), "DSP-20261122-XYZ789", Guid.NewGuid(), DisputeTestData.DebitOrder(), "duplicate", comment: null, DisputeTestData.Now));

        var result = await Handler.ByAgentAsync(Agent, dispute.Id, "Hello", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task A_dispute_that_does_not_exist_is_not_found()
    {
        var byCustomer = await Handler.ByCustomerAsync(Customer, Guid.NewGuid(), "Hello", CancellationToken.None);
        var byAgent = await Handler.ByAgentAsync(Agent, Guid.NewGuid(), "Hello", CancellationToken.None);

        byCustomer.Error!.Code.ShouldBe("dispute_not_found");
        byAgent.Error!.Code.ShouldBe("dispute_not_found");
    }

    [Theory]
    [InlineData("Refunded")]
    [InlineData("Declined")]
    [InlineData("Withdrawn")]
    public async Task Nobody_can_comment_on_a_closed_dispute(string statusName)
    {
        var dispute = Add(WithdrawDisputeHandlerTests.InStatus(Enum.Parse<DisputeStatus>(statusName)));

        var byCustomer = await Handler.ByCustomerAsync(Customer, dispute.Id, "Hello", CancellationToken.None);
        var byAgent = await Handler.ByAgentAsync(Agent, dispute.Id, "Hello", CancellationToken.None);

        byCustomer.Error!.Type.ShouldBe(ErrorType.Conflict);
        byCustomer.Error.Code.ShouldBe("dispute_closed");
        byAgent.Error!.Type.ShouldBe(ErrorType.Conflict);
        byAgent.Error.Code.ShouldBe("dispute_closed");
        dispute.Comments.ShouldBeEmpty();
        _disputes.Saves.ShouldBe(0);
    }

    [Fact]
    public async Task The_customer_sees_the_thread_in_order_with_author_types_but_never_the_agents_identity()
    {
        var dispute = Add(DisputeTestData.Submitted(DisputeTestData.DebitOrder(), comment: "I cancelled this in March."));
        await Handler.ByAgentAsync(Agent, dispute.Id, "Please send proof of cancellation.", CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(10));
        await Handler.ByCustomerAsync(Customer, dispute.Id, "I emailed it to you.", CancellationToken.None);
        var queries = new DisputeQueries(_disputes, new FakeTransactionDirectory());

        var detail = (await queries.GetAsync(Customer, dispute.Id, CancellationToken.None)).Value;

        detail.Comments.Select(c => (c.AuthorType, c.Body, c.CreatedAt)).ShouldBe(
        [
            (DisputeActorType.Customer, "I cancelled this in March.", DisputeTestData.Now),
            (DisputeActorType.Agent, "Please send proof of cancellation.", CommentedAt),
            (DisputeActorType.Customer, "I emailed it to you.", CommentedAt.AddMinutes(10)),
        ]);
        JsonSerializer.Serialize(detail).ShouldNotContain(Agent.ToString());
    }

    [Fact]
    public async Task An_agent_sees_the_thread_with_who_wrote_each_comment()
    {
        var dispute = Add(DisputeTestData.Submitted(DisputeTestData.DebitOrder(), comment: "I cancelled this in March."));
        await Handler.ByAgentAsync(Agent, dispute.Id, "Please send proof of cancellation.", CancellationToken.None);
        var queries = new AgentDisputeQueries(_disputes);

        var detail = (await queries.GetAsync(dispute.Id, CancellationToken.None)).Value;

        detail.Comments.Select(c => (c.AuthorType, c.AuthorSubjectId)).ShouldBe(
        [
            (DisputeActorType.Customer, Customer),
            (DisputeActorType.Agent, Agent),
        ]);
    }

    private Dispute Add(Dispute dispute)
    {
        _disputes.Disputes.Add(dispute);
        return dispute;
    }
}
