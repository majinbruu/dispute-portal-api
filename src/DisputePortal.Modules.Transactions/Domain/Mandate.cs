using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Domain;

internal sealed record MandateDetails(
    Guid Id,
    Guid CustomerId,
    string MandateReference,
    string ContractReference,
    MandateType Type,
    string CreditorName,
    string CreditorShortName,
    Money InstalmentAmount,
    Money MaximumCollectionAmount,
    CollectionFrequency Frequency,
    int CollectionDay,
    DateOnly FirstCollectionDate,
    bool DateAdjustmentAllowed,
    bool TrackingEnabled,
    string? AuthenticationType = null,
    DateTimeOffset? AuthenticatedAt = null,
    MandateStatus Status = MandateStatus.Active);

/// <summary>The customer's authority for a creditor to collect. Every collection quotes a mandate.</summary>
internal sealed class Mandate
{
    private Mandate()
    {
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public string MandateReference { get; private set; } = null!;

    public string ContractReference { get; private set; } = null!;

    public MandateType Type { get; private set; }

    public string CreditorName { get; private set; } = null!;

    /// <summary>The abbreviated name that prints on the customer's statement.</summary>
    public string CreditorShortName { get; private set; } = null!;

    public decimal InstalmentAmount { get; private set; }

    public decimal MaximumCollectionAmount { get; private set; }

    public string Currency { get; private set; } = null!;

    public CollectionFrequency Frequency { get; private set; }

    public int CollectionDay { get; private set; }

    public DateOnly FirstCollectionDate { get; private set; }

    /// <summary>Whether the creditor may collect on a day other than <see cref="CollectionDay"/>.</summary>
    public bool DateAdjustmentAllowed { get; private set; }

    public bool TrackingEnabled { get; private set; }

    public string? AuthenticationType { get; private set; }

    public DateTimeOffset? AuthenticatedAt { get; private set; }

    public MandateStatus Status { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static Mandate Create(MandateDetails details, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(details.MandateReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.ContractReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.CreditorName);
        ArgumentException.ThrowIfNullOrWhiteSpace(details.CreditorShortName);

        if (details.CollectionDay is < 1 or > 31)
        {
            throw new ArgumentOutOfRangeException(nameof(details), details.CollectionDay, "Collection day must be between 1 and 31.");
        }

        if (details.InstalmentAmount.IsGreaterThan(details.MaximumCollectionAmount))
        {
            throw new ArgumentException("The maximum collection amount cannot be below the instalment amount.", nameof(details));
        }

        if (details.Type == MandateType.DebiCheck && (details.AuthenticationType is null || details.AuthenticatedAt is null))
        {
            throw new ArgumentException("A DebiCheck mandate must record how and when the customer authenticated it.", nameof(details));
        }

        return new Mandate
        {
            Id = details.Id,
            CustomerId = details.CustomerId,
            MandateReference = details.MandateReference,
            ContractReference = details.ContractReference,
            Type = details.Type,
            CreditorName = details.CreditorName,
            CreditorShortName = details.CreditorShortName,
            InstalmentAmount = details.InstalmentAmount.Amount,
            MaximumCollectionAmount = details.MaximumCollectionAmount.Amount,
            Currency = details.InstalmentAmount.Currency,
            Frequency = details.Frequency,
            CollectionDay = details.CollectionDay,
            FirstCollectionDate = details.FirstCollectionDate,
            DateAdjustmentAllowed = details.DateAdjustmentAllowed,
            TrackingEnabled = details.TrackingEnabled,
            AuthenticationType = details.Type == MandateType.DebiCheck ? details.AuthenticationType : null,
            AuthenticatedAt = details.Type == MandateType.DebiCheck ? details.AuthenticatedAt : null,
            Status = details.Status,
            CreatedAt = now,
        };
    }
}
