namespace DisputePortal.SharedKernel;

/// <summary>
/// Fixed identifiers shared by the demo seed data and the Keycloak demo realm.
/// The customer IDs are the Keycloak user IDs (the token subject).
/// </summary>
public static class DemoData
{
    /// <summary>Demo customer with a full history.</summary>
    public static readonly Guid Thandi = Guid.Parse("8f14e45f-ceea-4672-a9d6-5f0c1d2e3a01");

    /// <summary>Second demo customer, used to show that data is isolated per customer.</summary>
    public static readonly Guid Sipho = Guid.Parse("8f14e45f-ceea-4672-a9d6-5f0c1d2e3a02");

    /// <summary>Demo customer with no debit orders, used to show empty states.</summary>
    public static readonly Guid Lerato = Guid.Parse("8f14e45f-ceea-4672-a9d6-5f0c1d2e3a03");

    public static readonly Guid RefundedDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000001");
    public static readonly Guid PendingDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000002");
    public static readonly Guid DeclinedDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000003");

    /// <summary>The simulated scheme rejects a return for this collection, which exercises the Declined outcome.</summary>
    public const string DeclinedTransactionId = "ACME-TX-RETURN-REJECT-0001";

    /// <summary>The acquirer reference number of a card purchase whose dispute the simulated scheme declines.</summary>
    public const string DeclinedCardReference = "74027000000000000000017";
}
