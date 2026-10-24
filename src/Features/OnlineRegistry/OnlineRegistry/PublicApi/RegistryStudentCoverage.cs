using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry;

/// <summary>Aggregates roster coverage without treating additional-course lists as new attendance audiences.</summary>
internal sealed class RegistryStudentCoverage(StudentAttendanceList attendance,
    IReadOnlyDictionary<string, string>? aliases)
{
    private static readonly IEqualityComparer<Name> NameComparer = Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance;
    private readonly Dictionary<StudentsLookupKey, SourceCoverage> _sources = [];
    private readonly List<RosterCoverage> _rosters = [];

    internal void Observe(HtmlStudent[] students, IEnumerable<StudentsLookupKey> routedKeys,
        IEnumerable<StudentsLookupKey> membershipKeys)
    {
        foreach (var key in routedKeys.Distinct())
        {
            foreach (var source in attendance.StudentNameSources(key))
            {
                var audience = source.Key;
                if (!_sources.TryGetValue(audience, out var coverage))
                {
                    global::System.Collections.Generic.Dictionary<global::ScheduleLib.OnlineRegistry.StudentsLookupKey, global::ScheduleLib.OnlineRegistry.RegistryStudentCoverage.SourceCoverage> dictionary = _sources;
                    global::ScheduleLib.OnlineRegistry.StudentsLookupKey key2 = audience;
                    global::ScheduleLib.OnlineRegistry.RegistryStudentCoverage.SourceCoverage value = coverage = new();
                    dictionary.Add(key2, value);
                }

                coverage.Names.UnionWith(source.Names.Select(student => student.Key));
                coverage.Rosters.Add(students);
            }
        }

        var sources = new Dictionary<StudentsLookupKey, NamesInDb>();
        var complete = true;
        foreach (var key in membershipKeys.Distinct())
        {
            var matchingSources = attendance.StudentNameSources(key).ToArray();
            var coveredGroups = matchingSources.SelectMany(source => source.Key.Groups).ToHashSet();
            bool CheckCondition()
            {
                if (matchingSources.Length == 0)
                {
                    return true;
                }

                if (matchingSources.Any(source => source.Names.Count == 0))
                {
                    return true;
                }

                if (key.Groups.Any(parent => !coveredGroups.Contains(parent)))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
            {
                complete = false;
            }

            foreach (var source in matchingSources)
            {
                var audience = source.Key;
                global::System.Collections.Generic.Dictionary<global::ScheduleLib.OnlineRegistry.StudentsLookupKey, global::ScheduleLib.OnlineRegistry.NamesInDb> dictionary2 = sources;
                global::ScheduleLib.OnlineRegistry.StudentsLookupKey key3 = audience;
                global::ScheduleLib.OnlineRegistry.NamesInDb namesInDb;
                if (sources.TryGetValue(audience, out var known))
                {
                    global::ScheduleLib.OnlineRegistry.NamesInDb create = NamesInDb.Create([.. known.Concat(source.Names).Select(student => student.Key).Distinct(NameComparer)]);
                    namesInDb = create;
                }
                else
                {
                    namesInDb = source.Names;
                }

                dictionary2[key3] = namesInDb;
            }
        }
        global::System.Collections.Generic.List<global::ScheduleLib.OnlineRegistry.RegistryStudentCoverage.RosterCoverage> list = _rosters;
        global::ScheduleLib.OnlineRegistry.RegistryStudentCoverage.RosterCoverage item = new(students, sources, complete);
        list.Add(item);
    }

    // Called only after every relevant list for the course was processed successfully.
    internal void Report(Schedule schedule, CourseId course, RegistrySyncDiagnostics? diagnostics)
    {
        if (diagnostics is null)
        {
            return;
        }

        foreach (var (key, source) in _sources)
        {
            var names = NamesInDb.Create([.. source.Names]);
            foreach (var roster in source.Rosters)
            {
                _ = StudentNameRemapHelper.Create(roster, names, [], aliases, source.Matched);
            }

            var missing = source.Names.Count(student => !source.Matched.Contains(student));
            if (missing == 0)
            {
                continue;
            }

            string CalculateGroup()
            {
                string group2 = key.Groups.ToString(schedule);
                return group2;
            }

            diagnostics.Add(kind: "attendance-students-not-in-registry", course: schedule.Get(course).FullName,
                            @group: CalculateGroup(),
                            detail: $"{missing} attendance students have no matching student in any relevant registry roster for {key.LessonType} ({RegistrySyncDiagnostics.DescribePartition(key.GroupPartition)}). Check enrollment and name mappings.");
        }

        // Related lists share one diagnostic, while each roster is checked only against
        // its own membership sources. A combined list must not validate a name in
        // an unrelated parent-only list through a transitive source connection.
        var remaining = _rosters.ToList();
        while (remaining.Count != 0)
        {
            var component = new List<RosterCoverage> { remaining[0] };
            remaining.RemoveAt(0);
            var sources = new Dictionary<StudentsLookupKey, NamesInDb>(component[0].Sources);
            for (var index = 0; index < component.Count; index++)
            {
                foreach (var roster in remaining.Where(roster => roster.Sources.Keys.Any(sources.ContainsKey)).ToArray())
                {
                    component.Add(roster);
                    remaining.Remove(roster);
                    foreach (var source in roster.Sources)
                    {
                        sources.TryAdd(source.Key, source.Value);
                    }
                }
            }
            var unknown = new List<Name>();
            foreach (var roster in component.Where(roster => { if (!(roster.Complete)) { return false; } if (!(roster.Sources.Count != 0)) { return false; } return true; }))
            {
                var names = NamesInDb.Create([.. roster.Sources.SelectMany(source => { global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Parsing.Name> select = source.Value.Select(student => student.Key); global::System.Collections.Generic.HashSet<global::ScheduleLib.Parsing.Name> second; if (_sources.TryGetValue(source.Key, out var coverage)) { second = coverage.Names; } else { second = []; } global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Parsing.Name> second2 = second; global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Parsing.Name> concat = select.Concat(
                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                     second2); return concat; }).Distinct(NameComparer)]);
                _ = StudentNameRemapHelper.Create(roster.Students, names, unknown, aliases);
            }
            var count = unknown.Distinct(NameComparer).Count();
            if (count == 0)
            {
                continue;
            }

            var groups = string.Join(", ", sources.Keys.SelectMany(key => key.Groups).Distinct()
                .Select(parent => schedule.Get(parent).Name).Order(StringComparer.Ordinal));
            var partitions = string.Join("; ", sources.Keys.Select(key => RegistrySyncDiagnostics.DescribePartition(key.GroupPartition))
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
            var types = string.Join(", ", sources.Keys.Select(key => key.LessonType).Distinct().Order());
            diagnostics.Add(kind: "unmatched-attendance-students", course: schedule.Get(course).FullName,
                @group: groups,
                detail: $"{count} registry students have no matching attendance row after all relevant rosters were considered for {types} ({partitions}). Check attendance worksheets and name mappings.");
        }
    }

    private sealed class SourceCoverage
    {
        internal HashSet<Name> Names { get; } = new(NameComparer);
        internal HashSet<Name> Matched { get; } = new(NameComparer);
        internal HashSet<HtmlStudent[]> Rosters { get; } = [];
    }

    private sealed record RosterCoverage(HtmlStudent[] Students, Dictionary<StudentsLookupKey, NamesInDb> Sources, bool Complete);
}
