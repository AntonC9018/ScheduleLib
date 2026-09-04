using AutoConstructor.Attributes;

namespace ScheduleLib.Builders;

[AutoConstructor]
public sealed partial class SubGroupNameRemapper
{
    private readonly ScheduleBuilder _builder;
    private SubGroupNameRemappings Remappings => _builder.Remappings.SubGroupNameRemappings;
    public SubGroup Remap(SubGroup subGroup) => Remappings.Remap(subGroup);
    public SubGroup Remap(ReadOnlyMemory<char> subGroup) => Remappings.Remap(subGroup);
    public SubGroup Remap(ReadOnlySpan<char> subGroup) => Remappings.Remap(subGroup);
    public SubGroup? TryRemapName(ReadOnlySpan<char> subGroup) => Remappings.TryRemapName(subGroup);
}

public static class SubGroupNameRemapperExtensions
{
    public static ReadOnlyMemory<char> RemapName(this SubGroupNameRemapper r, ReadOnlyMemory<char> mem)
    {
        if (r.TryRemapName(mem.Span) is { } remapped)
        {
            return remapped.Value.AsMemory();
        }
        return mem;
    }
}
