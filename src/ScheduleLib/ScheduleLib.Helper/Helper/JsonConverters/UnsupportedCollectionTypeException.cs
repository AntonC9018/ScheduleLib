namespace ScheduleLib.Helper.JsonConverters;

public sealed class UnsupportedCollectionTypeException(Type targetType)
    : NotSupportedException($"Collection type {targetType} is not supported.")
{
    public Type TargetType { get; } = targetType;
}
