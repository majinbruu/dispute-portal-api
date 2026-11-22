namespace DisputePortal.SharedKernel;

/// <summary>
/// Calendar dates for banking rules are South African dates, not UTC dates.
/// SAST is a fixed UTC+2 offset with no daylight saving, so the zone is defined here
/// and the service does not depend on time zone data being present in the container image.
/// </summary>
public static class SouthAfricanTime
{
    public static readonly TimeZoneInfo Zone = TimeZoneInfo.CreateCustomTimeZone(
        "SAST", TimeSpan.FromHours(2), "South Africa Standard Time", "SAST");

    public static DateOnly Today(TimeProvider timeProvider) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), Zone).DateTime);
}
