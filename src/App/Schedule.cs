using System.Text;
using Microsoft.VisualBasic.CompilerServices;

namespace App;

public sealed class Schedule
{
    public required List<RegularLesson> RegularLessons;
    public required List<OneTimeLesson> OneTimeLessons;
    public required List<Group> Groups;
    public required List<Teacher> Teachers;
}

public enum Parity
{
    OddWeek,
    EvenWeek,
    EveryWeek,
}

public sealed class LessonTimeConfig
{
    public required TimeSpan LessonDuration;
    public required TimeOnly[] TimeSlotStarts;

    public static LessonTimeConfig CreateDefault()
    {
        var ret = new LessonTimeConfig
        {
            LessonDuration = TimeSpan.FromMinutes(90),
            TimeSlotStarts = CreateDefaultTimeSlots(),
        };
        return ret;
    }

    public static TimeOnly[] CreateDefaultTimeSlots()
    {
        TimeOnly New(int hour, int min)
        {
            var t = new TimeSpan(hours: hour, minutes: min, seconds: 0);
            var ret = TimeOnly.FromTimeSpan(t);
            return ret;
        }

        return [
            New(8, 00),
            New(9, 45),
            New(11, 30),
            New(13, 00),
            New(15, 00),
            New(16, 45),
            New(18, 30),
        ];
    }

    public TimeSlotInterval GetTimeSlotInterval(TimeSlot index)
    {
        var start = TimeSlotStarts[index.Index];
        var ret = new TimeSlotInterval(start, LessonDuration);
        return ret;
    }
}

public record struct TimeSlotInterval(TimeOnly Start, TimeSpan Duration)
{
    public TimeOnly End => Start.Add(Duration);
}

public record struct TimeSlot(int Index);

public record struct RegularLessonDate
{
    public required Parity Parity;
    public required DayOfWeek DayOfWeek;
    public required TimeSlot TimeSlot;
}

public record struct OneTimeLessonDate
{
    public required DateOnly Date;
    public required TimeSlot TimeSlot;
}

public record struct LessonData
{
    public required TeacherId Teacher;
    public required GroupId Group;
    public required SubGroupNumber SubGroup;
    public required string Room;
}

public sealed class RegularLesson
{
    public required LessonData Lesson;
    public required RegularLessonDate Date;
}

public sealed class OneTimeLesson
{
    public required LessonData Lesson;
    public required OneTimeLessonDate Date;
}

public enum LessonType
{
    Unspecified,
    Lab,
    Curs,
    Custom,
}

public record struct SubGroupNumber(int Value)
{
    public static SubGroupNumber All => new(0);
}

public record struct GroupId(int Value)
{
}

public record struct TeacherId(int Id)
{
}

public enum QualificationType
{
    Licenta,
    Master,
    Doctor,
}

public enum Language
{
    Ro,
    Ru,
    En,
}

public record struct Faculty(string Name);
public record struct Specialty(string? Name);

public sealed class Group
{
    public required string Name;
    public required int Grade;
    public required QualificationType QualificationType;
    public required Faculty Faculty;
    public Specialty Specialty = default;
    public required Language Language;
}

public sealed class Teacher
{
    public required string Name;
}