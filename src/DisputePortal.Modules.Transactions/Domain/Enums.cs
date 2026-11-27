namespace DisputePortal.Modules.Transactions.Domain;

internal enum TransactionType
{
    DebitOrder,
    Card,
}

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
    /// <summary>The money left the customer's account.</summary>
    Accepted,

    /// <summary>Nothing was debited.</summary>
    Rejected,
}

/// <summary>Why a transaction is, or is not, disputable under the rules for its type. Decided once, when it is recorded.</summary>
internal enum DisputabilityReason
{
    RegisteredMandate,
    AmountAboveMandateMaximum,
    DateOutsideMandateTerms,
    WithinMandateTerms,
    CollectionRejected,
    CardPurchase,
    NotAPurchase,
    NotSettled,
    FullyRefunded,
}

/// <summary>Whether the customer can dispute a transaction right now.</summary>
internal enum DisputeEligibility
{
    Eligible,
    WithinMandateTerms,
    CollectionRejected,
    NotAPurchase,
    NotSettled,
    FullyRefunded,
    DisputeWindowClosed,
    AlreadyDisputed,
}

internal enum CardScheme
{
    Visa,
    Mastercard,
}

internal enum CardTransactionKind
{
    Purchase,
    CashWithdrawal,
}

/// <summary>How the card details reached the merchant.</summary>
internal enum CardEntryMode
{
    Chip,
    Contactless,

    /// <summary>Typed into a website or app by the cardholder.</summary>
    ECommerce,

    /// <summary>Charged by the merchant from stored card details, as a subscription does.</summary>
    Recurring,
}

/// <summary>How the cardholder proved it was them. A verified transaction cannot be disputed as "not mine".</summary>
internal enum CardholderVerification
{
    Pin,
    ThreeDSecure,
    None,
}

internal enum CardSettlementStatus
{
    /// <summary>The money left the customer's account.</summary>
    Settled,

    /// <summary>Authorised and reserved, not yet posted.</summary>
    Pending,

    /// <summary>The authorisation was refused. Nothing was debited.</summary>
    Declined,

    /// <summary>Authorised and then cancelled before it was posted.</summary>
    Reversed,
}
