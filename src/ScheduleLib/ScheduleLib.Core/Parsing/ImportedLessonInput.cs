using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing;

/// <summary>A parsed PDF/Word lesson and its resolved cell coordinates and audience.</summary>
public readonly record struct ImportedLessonInput(
    ParsedLesson Lesson,
    DayOfWeek Day,
    TimeSlot TimeSlot,
    IReadOnlyList<GroupId> Groups,
    Alternative Alternative = default);
