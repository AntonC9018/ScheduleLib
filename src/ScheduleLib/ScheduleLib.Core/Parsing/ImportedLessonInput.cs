using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing;

public readonly record struct ImportedLessonInput(
    ParsedLesson Lesson,
    DayOfWeek Day,
    TimeSlot TimeSlot,
    IReadOnlyList<GroupId> Groups,
    Alternative Alternative = default);
