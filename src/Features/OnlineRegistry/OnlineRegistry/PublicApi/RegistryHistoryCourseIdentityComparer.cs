namespace ScheduleLib.OnlineRegistry;

/// <summary>Course matching identity: semester and normalized ordinal-insensitive name.</summary>
public sealed class RegistryHistoryCourseIdentityComparer : IEqualityComparer<RegistryHistoryCourse>
{
    public static readonly RegistryHistoryCourseIdentityComparer Instance = new();

    private RegistryHistoryCourseIdentityComparer()
    {
    }

    public bool Equals(RegistryHistoryCourse? x, RegistryHistoryCourse? y)
    {
        if (ReferenceEquals(objA: x, objB: y))
        {
            return true;
        }
        if (x is null)
        {
            return false;
        }
        if (y is null)
        {
            return false;
        }
        if (x.Semester != y.Semester)
        {
            return false;
        }
        var xName = RegistryHistoryTopicSelection.NormalizeCourseName(x.Name);
        var yName = RegistryHistoryTopicSelection.NormalizeCourseName(y.Name);
        if (!StringComparer.OrdinalIgnoreCase.Equals(xName, yName))
        {
            return false;
        }
        return true;
    }

    public int GetHashCode(RegistryHistoryCourse course)
    {
        var name = RegistryHistoryTopicSelection.NormalizeCourseName(course.Name);
        var nameHash = StringComparer.OrdinalIgnoreCase.GetHashCode(name);
        return HashCode.Combine(value1: course.Semester, value2: nameHash);
    }
}
