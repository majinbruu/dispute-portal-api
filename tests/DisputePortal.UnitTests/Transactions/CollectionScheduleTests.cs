using DisputePortal.Modules.Transactions.Domain;

namespace DisputePortal.UnitTests.Transactions;

public class CollectionScheduleTests
{
    [Theory]
    [InlineData("2026-01-05", true)]   // the first collection
    [InlineData("2026-01-12", true)]
    [InlineData("2026-11-23", true)]   // 46 weeks later
    [InlineData("2026-01-13", false)]
    [InlineData("2026-01-11", false)]
    public void A_weekly_mandate_collects_every_seven_days_from_the_first_collection(string date, bool expected)
    {
        IsExpected(CollectionFrequency.Weekly, "2026-01-05", date).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-01-19", true)]
    [InlineData("2026-02-02", true)]
    [InlineData("2026-01-12", false)]  // only one week after the first collection
    public void A_fortnightly_mandate_collects_every_fourteen_days_from_the_first_collection(string date, bool expected)
    {
        IsExpected(CollectionFrequency.Fortnightly, "2026-01-05", date).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-11-15", true)]
    [InlineData("2026-12-15", true)]
    [InlineData("2026-11-18", false)]
    [InlineData("2026-11-14", false)]
    public void A_monthly_mandate_collects_on_its_collection_day(string date, bool expected)
    {
        IsExpected(CollectionFrequency.Monthly, "2025-03-15", date, collectionDay: 15).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-03-31", true)]
    [InlineData("2026-04-30", true)]   // April has 30 days
    [InlineData("2026-02-28", true)]   // February in a normal year
    [InlineData("2028-02-29", true)]   // February in a leap year
    [InlineData("2028-02-28", false)]
    [InlineData("2026-03-30", false)]  // March does have a 31st
    public void A_monthly_collection_day_that_a_month_does_not_have_falls_on_its_last_day(string date, bool expected)
    {
        IsExpected(CollectionFrequency.Monthly, "2026-01-31", date, collectionDay: 31).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-02-28", true)]   // three months on, moved to the end of February
    [InlineData("2026-05-30", true)]   // six months on: back on the 30th, not pulled to the 28th
    [InlineData("2026-08-30", true)]
    [InlineData("2026-05-28", false)]
    [InlineData("2026-03-30", false)]  // right day, but not a quarter
    [InlineData("2026-02-27", false)]
    public void A_quarterly_mandate_collects_every_three_months_from_the_first_collection(string date, bool expected)
    {
        IsExpected(CollectionFrequency.Quarterly, "2025-11-30", date).ShouldBe(expected);
    }

    [Theory]
    [InlineData("2025-02-28", true)]   // no 29 February in 2025
    [InlineData("2028-02-29", true)]   // the next leap year
    [InlineData("2028-02-28", false)]
    [InlineData("2025-03-01", false)]
    [InlineData("2024-08-29", false)]  // right day, but only six months on
    public void An_annual_mandate_collects_every_twelve_months_from_the_first_collection(string date, bool expected)
    {
        IsExpected(CollectionFrequency.Annually, "2024-02-29", date).ShouldBe(expected);
    }

    [Theory]
    [InlineData(nameof(CollectionFrequency.Weekly), "2026-05-28")]
    [InlineData(nameof(CollectionFrequency.Fortnightly), "2026-05-21")]
    [InlineData(nameof(CollectionFrequency.Monthly), "2026-05-04")]
    [InlineData(nameof(CollectionFrequency.Quarterly), "2026-03-04")]
    [InlineData(nameof(CollectionFrequency.Annually), "2025-06-04")]
    public void Nothing_is_expected_before_the_first_collection_date(string frequency, string date)
    {
        // Each date is exactly one period before the first collection, so only the first-date rule can refuse it.
        IsExpected(Enum.Parse<CollectionFrequency>(frequency), "2026-06-04", date, collectionDay: 4).ShouldBeFalse();
    }

    private static bool IsExpected(CollectionFrequency frequency, string firstCollectionDate, string date, int collectionDay = 1) =>
        CollectionSchedule.IsExpectedDate(
            TestData.DebiCheckMandate(collectionDay: collectionDay, frequency: frequency, firstCollectionDate: DateOnly.Parse(firstCollectionDate)),
            DateOnly.Parse(date));
}
