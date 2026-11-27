namespace DisputePortal.Modules.Disputes.Domain;

/// <summary>
/// A reason a customer can give for a dispute. The id is stable and is what the API accepts. The code is what the
/// payment scheme calls the reason, and for a card it depends on the card: Visa and Mastercard number their
/// reasons differently, and several reasons share one code.
/// </summary>
internal sealed record DisputeReason(
    string Id,
    string Label,
    Func<DisputedTransaction, bool> IsOfferedFor,
    Func<DisputedTransaction, string> CodeFor);

/// <summary>A reason as it applies to one transaction, with the scheme code worked out.</summary>
internal sealed record ResolvedDisputeReason(string Id, string Label, string Code);

internal static class DisputeReasons
{
    /// <summary>ISO 20022 return reason codes, sent in the pacs.004.</summary>
    private static readonly IReadOnlyList<DisputeReason> DebitOrderReasons =
    [
        // A DebiCheck mandate was authenticated by the customer with their bank, so "not authorised" cannot apply to it.
        new("not_authorised", "I did not authorise this debit order", t => t.DebitOrder!.MandateType != TransactionValues.DebiCheck, _ => "MD01"),
        new("incorrect_amount", "The amount is not what I agreed to", Always, _ => "AM09"),
        new("wrong_date", "It was collected on the wrong date", Always, _ => "DT01"),
        new("cancelled", "I cancelled this agreement", Always, _ => "MD06"),
        new("duplicate", "I was debited twice", Always, _ => "AM05"),
    ];

    /// <summary>Visa dispute condition codes and Mastercard chargeback reason codes.</summary>
    private static readonly IReadOnlyList<DisputeReason> CardReasons =
    [
        // A transaction the cardholder verified with a PIN or 3-D Secure cannot be disputed as not theirs.
        // Visa separates fraud where the card was not present (10.4) from fraud where it was (10.3).
        new(
            "not_authorised",
            "I did not make this transaction",
            t => t.Card!.Verification is not (TransactionValues.Pin or TransactionValues.ThreeDSecure),
            t => ByScheme(t, visa: IsCardNotPresent(t.Card!) ? "10.4" : "10.3", mastercard: "4837")),
        new("duplicate", "I was charged twice", Always, t => ByScheme(t, visa: "12.6", mastercard: "4834")),
        new("incorrect_amount", "The amount is wrong", Always, t => ByScheme(t, visa: "12.5", mastercard: "4834")),
        new("not_received", "I did not receive the goods or service", Always, t => ByScheme(t, visa: "13.1", mastercard: "4853")),
        new(
            "cancelled_recurring",
            "I cancelled this subscription",
            t => t.Card!.EntryMode == TransactionValues.Recurring,
            t => ByScheme(t, visa: "13.2", mastercard: "4853")),
        new("credit_not_processed", "I was promised a refund", Always, t => ByScheme(t, visa: "13.6", mastercard: "4853")),
    ];

    /// <summary>Every reason id of every transaction type. An id outside this list is a malformed request.</summary>
    public static IReadOnlyList<string> KnownIds { get; } =
        DebitOrderReasons.Concat(CardReasons).Select(reason => reason.Id).Distinct(StringComparer.Ordinal).ToList();

    /// <summary>The reasons the customer may choose for this transaction, each with its scheme code.</summary>
    public static IReadOnlyList<ResolvedDisputeReason> OfferedFor(DisputedTransaction transaction) =>
        ReasonsFor(transaction.Type)
            .Where(reason => reason.IsOfferedFor(transaction))
            .Select(reason => new ResolvedDisputeReason(reason.Id, reason.Label, reason.CodeFor(transaction)))
            .ToList();

    /// <summary>Returns the reason with its scheme code, or null when the id is unknown or not offered for this transaction.</summary>
    public static ResolvedDisputeReason? Resolve(string? reasonId, DisputedTransaction transaction) =>
        OfferedFor(transaction).FirstOrDefault(reason => string.Equals(reason.Id, reasonId, StringComparison.Ordinal));

    /// <summary>The customer wording of a reason already stored on a dispute.</summary>
    public static string LabelOf(DisputedTransactionType type, string reasonId) =>
        ReasonsFor(type).FirstOrDefault(reason => string.Equals(reason.Id, reasonId, StringComparison.Ordinal))?.Label ?? reasonId;

    private static IReadOnlyList<DisputeReason> ReasonsFor(DisputedTransactionType type) => type switch
    {
        DisputedTransactionType.DebitOrder => DebitOrderReasons,
        DisputedTransactionType.Card => CardReasons,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
    };

    private static bool Always(DisputedTransaction transaction) => true;

    private static bool IsCardNotPresent(DisputedCard card) =>
        card.EntryMode is TransactionValues.ECommerce or TransactionValues.Recurring;

    private static string ByScheme(DisputedTransaction transaction, string visa, string mastercard) => transaction.Card!.Scheme switch
    {
        TransactionValues.Visa => visa,
        TransactionValues.Mastercard => mastercard,
        _ => throw new DisputeRuleException($"There are no dispute reason codes for the card scheme '{transaction.Card.Scheme}'."),
    };
}

/// <summary>
/// The values in the Transactions module's contract that the dispute rules depend on. That contract passes them
/// as text, so they are named once here. A unit test checks them against the Transactions module's own values.
/// </summary>
internal static class TransactionValues
{
    public const string DebiCheck = "DebiCheck";
    public const string Visa = "Visa";
    public const string Mastercard = "Mastercard";
    public const string ECommerce = "ECommerce";
    public const string Recurring = "Recurring";
    public const string Pin = "Pin";
    public const string ThreeDSecure = "ThreeDSecure";
}
