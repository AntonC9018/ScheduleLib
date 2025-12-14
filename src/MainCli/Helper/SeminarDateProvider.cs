using AutoConstructor.Attributes;

namespace MainCli.Helper;

[AutoConstructor]
public sealed partial class RegularSeminarDateProvider
{
    private readonly LessonTimeConfig _lessonTimeConfig;

    public RegularSeminarDate Get()
    {
        var startTime = new TimeOnly(hour: 15, minute: 00);
        var timeSlot = _lessonTimeConfig.FindTimeSlotByStartTime(startTime)!.Value;
        return new(DayOfWeek.Wednesday, timeSlot);
    }
}

public readonly record struct RegularSeminarDate(
    DayOfWeek Day,
    TimeSlot TimeSlot);

