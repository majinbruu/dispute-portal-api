using System.Diagnostics.Metrics;

namespace DisputePortal.SharedKernel;

/// <summary>Business metrics, recorded through the vendor-neutral .NET metrics API and exposed on /metrics.</summary>
public static class PortalMetrics
{
    public const string MeterName = "DisputePortal";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    // The exporter prefixes each instrument with the meter name, for example disputeportal_disputes_submitted.
    public static readonly Counter<long> DisputesSubmitted =
        Meter.CreateCounter<long>("disputes_submitted", description: "Disputes submitted by customers.");

    public static readonly Counter<long> DisputesReviewed =
        Meter.CreateCounter<long>("disputes_reviewed", description: "Review decisions on submitted disputes, by decision (Approved or Rejected) and by who made it (Agent or System).");

    public static readonly Counter<long> DisputesCompleted =
        Meter.CreateCounter<long>("disputes_completed", description: "Disputes that reached an outcome.");

    public static readonly Counter<long> DisputesWithdrawn =
        Meter.CreateCounter<long>("disputes_withdrawn", description: "Disputes withdrawn by the customer before an agent reviewed them.");

    public static readonly Counter<long> OutboxMessagesDispatched =
        Meter.CreateCounter<long>("outbox_dispatched", description: "Outbox messages delivered to consumers.");

    public static readonly Counter<long> OutboxMessagesFailed =
        Meter.CreateCounter<long>("outbox_failed", description: "Outbox delivery attempts that failed.");

    public static readonly Counter<long> OutboxMessagesAbandoned =
        Meter.CreateCounter<long>("outbox_abandoned", description: "Outbox messages given up on after the last retry. Each one needs a person to look at it.");
}
