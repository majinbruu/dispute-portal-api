namespace DisputePortal.SharedKernel;

/// <summary>
/// An amount in a specific currency. Amounts are decimals rounded to two places; floating point is never used for money.
/// </summary>
public readonly record struct Money
{
    public Money(decimal amount, string currency)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), amount, "A money amount cannot be negative.");
        }

        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
        {
            throw new ArgumentException("Currency must be a three-letter ISO 4217 code.", nameof(currency));
        }

        Amount = decimal.Round(amount, 2, MidpointRounding.ToEven);
        Currency = currency.Trim().ToUpperInvariant();
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money Zar(decimal amount) => new(amount, "ZAR");

    public bool IsGreaterThan(Money other)
    {
        if (!string.Equals(Currency, other.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Cannot compare {Currency} with {other.Currency}.");
        }

        return Amount > other.Amount;
    }

    public override string ToString() => $"{Currency} {Amount:0.00}";
}
