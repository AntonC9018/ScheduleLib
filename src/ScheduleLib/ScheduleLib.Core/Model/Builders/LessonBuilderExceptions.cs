namespace ScheduleLib.Builders;

public sealed class MaximumLessonGroupCountException(int maximumGroupCount)
    : ArgumentException($"The maximum number of groups is {maximumGroupCount}.")
{
    public int MaximumGroupCount { get; } = maximumGroupCount;
}
