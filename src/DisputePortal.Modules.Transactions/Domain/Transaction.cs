using DisputePortal.SharedKernel;

namespace DisputePortal.Modules.Transactions.Domain;

/// <summary>Whether a transaction can be disputed at all, and why. Decided once, when the transaction is recorded.</summary>
internal readonly record struct DisputabilityDecision(bool IsDisputable, DisputabilityReason Reason);

/// <summary>
/// What every transaction on the customer's account shares. Each type adds its own details in its own table
/// (table per type), so the list, the ownership check and the dispute window work the same way for all of them.
/// </summary>
internal abstract class Transaction
{
    protected Transaction()
    {
    }

    protected Transaction(
        Guid id,
        Guid customerId,
        TransactionType type,
        string accountNumber,
        string counterpartyName,
        Money amount,
        DateOnly effectiveDate,
        DisputabilityDecision decision,
        DateOnly lastDisputeDate,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(counterpartyName);

        Id = id;
        CustomerId = customerId;
        Type = type;
        AccountMasked = NumberMask.KeepLastFour(accountNumber);
        CounterpartyName = counterpartyName;
        Amount = amount.Amount;
        Currency = amount.Currency;
        EffectiveDate = effectiveDate;
        IsDisputable = decision.IsDisputable;
        DisputabilityReason = decision.Reason;
        LastDisputeDate = lastDisputeDate;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid CustomerId { get; private set; }

    public TransactionType Type { get; private set; }

    /// <summary>Only the last four digits are stored. The full account number never reaches the database or the logs.</summary>
    public string AccountMasked { get; private set; } = null!;

    /// <summary>Who was paid: the creditor of a debit order, or the merchant of a card transaction.</summary>
    public string CounterpartyName { get; private set; } = null!;

    public decimal Amount { get; private set; }

    public string Currency { get; private set; } = null!;

    /// <summary>
    /// The date the dispute window counts from: the action date of a debit order,
    /// or the posting date of a card transaction.
    /// </summary>
    public DateOnly EffectiveDate { get; private set; }

    public bool IsDisputable { get; private set; }

    public DisputabilityReason DisputabilityReason { get; private set; }

    public DateOnly LastDisputeDate { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>
    /// Whether the customer can dispute this transaction today. The type's own rules were applied when the
    /// transaction was recorded; what remains depends on time and on whether a dispute already exists.
    /// </summary>
    public DisputeEligibility EvaluateEligibility(DateOnly today, bool alreadyDisputed)
    {
        if (!IsDisputable)
        {
            return DisputabilityReason switch
            {
                DisputabilityReason.CollectionRejected => DisputeEligibility.CollectionRejected,
                DisputabilityReason.WithinMandateTerms => DisputeEligibility.WithinMandateTerms,
                DisputabilityReason.NotAPurchase => DisputeEligibility.NotAPurchase,
                DisputabilityReason.NotSettled => DisputeEligibility.NotSettled,
                DisputabilityReason.FullyRefunded => DisputeEligibility.FullyRefunded,
                _ => throw new InvalidOperationException($"{DisputabilityReason} is not a reason for refusing a dispute."),
            };
        }

        if (alreadyDisputed)
        {
            return DisputeEligibility.AlreadyDisputed;
        }

        return today > LastDisputeDate ? DisputeEligibility.DisputeWindowClosed : DisputeEligibility.Eligible;
    }
}

internal static class NumberMask
{
    /// <summary>Replaces every digit of an account or card number except the last four.</summary>
    public static string KeepLastFour(string number)
    {
        var digits = new string((number ?? string.Empty).Where(char.IsDigit).ToArray());
        if (digits.Length < 4)
        {
            throw new ArgumentException("An account or card number must have at least four digits.", nameof(number));
        }

        return new string('*', digits.Length - 4) + digits[^4..];
    }
}
