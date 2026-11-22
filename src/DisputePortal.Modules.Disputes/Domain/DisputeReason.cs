namespace DisputePortal.Modules.Disputes.Domain;

/// <summary>A reason a customer can give for a dispute, with the ISO 20022 return reason code sent in the pacs.004.</summary>
internal sealed record DisputeReason(string Code, string Description, bool RegisteredMandatesOnly = false)
{
    public const string DebiCheck = "DebiCheck";
    public const string Registered = "Registered";

    public bool IsAllowedFor(string mandateType) =>
        !RegisteredMandatesOnly || string.Equals(mandateType, Registered, StringComparison.Ordinal);
}

internal static class DisputeReasons
{
    public static readonly IReadOnlyList<DisputeReason> All =
    [
        // A DebiCheck mandate was authenticated by the customer with their bank, so "not authorised" cannot apply to it.
        new("MD01", "I did not authorise this debit order", RegisteredMandatesOnly: true),
        new("AM09", "The amount is not what I agreed to"),
        new("DT01", "It was collected on the wrong date"),
        new("MD06", "I cancelled this agreement"),
        new("AM05", "I was debited twice"),
    ];

    public static DisputeReason? Find(string? code) =>
        All.FirstOrDefault(reason => string.Equals(reason.Code, code, StringComparison.Ordinal));

    public static IReadOnlyList<DisputeReason> AllowedFor(string mandateType) =>
        All.Where(reason => reason.IsAllowedFor(mandateType)).ToList();
}
