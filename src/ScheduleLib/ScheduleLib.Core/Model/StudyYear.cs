namespace ScheduleLib;

/// <summary>
/// The year an academic year starts in, e.g. 2026 for the 2026/2027 year.
/// Group labels only carry the year modulo 100, so the full year is enforced at
/// creation: a two-digit value would silently corrupt grade determination.
/// </summary>
public readonly record struct StudyYear : IComparable<StudyYear>
{
    public int Value { get; }

    public StudyYear(int value)
    {
        if (value is < 1900 or > 2999)
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value,
                "The study year must be the full year, e.g. 2026, not lower than 1900.");
        }
        Value = value;
    }

    /// <summary>
    /// The two-digit form the group labels use.
    /// </summary>
    public int Mod100 => Value % 100;

    public int CompareTo(StudyYear other) => Value.CompareTo(other.Value);

    public override string ToString() => Value.ToString();
}
