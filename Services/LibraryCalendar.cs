namespace LibraryManagementSystem.Services;

/// <summary>
/// Supplies calendar dates in the configured library timezone.
/// Circulation date columns are date-only values stored at midnight; no UTC-to-local
/// conversion is applied when reading those values back.
/// </summary>
public sealed class LibraryCalendar
{
    private readonly TimeZoneInfo timeZone;

    public LibraryCalendar(IConfiguration configuration)
    {
        var zoneId = configuration["Library:TimeZoneId"];
        timeZone = TimeZoneInfo.FindSystemTimeZoneById(
            string.IsNullOrWhiteSpace(zoneId) ? "Asia/Kolkata" : zoneId);
    }

    public string TimeZoneId => timeZone.Id;

    public DateTime Today =>
        TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, timeZone).Date;

    public DateTimeOffset ToLocal(DateTimeOffset instant) =>
        TimeZoneInfo.ConvertTime(instant, timeZone);

    public DateTime ToUtc(DateTime localDateTime) =>
        TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(localDateTime, DateTimeKind.Unspecified), timeZone);
}
