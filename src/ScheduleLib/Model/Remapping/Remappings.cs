namespace ScheduleLib.Builders;

public sealed class Remappings()
{
    public readonly TeacherNameRemappings TeacherLastNameRemappings = new();
    public readonly SubGroupNameRemappings SubGroupNameRemappings = new();
}

public sealed class SubGroupNameRemappings : Dictionary<string, string>
{
    public SubGroup Remap(SubGroup subGroup)
    {
        var ret = this!.GetValueOrDefault(subGroup.Value, subGroup.Value);
        return new(ret);
    }

    public SubGroup? TryRemapName(ReadOnlySpan<char> mem)
    {
        if (mem.IsEmpty)
        {
            return null;
        }
        var alt = this.GetAlternateLookup<ReadOnlySpan<char>>();
        if (alt.TryGetValue(mem, out var remapped))
        {
            return new(remapped);
        }
        return null;
    }
}

public sealed class TeacherNameRemappings : Dictionary<NameParts<string?>, LastName>
{
    public TeacherNameRemappings() : base(IgnoreDiacriticsAndCase_Name_Comparer.Instance)
    {
    }

    public void Add(string a, string b)
    {
        var x = new NameParts<string?>();
        var y = x;
        x[0] = a;
        y[0] = b;
        this[x] = new(y);
    }
}

