namespace ScheduleLib.Builders;

public sealed class Remappings()
{
    public readonly TeacherLastNameRemappings TeacherLastNameRemappings = new();
    public readonly TeacherFullNameRemappings TeacherFullNameRemappings = new();
    public readonly SubGroupNameRemappings SubGroupNameRemappings = new();
    /// <summary>Explicit aliases between canonical group codes, without language suffixes.</summary>
    public readonly Dictionary<string, string> GroupNameRemappings = new(StringComparer.Ordinal);
}

public sealed class SubGroupNameRemappings : Dictionary<string, string>
{
    public AliasMapping ForAlias(string alias)
    {
        ArgumentNullException.ThrowIfNull(alias);
        return new(this, alias);
    }

    public readonly struct AliasMapping
    {
        private readonly SubGroupNameRemappings? _owner;
        private readonly string? _alias;

        internal AliasMapping(SubGroupNameRemappings owner, string alias)
        {
            _owner = owner;
            _alias = alias;
        }

        public void To(Specialization value)
        {
            var destination = value.Value;
            if (destination is null)
            {
                throw new ArgumentException("The specialization must not be All.", nameof(value));
            }
            AddDestination(destination);
        }

        public void To(Alternative value)
        {
            var destination = value.Value;
            if (destination is null)
            {
                throw new ArgumentException("The alternative must not be All.", nameof(value));
            }
            AddDestination(destination);
        }

        private void AddDestination(string destination)
        {
            if (_owner is null)
            {
                throw new UnselectedSubGroupAliasException();
            }
            _owner.Add(key: _alias!, value: destination);
        }
    }

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

public sealed class UnselectedSubGroupAliasException : ScheduleBuildException
{
    public UnselectedSubGroupAliasException()
        : base("Select an alias with ForAlias before setting its destination.")
    {
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
