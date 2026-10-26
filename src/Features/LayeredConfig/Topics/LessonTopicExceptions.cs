namespace ScheduleLib.Application.Core.Topics;

public sealed class NoLessonNameProviderForLessonTypeException(LessonType lessonType, string courses)
    : InvalidOperationException($"No provider for lesson type {lessonType} for courses {courses}.")
{
    public LessonType LessonType { get; } = lessonType;
    public string Courses { get; } = courses;
}
