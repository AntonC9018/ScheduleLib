using AutoConstructor.Attributes;
using Microsoft.Extensions.Options;

namespace ScheduleLib.Application.Core.Helper;

[AutoConstructor]
public sealed partial class RegularSeminarDateProvider
{
    private readonly LessonTimeConfig _lessonTimeConfig;
    private readonly IOptions<RegularSeminarDateConfig> _options;

    public RegularSeminarDate Get()
    {
        var t = _options.Value;
        var timeSlot = _lessonTimeConfig.FindTimeSlotByStartTime(t.Time)!.Value;
        return new(t.Day, timeSlot);
    }
}

public sealed class RegularSeminarDateConfig
{
    public required TimeOnly Time { get; set; }
    public required DayOfWeek Day { get; set; }
}

public readonly record struct RegularSeminarDate(
    DayOfWeek Day,
    TimeSlot TimeSlot);

