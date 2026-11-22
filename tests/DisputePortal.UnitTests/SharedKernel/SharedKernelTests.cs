using DisputePortal.SharedKernel;
using Microsoft.Extensions.Time.Testing;

namespace DisputePortal.UnitTests.SharedKernel;

public class MoneyTests
{
    [Fact]
    public void Rounds_to_two_decimal_places_using_bankers_rounding()
    {
        Money.Zar(10.005m).Amount.ShouldBe(10.00m);
        Money.Zar(10.015m).Amount.ShouldBe(10.02m);
    }

    [Fact]
    public void Normalises_the_currency_code()
    {
        new Money(1m, " zar ").Currency.ShouldBe("ZAR");
    }

    [Fact]
    public void Rejects_a_negative_amount()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Money.Zar(-0.01m));
    }

    [Theory]
    [InlineData("")]
    [InlineData("R")]
    [InlineData("RAND")]
    public void Rejects_a_currency_that_is_not_three_letters(string currency)
    {
        Should.Throw<ArgumentException>(() => new Money(1m, currency));
    }

    [Fact]
    public void Refuses_to_compare_different_currencies()
    {
        Should.Throw<InvalidOperationException>(() => Money.Zar(1m).IsGreaterThan(new Money(1m, "USD")));
    }

    [Fact]
    public void Compares_amounts_in_the_same_currency()
    {
        Money.Zar(2m).IsGreaterThan(Money.Zar(1m)).ShouldBeTrue();
        Money.Zar(1m).IsGreaterThan(Money.Zar(1m)).ShouldBeFalse();
    }
}

public class SouthAfricanTimeTests
{
    [Theory]
    [InlineData("2026-11-22T21:59:59Z", "2026-11-22")] // 23:59:59 SAST
    [InlineData("2026-11-22T22:00:00Z", "2026-11-23")] // 00:00:00 SAST the next day
    [InlineData("2026-11-22T10:00:00Z", "2026-11-22")]
    public void Today_is_the_south_african_calendar_date_not_the_utc_date(string utcNow, string expected)
    {
        var clock = new FakeTimeProvider(DateTimeOffset.Parse(utcNow));

        SouthAfricanTime.Today(clock).ShouldBe(DateOnly.Parse(expected));
    }
}

public class PagingTests
{
    [Fact]
    public void Defaults_to_the_first_page_of_twenty()
    {
        var page = new PageRequest(null, null);

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(20);
        page.Skip.ShouldBe(0);
    }

    [Fact]
    public void Clamps_out_of_range_values()
    {
        var page = new PageRequest(0, 5000);

        page.Page.ShouldBe(1);
        page.PageSize.ShouldBe(PageRequest.MaxPageSize);
    }

    [Fact]
    public void Calculates_skip_and_total_pages()
    {
        new PageRequest(3, 20).Skip.ShouldBe(40);
        new PagedResult<int>([1, 2], 1, 20, 41).TotalPages.ShouldBe(3);
        new PagedResult<int>([], 1, 20, 0).TotalPages.ShouldBe(0);
    }
}

public class ResultTests
{
    [Fact]
    public void A_failed_result_has_no_value()
    {
        Result<int> result = Error.NotFound("missing", "Not there.");

        result.IsSuccess.ShouldBeFalse();
        result.Error!.Code.ShouldBe("missing");
        Should.Throw<InvalidOperationException>(() => { _ = result.Value; });
    }

    [Fact]
    public void A_successful_result_exposes_its_value()
    {
        var result = Result<int>.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }
}
