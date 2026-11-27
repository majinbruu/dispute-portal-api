using System.Net;
using System.Net.Http.Headers;
using DisputePortal.IntegrationTests.Infrastructure;
using DisputePortal.Modules.Disputes.Application;
using DisputePortal.SharedKernel;

namespace DisputePortal.IntegrationTests;

/// <summary>
/// What a Prometheus scrape of /metrics sees. The alert rules in deploy/monitoring use increase() on the
/// business counters, which needs each series to be a counter and to exist before its first event.
/// </summary>
[Collection(PortalCollection.Name)]
public class MetricsTests(PortalFixture portal) : PortalTest(portal)
{
    public static TheoryData<string> SeriesPublishedAtStartUp => new()
    {
        "disputeportal_disputes_submitted_total",
        """disputeportal_disputes_reviewed_total{decision="Approved",by="Agent"}""",
        """disputeportal_disputes_reviewed_total{decision="Approved",by="System"}""",
        """disputeportal_disputes_reviewed_total{decision="Rejected",by="Agent"}""",
        """disputeportal_disputes_completed_total{outcome="Refunded"}""",
        """disputeportal_disputes_completed_total{outcome="Declined"}""",
        "disputeportal_disputes_withdrawn_total",
        """disputeportal_dispute_comments_added_total{author="Customer"}""",
        """disputeportal_dispute_comments_added_total{author="Agent"}""",
        "disputeportal_outbox_dispatched_total",
        "disputeportal_outbox_failed_total",
        "disputeportal_outbox_abandoned_total",
    };

    [Theory]
    [MemberData(nameof(SeriesPublishedAtStartUp))]
    public async Task Every_business_series_is_a_counter_that_exists_before_anything_has_happened(string series)
    {
        var name = series.Split('{')[0];

        var text = await ScrapeAsync("text/plain");
        var openMetrics = await ScrapeAsync("application/openmetrics-text");

        text.ShouldContain($"# TYPE {name} counter");
        text.ShouldContain(line => line.StartsWith($"{series} ", StringComparison.Ordinal));

        // OpenMetrics, which Prometheus asks for, names the family without the suffix and the sample with it.
        openMetrics.ShouldContain($"# TYPE {name[..^"_total".Length]} counter");
        openMetrics.ShouldContain(line => line.StartsWith($"{series} ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_counter_shows_what_was_recorded_on_it()
    {
        var before = await ValueAsync("disputeportal_disputes_submitted_total");

        (await Portal.Thandi().SubmitDisputeAsync(DemoData.PendingCardId, "not_received")).StatusCode.ShouldBe(HttpStatusCode.Created);

        (await ValueAsync("disputeportal_disputes_submitted_total")).ShouldBe(before + 1);
        Metrics.Count(PortalMetrics.DisputesSubmitted).ShouldBe(1);
    }

    private async Task<double> ValueAsync(string series)
    {
        var line = (await ScrapeAsync("text/plain")).Single(l => l.StartsWith($"{series} ", StringComparison.Ordinal));
        return double.Parse(line.Split(' ')[1], System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task<string[]> ScrapeAsync(string accept)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));

        var response = await Portal.Anonymous().SendAsync(request);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        return (await response.Content.ReadAsStringAsync()).Split('\n');
    }
}
