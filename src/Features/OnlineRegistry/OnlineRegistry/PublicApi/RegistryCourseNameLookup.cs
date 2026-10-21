using System.Text;
using ScheduleLib.Builders;

namespace ScheduleLib.OnlineRegistry;

public static class RegistryCourseNameLookup
{
    public static (string Name, IReadOnlyList<string> ElectiveClassifications) Parse(string registryName)
    {
        var name = registryName.Trim();
        var start = name.LastIndexOf('(');
        if (start < 0 || !name.EndsWith(')')) return (name, []);
        var classifications = name[(start + 1)..^1].Split('&', StringSplitOptions.TrimEntries);
        if (!classifications.All(value => value is "BaDe" or "FrDe")) return (name, []);
        return (name[..start].TrimEnd(), classifications);
    }

    /// <summary>Explicit aliases override lookup. Approximate matches must identify exactly one course.</summary>
    public static CourseId? Find(LookupFacade lookup, string registryName,
        IReadOnlyDictionary<string, string> courseNames, int maxDistance = 2)
    {
        if (maxDistance < 0) throw new ArgumentOutOfRangeException(nameof(maxDistance));
        var parsed = Parse(registryName);
        if (TryAlias(courseNames, registryName, parsed.Name, out var mappedName))
            return lookup.Course(mappedName.AsMemory(), new() { IgnorePunctuation = true });
        if (lookup.Course(parsed.Name.AsMemory(), new() { IgnorePunctuation = true }) is { } exact)
            return exact;
        // An unknown annotation can distinguish courses; never discard it through fuzzy matching.
        if (parsed.Name.Contains('(') || parsed.Name.Contains(')')) return null;
        var normalized = Normalize(parsed.Name);
        var candidates = lookup.LookupModule.Courses
            .Select(course => (Name: Normalize(course.Key), Id: course.Value)).ToArray();
        var normalizedMatches = candidates.Where(course => course.Name == normalized)
            .Select(course => course.Id).Distinct().ToArray();
        if (normalizedMatches.Length != 0)
            return normalizedMatches.Length == 1 ? normalizedMatches[0] : null;
        if (normalized.Length < 12 || maxDistance == 0) return null;
        var tolerance = Math.Min(maxDistance, normalized.Length / 10);
        var approximate = candidates.Where(course => course.Name.Length >= 12
                && Math.Abs(course.Name.Length - normalized.Length) <= tolerance
                && Distance(normalized, course.Name) <= tolerance)
            .Select(course => course.Id).Distinct().ToArray();
        return approximate.Length == 1 ? approximate[0] : null;
    }

    private static bool TryAlias(IReadOnlyDictionary<string, string> courseNames,
        string registryName, string parsedName, out string mappedName)
    {
        return courseNames.TryGetValue(registryName, out mappedName!)
            || courseNames.TryGetValue(parsedName, out mappedName!);
    }

    private static string Normalize(string name) => new(name.Normalize(NormalizationForm.FormD)
        .Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static int Distance(string first, string second)
    {
        var previous = Enumerable.Range(0, second.Length + 1).ToArray();
        var current = new int[second.Length + 1];
        for (var i = 1; i <= first.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= second.Length; j++)
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (first[i - 1] == second[j - 1] ? 0 : 1));
            (previous, current) = (current, previous);
        }
        return previous[second.Length];
    }
}
