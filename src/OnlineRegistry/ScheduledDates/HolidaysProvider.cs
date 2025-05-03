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
            validFrom: p.From.ToDateTimeOffset(),
            validTo: p.To.ToDateTimeOffset(),
            languageIsoCode: _config.LanguageIsoCode,
            subdivisionCode: _config.SubdivisionCode);
        var ret = response
            .OrderBy(x => x.StartDate)
            .Select(x =>
            {
                var end = x.EndDate.ToDateOnly();
                // It's inclusive in the API (figured it out experimentally)
                end = end.AddDays(1);

                var ret = new HolidayPeriod(
                    start: x.StartDate.ToDateOnly(),
                    endExclusive: end);
                return ret;
            });
        return ret.ToArray();
    }
}

public sealed class HolidayConfig
{
    public required string CountryIsoCode { get; init; }
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

file static class DateOnlyExtensions
{
    public static DateTimeOffset ToDateTimeOffset(
        this DateOnly dateOnly)
    {
        var dateTime = dateOnly.ToDateTime(time: new TimeOnly(0));
        return new DateTimeOffset(dateTime, offset: new TimeSpan(0));
    }

    public static DateOnly ToDateOnly(
        this DateTimeOffset dto)
    {
        var ret = DateOnly.FromDateTime(dto.Date);
        return ret;
    }
}
