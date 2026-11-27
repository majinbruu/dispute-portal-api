using System.Net;
using DisputePortal.IntegrationTests.Infrastructure;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Contracts;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Transactions.Application;
using DisputePortal.Modules.Transactions.Domain;
using DisputePortal.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Withdrawal against the real database: the filtered unique index that lets a withdrawn transaction be disputed
/// again, and the row version that lets only one of a withdrawal and an agent's decision through.
/// </summary>
[Collection(PortalCollection.Name)]
public class WithdrawalTests(PortalFixture portal) : PortalTest(portal)
{
    private const int Rounds = 15;

    private static readonly Guid Transaction = DemoData.RefundedDebitOrderId;

    [Fact]
    public async Task A_transaction_whose_dispute_was_withdrawn_can_be_disputed_again()
    {
        var customer = Portal.Thandi();
        var first = await (await customer.SubmitDisputeAsync(Transaction, "not_authorised")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);

        var withdrawn = await (await customer.PostAsync($"/api/v1/disputes/{first.Id}/withdraw")).ReadAsync<DisputeDetailDto>();
        withdrawn.Status.ShouldBe(DisputeStatus.Withdrawn);

        // With no open dispute the transaction is offered for dispute again.
        var transaction = await customer.GetAsync<TransactionDto>($"/api/v1/transactions/{Transaction}");
        transaction.Eligibility.CanDispute.ShouldBeTrue();
        transaction.Dispute.ShouldBeNull();

        var second = await (await customer.SubmitDisputeAsync(Transaction, "duplicate")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);

        second.Id.ShouldNotBe(first.Id);
        var stored = await Portal.WithDisputesAsync(db => db.Disputes.Where(d => d.TransactionId == Transaction).ToListAsync());
        stored.Select(d => d.Status).ShouldBe([DisputeStatus.Withdrawn, DisputeStatus.Submitted], ignoreOrder: true);

        transaction = await customer.GetAsync<TransactionDto>($"/api/v1/transactions/{Transaction}");
        transaction.Dispute!.Id.ShouldBe(second.Id);
        transaction.Eligibility.Code.ShouldBe(DisputeEligibility.AlreadyDisputed);
    }

    [Fact]
    public async Task Only_one_open_dispute_is_allowed_after_a_withdrawal()
    {
        var customer = Portal.Thandi();
        var first = await (await customer.SubmitDisputeAsync(Transaction, "not_authorised")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        (await customer.PostAsync($"/api/v1/disputes/{first.Id}/withdraw")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await customer.SubmitDisputeAsync(Transaction, "duplicate")).StatusCode.ShouldBe(HttpStatusCode.Created);

        var third = await customer.SubmitDisputeAsync(Transaction, "cancelled");

        await third.ShouldBeProblemAsync(HttpStatusCode.Conflict, "already_disputed");
    }

    [Fact]
    public async Task A_dispute_can_be_withdrawn_and_raised_again_more_than_once()
    {
        var customer = Portal.Thandi();

        for (var attempt = 0; attempt < 3; attempt++)
        {
            var dispute = await (await customer.SubmitDisputeAsync(Transaction, "not_authorised")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
            (await customer.PostAsync($"/api/v1/disputes/{dispute.Id}/withdraw")).StatusCode.ShouldBe(HttpStatusCode.OK);
        }

        (await Portal.WithDisputesAsync(db => db.Disputes.CountAsync(d => d.TransactionId == Transaction && d.Status == DisputeStatus.Withdrawn))).ShouldBe(3);
    }

    [Fact]
    public async Task A_dispute_an_agent_already_approved_cannot_be_withdrawn()
    {
        var customer = Portal.Thandi();
        var dispute = await (await customer.SubmitDisputeAsync(Transaction, "not_authorised")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
        (await Portal.Agent().PostAsync($"/api/v1/agent/disputes/{dispute.Id}/approve")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var response = await customer.PostAsync($"/api/v1/disputes/{dispute.Id}/withdraw");

        await response.ShouldBeProblemAsync(HttpStatusCode.Conflict, "dispute_not_withdrawable");
    }

    [Fact]
    public async Task A_withdrawal_racing_an_agents_approval_has_exactly_one_winner()
    {
        var customer = Portal.Thandi();
        var agent = Portal.Agent();

        // The race is run several times, because which request the database sees first differs from run to run.
        for (var round = 0; round < Rounds; round++)
        {
            var dispute = (await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now)))[0];

            var requests = await Task.WhenAll(
                Task.Run(() => customer.PostAsync($"/api/v1/disputes/{dispute.Id}/withdraw")),
                Task.Run(() => agent.PostAsync($"/api/v1/agent/disputes/{dispute.Id}/approve")));
            var (withdraw, approve) = (requests[0], requests[1]);

            var stored = await Portal.WithDisputesAsync(db => db.Disputes.Include(d => d.Events).SingleAsync(d => d.Id == dispute.Id));
            if (withdraw.StatusCode == HttpStatusCode.OK)
            {
                await approve.ShouldBeProblemAsync(HttpStatusCode.Conflict, "dispute_not_awaiting_review");
                stored.Status.ShouldBe(DisputeStatus.Withdrawn);
                stored.SchemePayload.ShouldBeNull();
            }
            else
            {
                await withdraw.ShouldBeProblemAsync(HttpStatusCode.Conflict, "dispute_not_withdrawable");
                approve.StatusCode.ShouldBe(HttpStatusCode.OK);
                stored.Status.ShouldBe(DisputeStatus.SentToScheme);
            }

            // The loser left nothing behind: one event and one outbox message after the submission.
            stored.Events.Count.ShouldBe(2);
            var messages = await Portal.WithDisputesAsync(db => db.OutboxMessages.Where(m => m.Type == nameof(DisputeStatusChanged)).ToListAsync());
            messages.Count(m => m.Payload.Contains(dispute.Id.ToString(), StringComparison.Ordinal)).ShouldBe(2);
        }

        (Metrics.Count(PortalMetrics.DisputesWithdrawn) + Metrics.Count(PortalMetrics.DisputesReviewed)).ShouldBe(Rounds);
    }

    [Fact]
    public async Task A_decision_made_on_a_stale_copy_of_a_dispute_is_not_saved()
    {
        // The race above, taken apart so that both sides are certain to have read the dispute before either saves.
        var dispute = (await Portal.AddDisputesAsync(TestDisputes.Submitted(Clock.Now)))[0];
        await using var customerScope = Portal.App.Services.CreateAsyncScope();
        await using var agentScope = Portal.App.Services.CreateAsyncScope();
        var customerSide = customerScope.ServiceProvider.GetRequiredService<IDisputeRepository>();
        var agentSide = agentScope.ServiceProvider.GetRequiredService<IDisputeRepository>();

        var readByCustomer = await customerSide.FindForChangeAsync(dispute.Id, dispute.CustomerId, CancellationToken.None);
        var readByAgent = await agentSide.FindForChangeByAgentAsync(dispute.Id, CancellationToken.None);

        readByCustomer!.Withdraw(Clock.Now);
        (await customerSide.TrySaveAsync(CancellationToken.None)).ShouldBeTrue();

        readByAgent!.Reject(DemoData.Zanele, "The collection matches the mandate.", Clock.Now);
        (await agentSide.TrySaveAsync(CancellationToken.None)).ShouldBeFalse();

        var stored = await Portal.WithDisputesAsync(db => db.Disputes.Include(d => d.Events).SingleAsync(d => d.Id == dispute.Id));
        stored.Status.ShouldBe(DisputeStatus.Withdrawn);
        stored.OutcomeNote.ShouldBeNull();
        stored.Events.Select(e => e.Status).ShouldBe([DisputeStatus.Submitted, DisputeStatus.Withdrawn], ignoreOrder: true);
    }
}
