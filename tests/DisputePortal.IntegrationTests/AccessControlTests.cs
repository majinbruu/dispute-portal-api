using System.Net;
using DisputePortal.IntegrationTests.Infrastructure;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.Modules.Notifications.Application;
using DisputePortal.Modules.Transactions.Application;
using DisputePortal.SharedKernel;
using DisputePortal.SharedKernel.Web;
using Microsoft.EntityFrameworkCore;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// Who may call what. Tokens are issued by the test identity provider and validated by the application's
/// own JWT configuration: signature, issuer, audience, lifetime, the roles claim and the subject.
/// </summary>
[Collection(PortalCollection.Name)]
public class AccessControlTests(PortalFixture portal) : PortalTest(portal)
{
    private const string Transactions = "/api/v1/transactions?type=Card";
    private const string AgentQueue = "/api/v1/agent/disputes";

    private static readonly string[] Customer = [ApiConventions.CustomerRole];

    public static TheoryData<string> ProtectedEndpoints => new()
    {
        Transactions,
        "/api/v1/mandates",
        "/api/v1/disputes",
        "/api/v1/notifications",
        AgentQueue,
    };

    public static TheoryData<string, string> CustomerEndpoints => new()
    {
        { "GET", Transactions },
        { "GET", $"/api/v1/transactions/{DemoData.PendingCardId}" },
        { "GET", "/api/v1/mandates" },
        { "GET", "/api/v1/disputes" },
        { "POST", "/api/v1/disputes" },
        { "GET", $"/api/v1/dispute-reasons?transactionId={DemoData.PendingCardId}" },
        { "GET", "/api/v1/notifications" },
        { "GET", "/api/v1/notifications/unread-count" },
    };

    public static TheoryData<string, string> AgentEndpoints => new()
    {
        { "GET", AgentQueue },
        { "GET", $"{AgentQueue}/{Guid.Empty}" },
        { "POST", $"{AgentQueue}/{Guid.Empty}/approve" },
        { "POST", $"{AgentQueue}/{Guid.Empty}/reject" },
        { "POST", $"{AgentQueue}/{Guid.Empty}/comments" },
    };

    [Theory]
    [MemberData(nameof(ProtectedEndpoints))]
    public async Task A_request_without_a_token_is_refused_with_401(string path)
    {
        var response = await Portal.Anonymous().GetAsync(path);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_valid_customer_token_is_accepted()
    {
        var response = await Portal.Thandi().GetAsync(Transactions);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_token_for_another_audience_is_refused_with_401()
    {
        var token = Portal.Identity.IssueToken(DemoData.Thandi.ToString(), Customer, audience: "some-other-api");

        var response = await Portal.ClientWithToken(token).GetAsync(Transactions);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_from_another_issuer_is_refused_with_401()
    {
        var token = Portal.Identity.IssueToken(DemoData.Thandi.ToString(), Customer, issuer: "https://identity.test/realms/another-bank");

        var response = await Portal.ClientWithToken(token).GetAsync(Transactions);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task An_expired_token_is_refused_with_401()
    {
        // Expired well beyond the 30 seconds of clock skew the API allows.
        var token = Portal.Identity.IssueToken(DemoData.Thandi.ToString(), Customer, expiresIn: TimeSpan.FromMinutes(-5));

        var response = await Portal.ClientWithToken(token).GetAsync(Transactions);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_signed_with_a_key_the_identity_provider_does_not_publish_is_refused_with_401()
    {
        var token = Portal.Identity.IssueToken(DemoData.Thandi.ToString(), Customer, signedByStranger: true);

        var response = await Portal.ClientWithToken(token).GetAsync(Transactions);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_token_without_a_role_is_refused_with_403()
    {
        var response = await Portal.ClientFor(DemoData.Thandi).GetAsync(Transactions);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task A_token_whose_subject_is_not_a_GUID_is_refused_with_403()
    {
        // The subject is the customer ID. A service account, for example, has none that the portal can use.
        var token = Portal.Identity.IssueToken("service-account-reporting", Customer);

        var response = await Portal.ClientWithToken(token).GetAsync(Transactions);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AgentEndpoints))]
    public async Task A_customer_is_refused_on_the_agent_endpoints_with_403(string method, string path)
    {
        var response = await Portal.Thandi().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(CustomerEndpoints))]
    public async Task An_agent_is_refused_on_the_customer_endpoints_with_403(string method, string path)
    {
        var response = await Portal.Agent().SendAsync(new HttpRequestMessage(new HttpMethod(method), path));

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task An_agent_token_is_accepted_on_the_agent_endpoints()
    {
        var response = await Portal.Agent().GetAsync(AgentQueue);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_customer_cannot_read_another_customers_transaction()
    {
        var response = await Portal.Thandi().GetAsync($"/api/v1/transactions/{DemoData.SiphoPendingCardId}");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "transaction_not_found");
    }

    [Fact]
    public async Task A_customers_transaction_list_holds_only_their_own_transactions()
    {
        var thandi = await Portal.Thandi().GetAsync<PagedResult<TransactionDto>>($"{Transactions}&pageSize=100");
        var sipho = await Portal.Sipho().GetAsync<PagedResult<TransactionDto>>($"{Transactions}&pageSize=100");

        var owners = await Portal.WithTransactionsAsync(db => db.Transactions.ToDictionaryAsync(t => t.Id, t => t.CustomerId));
        thandi.Items.ShouldNotBeEmpty();
        thandi.Items.ShouldAllBe(t => owners[t.Id] == DemoData.Thandi);
        sipho.Items.ShouldNotBeEmpty();
        sipho.Items.ShouldAllBe(t => owners[t.Id] == DemoData.Sipho);
    }

    [Fact]
    public async Task A_customer_cannot_dispute_another_customers_transaction()
    {
        var response = await Portal.Thandi().SubmitDisputeAsync(DemoData.SiphoPendingCardId, "not_received");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "transaction_not_found");
        (await Portal.WithDisputesAsync(db => db.Disputes.CountAsync())).ShouldBe(0);
    }

    [Fact]
    public async Task A_customer_cannot_read_another_customers_dispute()
    {
        var dispute = await SiphoRaisesADisputeAsync();

        var response = await Portal.Thandi().GetAsync($"/api/v1/disputes/{dispute.Id}");

        // 404 and not 403: the answer must not confirm that the dispute exists.
        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "dispute_not_found");
        (await Portal.Thandi().GetAsync<PagedResult<DisputeSummaryDto>>("/api/v1/disputes")).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_customer_cannot_withdraw_another_customers_dispute()
    {
        var dispute = await SiphoRaisesADisputeAsync();

        var response = await Portal.Thandi().PostAsync($"/api/v1/disputes/{dispute.Id}/withdraw");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "dispute_not_found");
        (await Portal.WithDisputesAsync(db => db.Disputes.SingleAsync())).Status.ShouldBe(DisputeStatus.Submitted);
    }

    [Fact]
    public async Task A_customer_cannot_comment_on_another_customers_dispute()
    {
        var dispute = await SiphoRaisesADisputeAsync();

        var response = await Portal.Thandi().PostAsync($"/api/v1/disputes/{dispute.Id}/comments", new AddCommentRequest("Let me in."));

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "dispute_not_found");
        (await Portal.WithDisputesAsync(db => db.Disputes.Include(d => d.Comments).SingleAsync())).Comments.ShouldBeEmpty();
    }

    [Fact]
    public async Task A_customer_cannot_see_or_mark_another_customers_notification()
    {
        await SiphoRaisesADisputeAsync();
        await DispatchOutboxAsync();
        var siphosNotification = (await Portal.Sipho().GetAsync<PagedResult<NotificationDto>>("/api/v1/notifications")).Items.ShouldHaveSingleItem();

        var response = await Portal.Thandi().PostAsync($"/api/v1/notifications/{siphosNotification.Id}/read");

        await response.ShouldBeProblemAsync(HttpStatusCode.NotFound, "notification_not_found");
        (await Portal.Thandi().GetAsync<PagedResult<NotificationDto>>("/api/v1/notifications")).Items.ShouldBeEmpty();
    }

    private async Task<DisputeDetailDto> SiphoRaisesADisputeAsync() =>
        await (await Portal.Sipho().SubmitDisputeAsync(DemoData.SiphoPendingCardId, "not_received")).ReadAsync<DisputeDetailDto>(HttpStatusCode.Created);
}
