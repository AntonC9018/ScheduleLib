using OpenHolidays;

namespace ScheduleLib.OnlineRegistry;

public sealed class HolidaysProvider
{
    private readonly HolidayConfig _config;
    private readonly OpenHolidaysClient _client;

    public HolidaysProvider(
        OpenHolidaysClient client,
        HolidayConfig config)
    {
        _client = client;
        _config = config;
    }

    public async Task<HolidayPeriod[]> GetHolidayPeriods(GetHolidayParams p)
    {
        var response = await _client.SchoolHolidaysAsync(
            countryIsoCode: _config.CountryIsoCode,
            validFrom: p.From.ToDateTimeOffset(_config.TimeZone),
            validTo: p.To.ToDateTimeOffset(_config.TimeZone),
            languageIsoCode: _config.LanguageIsoCode,
            subdivisionCode: _config.SubdivisionCode);
        var ret = response.Select(x =>
        {
            var ret = new HolidayPeriod(
                x.StartDate.ToDateOnly(_config.TimeZone),
                x.EndDate.ToDateOnly(_config.TimeZone));
            return ret;
        });
        return ret.ToArray();
    }
}

public sealed class HolidayConfig
{
    public required string CountryIsoCode { get; init; }
    public required TimeZoneInfo TimeZone { get; init; }
    public string? SubdivisionCode { get; init; }
    public string? LanguageIsoCode { get; init; }
}

public readonly struct GetHolidayParams
{
    public required DateOnly From { get; init; }
    public required DateOnly To { get; init; }
    public required CancellationToken CancellationToken { get; init; }
}

public readonly struct HolidayPeriod
{
    public HolidayPeriod(DateOnly start, DateOnly endExclusive)
    {
        Start = start;
        EndExclusive = endExclusive;
    }

    public HolidayPeriod(DateOnly singleDay)
    {
        Start = singleDay;
        EndExclusive = singleDay.AddDays(1);
    }

    public readonly DateOnly Start;
    public readonly DateOnly EndExclusive;
}

// https://stackoverflow.com/a/76164137/9731532
file static class DateOnlyExtensions
{
    public static DateTimeOffset ToDateTimeOffset(
        this DateOnly dateOnly,
        TimeZoneInfo zone)
    {
        var dateTime = dateOnly.ToDateTime(new TimeOnly(0));
        return new DateTimeOffset(dateTime, zone.GetUtcOffset(dateTime));
    }

    public static DateOnly ToDateOnly(
        this DateTimeOffset dto,
        TimeZoneInfo zone)
    {
        var inTargetZone = TimeZoneInfo.ConvertTime(dto, zone);
        return DateOnly.FromDateTime(inTargetZone.Date);
    }
}
