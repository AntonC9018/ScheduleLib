using ScheduleLib.Parsing;

namespace FmiWebsiteInterop.Teachers;

public sealed class TeacherDataProviderAlreadyInitializedException()
    : InvalidOperationException("Tried to initialize the provider a second time!");

public sealed class ConflictingTeacherSlugException(
    Name teacherName,
    string existingSlug,
    string newSlug)
    : InvalidOperationException(
        $"Slugs don't match for duplicate teacher '{teacherName}': '{existingSlug}' vs '{newSlug}'.")
{
    public Name TeacherName { get; } = teacherName;
    public string ExistingSlug { get; } = existingSlug;
    public string NewSlug { get; } = newSlug;
}
