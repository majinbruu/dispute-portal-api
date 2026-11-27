namespace DisputePortal.SharedKernel;

/// <summary>
/// Fixed identifiers shared by the demo seed data and the Keycloak demo realm.
/// The customer and agent IDs are the Keycloak user IDs (the token subject).
/// </summary>
public static class DemoData
{
    /// <summary>Demo customer with a full history.</summary>
    public static readonly Guid Thandi = Guid.Parse("8f14e45f-ceea-4672-a9d6-5f0c1d2e3a01");

    /// <summary>Second demo customer, used to show that data is isolated per customer.</summary>
    public static readonly Guid Sipho = Guid.Parse("8f14e45f-ceea-4672-a9d6-5f0c1d2e3a02");

    /// <summary>Demo customer with no debit orders, used to show empty states.</summary>
    public static readonly Guid Lerato = Guid.Parse("8f14e45f-ceea-4672-a9d6-5f0c1d2e3a03");

    /// <summary>The demo agent, who reviews disputes. An agent is not a customer and has no transactions.</summary>
    public static readonly Guid Zanele = Guid.Parse("8f14e45f-ceea-4672-a9d6-5f0c1d2e3a04");

    // Transactions the demo disputes are raised against. Each name says how its dispute ends up.
    public static readonly Guid RefundedDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000001");
    public static readonly Guid PendingDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000002");
    public static readonly Guid DeclinedDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000003");
    public static readonly Guid WithdrawnDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000004");
    public static readonly Guid AgentRejectedDebitOrderId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000005");

    public static readonly Guid PendingCardId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000011");
    public static readonly Guid RefundedCardId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000012");
    public static readonly Guid WithdrawnCardId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000013");
    public static readonly Guid DeclinedCardId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000014");
    public static readonly Guid SiphoPendingCardId = Guid.Parse("5a1d0c1e-0000-4000-8000-000000000015");

    /// <summary>The pacs.003 TxId of a debit order whose dispute the simulated scheme declines.</summary>
    public const string DeclinedTransactionId = "ACME-TX-RETURN-REJECT-0001";

    /// <summary>The acquirer reference number of a card purchase whose dispute the simulated scheme declines.</summary>
    public const string DeclinedCardReference = "74027000000000000000017";
}
