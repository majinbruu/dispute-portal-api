namespace DisputePortal.Modules.DebitOrders.Domain;

internal enum MandateType
{
    /// <summary>Authenticated Collections: the customer approved the mandate with their bank.</summary>
    DebiCheck,

    /// <summary>Registered Mandate: registered by the creditor, never confirmed by the customer with the bank.</summary>
    Registered,
}

internal enum MandateStatus
{
    Active,
    Suspended,
    Cancelled,
    Expired,
}

internal enum CollectionFrequency
{
    Weekly,
    Fortnightly,
    Monthly,
    Quarterly,
    Annually,
}

/// <summary>pacs.003 sequence type.</summary>
internal enum SequenceType
{
    First,
    Recurring,
    Final,
    OnceOff,
}

/// <summary>Outcome of the collection, from the pacs.002 status report.</summary>
internal enum DebitOrderStatus
{
    /// <summary>Accepted: the money left the customer's account.</summary>
    Successful,

    /// <summary>Rejected: nothing was debited.</summary>
    Failed,
}

/// <summary>Why a debit order is, or is not, disputable under the mandate rules. Decided once, when the collection is recorded.</summary>
internal enum DisputabilityReason
{
    RegisteredMandate,
    AmountAboveMandateMaximum,
    DateOutsideMandateTerms,
    WithinMandateTerms,
}

/// <summary>Whether the customer can dispute a debit order right now.</summary>
internal enum DisputeEligibility
{
    Eligible,
    WithinMandateTerms,
    CollectionFailed,
    DisputeWindowClosed,
    AlreadyDisputed,
}
