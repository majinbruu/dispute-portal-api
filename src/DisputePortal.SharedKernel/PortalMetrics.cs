using Prometheus;

namespace DisputePortal.SharedKernel;

/// <summary>
/// Business counters, exposed on /metrics. They are Prometheus counters that exist with the value 0 from
/// start-up, so rate() and increase() see the first event as an increase and an alert on them can fire.
/// The .NET meter adapter is not used for these: it exports a counter as a gauge, only once it has been
/// incremented, and drops it again when it has not changed for a while. The names end in _total because the
/// OpenMetrics format, which Prometheus asks for when it scrapes, only accepts a counter under such a name.
/// </summary>
public static class PortalMetrics
{
    public static readonly Counter DisputesSubmitted =
        Metrics.CreateCounter("disputeportal_disputes_submitted_total", "Disputes submitted by customers.");

    public static readonly Counter DisputesReviewed = Metrics.CreateCounter(
        "disputeportal_disputes_reviewed_total",
        "Review decisions on submitted disputes, by decision (Approved or Rejected) and by who made it (Agent or System).",
        "decision",
        "by");

    public static readonly Counter DisputesCompleted =
        Metrics.CreateCounter("disputeportal_disputes_completed_total", "Disputes that reached an outcome (Refunded or Declined).", "outcome");

    public static readonly Counter DisputesWithdrawn =
        Metrics.CreateCounter("disputeportal_disputes_withdrawn_total", "Disputes withdrawn by the customer before an agent reviewed them.");

    public static readonly Counter DisputeCommentsAdded =
        Metrics.CreateCounter("disputeportal_dispute_comments_added_total", "Comments added to disputes, by author (Customer or Agent).", "author");

    public static readonly Counter OutboxMessagesDispatched =
        Metrics.CreateCounter("disputeportal_outbox_dispatched_total", "Outbox messages delivered to consumers.");

    public static readonly Counter OutboxMessagesFailed =
        Metrics.CreateCounter("disputeportal_outbox_failed_total", "Outbox delivery attempts that failed.");

    public static readonly Counter OutboxMessagesAbandoned = Metrics.CreateCounter(
        "disputeportal_outbox_abandoned_total",
        "Outbox messages given up on after the last retry. Each one needs a person to look at it.");

    /// <summary>
    /// Called once by the host at start-up. A counter without labels is published at 0 as soon as it is created;
    /// a labelled one only has a series once a label combination is first used, so every combination is named here.
    /// </summary>
    public static void PublishInitialValues()
    {
        DisputesReviewed.WithLabels("Approved", "Agent");
        DisputesReviewed.WithLabels("Approved", "System");
        DisputesReviewed.WithLabels("Rejected", "Agent");
        DisputesCompleted.WithLabels("Refunded");
        DisputesCompleted.WithLabels("Declined");
        DisputeCommentsAdded.WithLabels("Customer");
        DisputeCommentsAdded.WithLabels("Agent");
    }
}
