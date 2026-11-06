namespace ScheduleLib.Helper.JsonConverters;

public sealed class OutputOnlyJsonReadException(Type targetType)
    : Exception($"{targetType.Name} is written to output only.")
{
    public Type TargetType { get; } = targetType;
}
