using DisputePortal.Modules.Disputes.Application;
using DisputePortal.Modules.Disputes.Domain;
using DisputePortal.SharedKernel;

namespace DisputePortal.IntegrationTests.Infrastructure;

/// <summary>
/// Disputes written straight to the database, for the tests that need many of them or need one in a given state
/// without going through the API. They are built with the aggregate's own methods and saved through the
/// application's DbContext, so their events and outbox messages are the real ones.
/// </summary>
internal static class TestDisputes
{
    /// <summary>A dispute waiting for an agent, against a debit order that exists only on the dispute.</summary>
    public static Dispute Submitted(DateTimeOffset now, Guid? customerId = null, string schemeReference = "ACME20261101000001") => Dispute.Submit(
        Guid.NewGuid(),
        DisputeReference.New(now),
        customerId ?? DemoData.Thandi,
        new DisputedTransaction(
            Guid.NewGuid(),
            DisputedTransactionType.DebitOrder,
            schemeReference,
            Money.Zar(599.00m),
            DateOnly.FromDateTime(now.UtcDateTime).AddDays(-5),
            "Vodacom",
            "******7890",
            new DisputedDebitOrder("VOD-7741203-20261122", "Registered", "MRN000000001")),
        "not_authorised",
        comment: null,
        now);

    /// <summary>A dispute an agent approved, now waiting for the scheme's answer.</summary>
    public static Dispute SentToScheme(DateTimeOffset now, string schemeReference = "ACME20261101000001")
    {
        var dispute = Submitted(now, schemeReference: schemeReference);
        dispute.MarkSentToScheme(SchemePayloadFactory.Create(dispute, now), DisputeActor.Agent(DemoData.Zanele), now);
        return dispute;
    }

    public static async Task<IReadOnlyList<Dispute>> AddDisputesAsync(this PortalFixture portal, params Dispute[] disputes)
    {
        await portal.WithDisputesAsync(db =>
        {
            db.Disputes.AddRange(disputes);
            return db.SaveChangesAsync();
        });
        return disputes;
    }
}
