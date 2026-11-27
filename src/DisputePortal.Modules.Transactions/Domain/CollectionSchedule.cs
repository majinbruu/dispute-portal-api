namespace DisputePortal.Modules.Transactions.Domain;

/// <summary>Works out the dates on which a mandate says the creditor will collect.</summary>
internal static class CollectionSchedule
{
    /// <summary>
    /// Whether <paramref name="date"/> is a date the mandate expects a collection on. Nothing is expected before
    /// the first collection date. A collection day that a shorter month does not have falls on its last day.
    /// </summary>
    public static bool IsExpectedDate(Mandate mandate, DateOnly date)
    {
        var first = mandate.FirstCollectionDate;
        if (date < first)
        {
            return false;
        }

        return mandate.Frequency switch
        {
            CollectionFrequency.Weekly => IsWholeNumberOfPeriods(first, date, days: 7),
            CollectionFrequency.Fortnightly => IsWholeNumberOfPeriods(first, date, days: 14),
            CollectionFrequency.Monthly => date.Day == Math.Min(mandate.CollectionDay, DateTime.DaysInMonth(date.Year, date.Month)),
            CollectionFrequency.Quarterly => IsAnniversary(first, date, months: 3),
            CollectionFrequency.Annually => IsAnniversary(first, date, months: 12),
            _ => throw new ArgumentOutOfRangeException(nameof(mandate), mandate.Frequency, null),
        };
    }

    private static bool IsWholeNumberOfPeriods(DateOnly first, DateOnly date, int days) =>
        (date.DayNumber - first.DayNumber) % days == 0;

    /// <summary>
    /// Each expected date is counted in months from the first collection date, not from the previous one,
    /// so a date that was moved to the end of a short month does not pull the later dates forward with it.
    /// </summary>
    private static bool IsAnniversary(DateOnly first, DateOnly date, int months)
    {
        var monthsSinceFirst = ((date.Year - first.Year) * 12) + date.Month - first.Month;
        return monthsSinceFirst % months == 0 && first.AddMonths(monthsSinceFirst) == date;
    }
}
