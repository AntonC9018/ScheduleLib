namespace ScheduleLib.Builders;

public sealed class Remappings()
{
    public readonly TeacherLastNameRemappings TeacherLastNameRemappings = new();
    public readonly TeacherFullNameRemappings TeacherFullNameRemappings = new();
    public readonly SubGroupNameRemappings SubGroupNameRemappings = new();
}

public sealed class SubGroupNameRemappings : Dictionary<string, string>
{
    public void Add(string from, Specialization to) => Add(from, to.Value ?? throw new ArgumentException("The specialization must not be All.", nameof(to)));
    public void Add(string from, Alternative to) => Add(from, to.Value ?? throw new ArgumentException("The alternative must not be All.", nameof(to)));

    public SubGroup Remap(SubGroup subGroup)
    {
        if (subGroup.Value is null)
        {
            return subGroup;
        }
        return Remap(subGroup.Value.AsSpan());
    }

    public SubGroup Remap(ReadOnlyMemory<char> mem) => Remap(mem.Span);

    public SubGroup Remap(ReadOnlySpan<char> mem)
    {
        if (TryRemapName(mem) is { } remapped)
        {
            return remapped;
        }
        return new(mem.ToString());
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

public delegate bool TryChange(ref TeacherBuilderModel.NameModel m);

public sealed class TeacherFullNameRemappings : List<TryChange>
{
    public TeacherFullNameRemappings() : base()
    {
    }
}

public sealed class TeacherLastNameRemappings : Dictionary<NameParts<string?>, LastName>
{
    public TeacherLastNameRemappings() : base(IgnoreDiacriticsAndCase_Name_Comparer.Instance)
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

