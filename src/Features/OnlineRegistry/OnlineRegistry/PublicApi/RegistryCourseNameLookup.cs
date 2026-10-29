using System.Text;
using ScheduleLib.Builders;

namespace ScheduleLib.OnlineRegistry;

public static class RegistryCourseNameLookup
{
    /// <summary>A registry course display name split into the course name and its elective classifications.</summary>
    public readonly record struct ParsedRegistryCourseName(string Name, IReadOnlyList<string> ElectiveClassifications);

    public static ParsedRegistryCourseName Parse(string registryName)
    {
        var name = registryName.Trim();
        var start = name.LastIndexOf('(');
        if (start < 0)
        {
            return new(name, []);
        }

        if (!name.EndsWith(')'))
        {
            return new(name, []);
        }

        var classifications = name[(start + 1)..^1].Split('&', StringSplitOptions.TrimEntries);
        if (!classifications.All(value => value is "BaDe" or "FrDe"))
        {
            return new(name, []);
        }

        return new(name[..start].TrimEnd(), classifications);
    }

    /// <summary>A course name reduced to its comparison form, with the course it belongs to.</summary>
    private readonly record struct NormalizedCourseName(string Name, CourseId Id);

    /// <summary>Explicit aliases override lookup. Approximate matches must identify exactly one course.</summary>
    public static CourseId? Find(
        LookupFacade lookup,
        string registryName,
        IReadOnlyDictionary<string, string> courseNames,
        int maxDistance = 2)
    {
        if (maxDistance < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance));
        }

        var parsed = Parse(registryName);
        if (TryAlias(
                courseNames,
                registryName: registryName,
                parsedName: parsed.Name,
                mappedName: out var mappedName))
        {
            var result = lookup.Course(
                mappedName.AsMemory(),
                new()
                {
                    IgnorePunctuation = true,
                });
            if (result is null)
            {
                return null;
            }

            return result;
        }

        if (lookup.Course(
                parsed.Name.AsMemory(),
                new()
                {
                    IgnorePunctuation = true,
                }) is { } exact)
        {
            return exact;
        }
        // An unknown annotation can distinguish courses; never discard it through fuzzy matching.
        if (parsed.Name.Contains('(') || parsed.Name.Contains(')'))
        {
            return null;
        }

        var normalized = Normalize(parsed.Name);
        var candidates = lookup.LookupModule.Courses
            .Select(course =>
            {
                var normalizedCourseName = Normalize(course.Key);
                return new NormalizedCourseName(normalizedCourseName, course.Value);
            })
            .ToArray();
        var normalizedMatches = candidates.Where(course => course.Name == normalized)
            .Select(course => course.Id)
            .Distinct()
            .ToArray();
        if (normalizedMatches.Length != 0)
        {
            return normalizedMatches.Length == 1 ? normalizedMatches[0] : null;
        }

        bool CalculateConditionResult()
        {
            if (normalized.Length < 12)
            {
                return true;
            }

            if (maxDistance == 0)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            return null;
        }

        var tolerance = Math.Min(maxDistance, normalized.Length / 10);
        var approximate = candidates.Where(course =>
            {
                bool CheckCondition()
                {
                    if (!(course.Name.Length >= 12))
                    {
                        return false;
                    }

                    if (!(Math.Abs(course.Name.Length - normalized.Length) <= tolerance))
                    {
                        return false;
                    }

                    if (Distance(first: normalized, second: course.Name) <= tolerance)
                    {
                        return true;
                    }

                    return false;
                }

                return CheckCondition();
            })
            .Select(course => course.Id)
            .Distinct()
            .ToArray();
        return approximate.Length == 1 ? approximate[0] : null;
    }

    private static bool TryAlias(
        IReadOnlyDictionary<string, string> courseNames,
        string registryName,
        string parsedName,
        out string mappedName)
    {
        return courseNames.TryGetValue(registryName, out mappedName!)
            || courseNames.TryGetValue(parsedName, out mappedName!);
    }

    private static string Normalize(string name)
    {
        string normalize = name.Normalize(NormalizationForm.FormD);
        Func<char, bool> predicate = char.IsLetterOrDigit;
        var where = normalize.Where(predicate);
        Func<char, char> selector = char.ToLowerInvariant;
        var select = where.Select(selector);
        char[] value = select.ToArray();
        string text = new(value);
        return text;
    }

    private static int Distance(string first, string second)
    {
        var previous = Enumerable.Range(start: 0, count: second.Length + 1)
            .ToArray();
        var current = new int[second.Length + 1];
        for (var i = 1; i <= first.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= second.Length; j++)
            {
                int[] value = current;
                int int32 = j;
                int[] value2 = current;
                int int322 = j - 1;
                int val1 = value2[int322] + 1;
                int val2 = previous[j] + 1;
                int val12 = Math.Min(val1, val2);
                int[] value3 = previous;
                int int323 = j - 1;
                int int324 = value3[int323];
                string text = first;
                int index = i - 1;
                int charValue = text[index];
                string text2 = second;
                int index2 = j - 1;
                int int325;
                if (charValue == text2[index2])
                {
                    int325 = 0;
                }
                else
                {
                    int325 = 1;
                }

                int val22 = int324 + int325;
                int min = Math.Min(val12, val22);
                value[int32] = min;
            }

            (previous, current) = (current, previous);
        }
        return previous[second.Length];
    }
}
