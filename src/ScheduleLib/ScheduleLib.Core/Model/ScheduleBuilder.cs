using System.Collections.Immutable;
using System.Diagnostics;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Builders;

public enum ValidationMode
{
    Strict,
    None,
    AttemptAutoFix,
}

public enum SubGroupValidationMode
{
    Strict = ValidationMode.Strict,
    None = ValidationMode.None,
    PossiblyRegisterSubGroup = ValidationMode.AttemptAutoFix,
}

public sealed class ValidationSettings()
{
    public SubGroupValidationMode SubGroup = SubGroupValidationMode.Strict;
}

// TODO: Maybe add versioning for caching.
public sealed partial class ScheduleBuilder()
{
    public Remappings Remappings = new();
    public SpecializationRegistry? SpecializationRegistry;
    public ImplicitSplitConfig? ImplicitSplitConfig;
    public LessonOverlapValidationConfig? OverlapValidationConfig;
    public ListBuilder<OneTimeLessonBuilderModel> OneTimeLessons = new();
    public ListBuilder<Course> Courses = new();
    public ValidationSettings ValidationSettings = new();

    public GroupParseContext? GroupParseContext
    {
        get => _groupParseContext;
        set
        {
            _groupParseContext = value;
            GroupParseContextIsExplicit = value is not null;
        }
    }

    private GroupParseContext? _groupParseContext;

    /// <summary>
    /// A clock-derived year silently stops matching year-pinned implicit split scopes once the clock
    /// moves past them, so <c>AssignImplicitSplits</c> fails loudly when <see cref="GroupParseContext"/>
    /// was not supplied explicitly.
    /// </summary>
    internal bool GroupParseContextIsExplicit { get; private set; }

    internal void SetDefaultGroupParseContext(GroupParseContext context)
    {
        _groupParseContext = context;
        GroupParseContextIsExplicit = false;
    }

    public static Schedule Create(Action<ScheduleBuilder> builder)
    {
        var r = new ScheduleBuilder();
        builder(r);
        var ret = r.Build();
        return ret;
    }
}

public static partial class ScheduleBuilderHelper
{
    public static T Build<T>(this ScheduleBuilder s, Func<ScheduleBuilder, T> builder)
    {
        s.Preprocess();
        s.Validate();
        s.ClassifySubGroups();
        s.DropEngSubGroupFromEnglishGroups();
        s.AssignImplicitSplits();
        s.NormalizeLanguageProficiency();
        s.ValidateLessonOverlaps();
        s.SanityChecks();
        var ret = builder(s);
        return ret;
    }

    public static Schedule Build(this ScheduleBuilder s)
    {
        var ret = s.Build(CreateDefaultModel);
        return ret;
    }

    private static void Preprocess(this ScheduleBuilder b)
    {
        if (b.LookupModule is not { } lookup)
        {
            return;
        }

        var courseNamesByKey = lookup.Courses
            .GroupBy(x => x.Value)
            .Select(x =>
            {
                var names = x.Select(x1 => x1.Key)
                    .OrderByDescending(static a => a.Length);
                return (x.Key, Names: names.ToImmutableArray());
            });

        foreach (var t in courseNamesByKey)
        {
            b.Courses.Ref(t.Key.Id) = new(t.Names);
        }
    }

    private static void Validate(this ScheduleBuilder s)
    {
        GroupBuilderHelper.ValidateGroups(s);
        LessonBuilderHelper.ValidateLessons(s);
        TeacherBuilderHelper.ValidateTeachers(s);
        PeriodBuilderHelper.ValidatePeriods(s);
    }

    internal static void SanityChecks(this ScheduleBuilder s)
    {
        if (s.ValidationSettings.SubGroup == SubGroupValidationMode.None)
        {
            return;
        }

        foreach (var lesson in s.WeeklyLessons.List)
        {
            ValidateSubGroup(lesson.Base.Group.SubGroup, lesson.Base.Group.Groups);
        }
        foreach (var lesson in s.OneTimeLessons.List)
        {
            ValidateSubGroup(lesson.Base.Group.SubGroup, lesson.Base.Group.Groups);
        }

        s.CheckNumericSubGroupsAreContiguous();
        s.CheckLanguageSubGroupCount();

        void ValidateSubGroup(SubGroup subGroup, in LessonGroups groups)
        {
            if (subGroup == SubGroup.All)
            {
                return;
            }
            if (SpecialSubGroups.AllSpecial.Contains(subGroup))
            {
                return;
            }
            if (NumberHelper.FromRoman(subGroup.Value) is not null)
            {
                return;
            }

            string groupContext;
            if (groups.Count == 0)
            {
                groupContext = "no group";
            }
            else
            {
                groupContext = string.Join("; ", groups.Select(DescribeGroup));
            }

            string registryContext;
            if (s.SpecializationRegistry is { } registry)
            {
                registryContext = string.Join(
                    "; ",
                    groups.Select(groupId => DescribeRegistryContext(registry, groupId)));
            }
            else
            {
                registryContext = "no specialization registry configured";
            }
            throw UnknownSubGroupException.ForInvalid(
                subGroupValue: subGroup.Value,
                groupContext: groupContext,
                registryContext: registryContext);
        }

        string DescribeGroup(GroupId id)
        {
            if (id.Value < 0 || id.Value >= s.Groups.List.Count)
            {
                return id.Value.ToString();
            }
            var group = s.Groups.List[id.Value];
            return
                $"{$"{group.Name} (grade {group.Grade.Value}, faculty {group.Faculty.Name}, "}{$"attendance {group.AttendanceMode}, qualification {group.QualificationType})"}";
        }

        string DescribeRegistryContext(SpecializationRegistry registry, GroupId id)
        {
            if (id.Value < 0 || id.Value >= s.Groups.List.Count)
            {
                return $"{id.Value} => unavailable group";
            }
            var group = s.Groups.List[id.Value];
            return $"{group.Name} => {registry.DescribeMatches(group.Category)}";
        }
    }

    private static Schedule CreateDefaultModel(ScheduleBuilder s)
    {
        LessonBase BuildBase(in LessonBuilderModelDataBase x)
        {
            var groups = x.Group.Groups.Ordered();
            ImmutableArray<TeacherId> teachers = [.. x.General.Teachers];
            var data = new LessonData(
                groups: groups,
                course: x.General.Course!.Value,
                teachers: teachers,
                room: x.General.Room,
                type: x.General.Type,
                subGroup: x.Group.SubGroup,
                specialization: x.Group.Specialization,
                alternative: x.Group.Alternative);
            return new(data);
        }

        var weeklyLessons = s.WeeklyLessons.Build(x =>
        {
            var lessonBase = BuildBase(x.Base);
            var date = new WeeklyLessonDate(
                dayOfWeek: x.Date.DayOfWeek!.Value,
                timeSlot: x.Date.TimeSlot!.Value,
                period: x.General.Period,
                parity: x.Date.Parity ?? Parity.EveryWeek);
            return new WeeklyLesson(lessonBase, date);
        });
        var oneTimeLessons = s.OneTimeLessons.Build(x =>
        {
            var lessonBase = BuildBase(x.Base);
            var date = new OneTimeLessonDate(
                date: x.Date.Date!.Value,
                timeSlot: x.Date.TimeSlot!.Value);
            return new OneTimeLesson(lessonBase, date);
        });
        var groups = s.Groups.Build(x => x.Build());
        var teachers = s.Teachers.Build(x =>
        {
            // The imported name parts, which matching relies on; displays derive initials.
            var personName = new PersonName(x.Name.FirstName, x.Name.LastName);
            var contacts = x.Contacts.ToPersonContacts();
            return new Teacher(personName, contacts);
        });
        var courses = s.Courses.Build();

        var periods = s.Periods.Build(x =>
        {
            var ret = new Period(start: x.Start, end: x.EndExclusive);
            return ret;
        });

        return new Schedule(
            weeklyLessons: weeklyLessons,
            oneTimeLessons: oneTimeLessons,
            groups: groups,
            teachers: teachers,
            courses: courses,
            periods: periods);
    }

    public static CourseId Course(this ScheduleBuilder s, params ImmutableArray<string> names)
    {
        Debug.Assert(names.Length > 0, "Must provide a course name");

        {
            if (s.LookupModule is { } lookupModule)
            {
                if (lookupModule.Courses.TryGetValue(names[0], out var val))
                {
                    return val;
                }
            }
        }

        var r = s.Courses.New();
        r.Value = new(names);

        {
            if (s.LookupModule is { } lookupModule)
            {
                foreach (var name in names)
                {
                    // Let it throw on duplicates here for now.
                    lookupModule.Courses.Add(name, new(r.Id));
                }
                UpdateLookupAfterCourseAdded(s);
            }
        }

        return new(r.Id);
    }

    // Obviously pretty bad code.
    // Gonna need to introduce some more abstraction later.
    internal static void UpdateLookupAfterCourseAdded(ScheduleBuilder s)
    {
        if (s.LookupModule is { } lookupModule)
        {
            lookupModule.LessonsByCourse.Add([]);
        }
    }

    public static RoomId Room(this ScheduleBuilder s, string id)
    {
        _ = s;
        return new(id);
    }

    /// <summary>Remappings apply as teachers are added, so they must be chosen first.</summary>
    public static void UseRemappings(this ScheduleBuilder s, Remappings remappings)
    {
        if (s.Teachers.Count > 0)
        {
            throw new RemappingsConfiguredAfterTeachersAddedException(s.Teachers.Count);
        }
        s.Remappings = remappings;
    }
}
