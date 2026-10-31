namespace ScheduleLib.Helper;

public sealed class EnumMembersNotSetException<TEnum>(EnumBitArray<TEnum> missingMembers)
    : InvalidOperationException($"Some members not set: {missingMembers}")
    where TEnum : struct, Enum
{
    public EnumBitArray<TEnum> MissingMembers { get; } = missingMembers;
}
