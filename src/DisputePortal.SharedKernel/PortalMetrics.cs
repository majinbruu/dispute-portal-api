using System.Diagnostics.Metrics;

namespace DisputePortal.SharedKernel;

/// <summary>Business metrics, recorded through the vendor-neutral .NET metrics API and exposed on /metrics.</summary>
public static class PortalMetrics
{
    public const string MeterName = "DisputePortal";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    public static readonly Counter<long> DisputesSubmitted =
        Meter.CreateCounter<long>("dispute_portal_disputes_submitted", description: "Disputes submitted by customers.");

    public static readonly Counter<long> DisputesCompleted =
        Meter.CreateCounter<long>("dispute_portal_disputes_completed", description: "Disputes that reached an outcome.");

    public static readonly Counter<long> OutboxMessagesDispatched =
        Meter.CreateCounter<long>("dispute_portal_outbox_dispatched", description: "Outbox messages delivered to consumers.");

    public static readonly Counter<long> OutboxMessagesFailed =
        Meter.CreateCounter<long>("dispute_portal_outbox_failed", description: "Outbox delivery attempts that failed.");
}
