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
    /// Whether <see cref="GroupParseContext"/> was supplied explicitly by the
    /// caller rather than defaulted from the wall clock in
    /// <c>GroupBuilderHelper.ParseGroup</c>. A clock-derived year silently stops
    /// matching year-pinned implicit split scopes once the clock moves past
    /// them, so <c>AssignImplicitSplits</c> fails loudly instead in that case.
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
        s.Validate();
        s.Preprocess();
        s.ClassifySubGroups();
        s.AssignImplicitSplits();
        s.NormalizeLanguageProficiency();
        s.ValidateLessonOverlaps();
        s.SanityChecks();
        var ret = builder(s);
        return ret;
    }

    public static Schedule Build(this ScheduleBuilder s)
    {
        var ret = Build(s, CreateDefaultModel);
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
                var names = x.Select(x1 => x1.Key).OrderByDescending(static a => a.Length);
                return (x.Key, Names: names.ToImmutableArray());
            });

        foreach (var t in courseNamesByKey)
        {
            ref var course = ref b.Courses.Ref(t.Key.Id);
            course.Names = t.Names;
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
            if (subGroup == SubGroup.All
                || SpecialSubGroups.AllSpecial.Contains(subGroup)
                || NumberHelper.FromRoman(subGroup.Value) is not null)
            {
                return;
            }

            var groupContext = groups.Count == 0
                ? "no group"
                : string.Join("; ", groups.Select(DescribeGroup));
            throw new InvalidOperationException(
                $"Invalid subgroup '{subGroup.Value}' for group '{groupContext}'. "
                + "Specialization registry context: "
                + (s.SpecializationRegistry is { } registry
                    ? string.Join("; ", groups.Select(groupId => DescribeRegistryContext(registry, groupId)))
                    : "no specialization registry configured")
                + ". "
                + "The configured subgroup selector accepts numeric values and configured special subgroups.");
        }

        string DescribeGroup(GroupId id)
        {
            if (id.Value < 0 || id.Value >= s.Groups.List.Count)
            {
                return id.Value.ToString();
            }
            var group = s.Groups.List[id.Value];
            return $"{group.Name} (grade {group.Grade.Value}, faculty {group.Faculty.Name}, "
                + $"attendance {group.AttendanceMode}, qualification {group.QualificationType})";
        }

        string DescribeRegistryContext(SpecializationRegistry registry, GroupId id)
        {
            if (id.Value < 0 || id.Value >= s.Groups.List.Count)
            {
                return $"{id.Value} => unavailable group";
            }
            var group = s.Groups.List[id.Value];
            return $"{group.Name} => {registry.DescribeMatches(in group)}";
        }
    }

    private static Schedule CreateDefaultModel(ScheduleBuilder s)
    {
        LessonBase BuildBase(in LessonBuilderModelDataBase x)
        {
            return new()
            {
                Data = new()
                {
                    Groups = x.Group.Groups.Ordered(),
                    SubGroup = x.Group.SubGroup,
                    Specialization = x.Group.Specialization,
                    Alternative = x.Group.Alternative,
                    Course = x.General.Course!.Value,
                    Room = x.General.Room,
                    Teachers = [.. x.General.Teachers],
                    Type = x.General.Type,
                },
            };
        }

        var weeklyLessons = s.WeeklyLessons.Build(x =>
        {
            var ret = new WeeklyLesson
            {
                Base = BuildBase(x.Base),
                Date = new()
                {
                    TimeSlot = x.Date.TimeSlot!.Value,
                    DayOfWeek = x.Date.DayOfWeek!.Value,
                    Parity = x.Date.Parity ?? Parity.EveryWeek,
                    Period = x.General.Period,
                },
            };
            return ret;
        });
        var oneTimeLessons = s.OneTimeLessons.Build(x =>
        {
            return new OneTimeLesson
            {
                Base = BuildBase(x.Base),
                Date = new()
                {
                    Date = x.Date.Date!.Value,
                    TimeSlot = x.Date.TimeSlot!.Value,
                },
            };
        });
        var groups = s.Groups.Build();
        var teachers = s.Teachers.Build(x =>
        {
            var ret = new Teacher
            {
                Contacts = x.Contacts,
                PersonName = new()
                {
                    FirstName = x.Name.FirstName.Map(x1 => x1 with
                    {
                        Short = ShortName(x1),
                    }),
                    LastName = x.Name.LastName,
                },
            };
            return ret;

            static string? ShortName(OptionalNamePart x)
            {
                if (x.Short is { } shortf)
                {
                    return shortf;
                }
                if (x.Full is { } fullf)
                {
                    return $"{fullf[0]}{WordHelper.ShortenedWordCharacter}";
                }
                return null;
            }
        });
        var courses = s.Courses.Build();

        var periods = s.Periods.Build(x =>
        {
            var ret = new Period(x.Start, x.EndExclusive);
            return ret;
        });

        return new Schedule
        {
            WeeklyLessons = weeklyLessons,
            OneTimeLessons = oneTimeLessons,
            Groups = groups,
            Teachers = teachers,
            Courses = courses,
            Periods = periods,
        };
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
        r.Value = new()
        {
            Names = names,
        };

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

    public static TeacherNameRemapStatus RemapTeacherName(this ScheduleBuilder s, ref TeacherBuilderModel.NameModel name)
    {
        var status = Impl(s, ref name);
        if (status != TeacherNameRemapStatus.None)
        {
            var status1 = Impl(s, ref name);
            if (status1 != TeacherNameRemapStatus.None)
            {
                // Maybe allow 1 recursion level?
                throw new InvalidOperationException(
                    "Recursive teacher name remaps are not supported to prevent errors. Ensure the remap maps to the final version.");
            }
        }
        return status;

        static TeacherNameRemapStatus Impl(ScheduleBuilder s, ref TeacherBuilderModel.NameModel name)
        {
            var status = TeacherNameRemapStatus.None;
            if (!name.LastName.IsNull
                && s.Remappings.TeacherLastNameRemappings.TryGetValue(name.LastName, out var t))
            {
                name.LastName = t;
                status = TeacherNameRemapStatus.LastName;
            }

            var copy = name;
            foreach (var x in s.Remappings.TeacherFullNameRemappings)
            {
                if (x(ref name))
                {
                    if (copy != name)
                    {
                        status = TeacherNameRemapStatus.FullName;
                    }
                    break;
                }
            }
            return status;
        }
    }

    internal static SubGroup RemapSubGroup(this ScheduleBuilder s, SubGroup subGroup)
    {
        if (subGroup == SubGroup.All)
        {
            return subGroup;
        }

        var val = subGroup.Value;
        Debug.Assert(val != null);
        var remapped = s.Remappings.SubGroupNameRemappings.GetValueOrDefault(val, val);
        return new(remapped);
    }

    public static void ConfigureRemappings(this ScheduleBuilder s, ConfigureRemappingsDelegate configure)
    {
        if (s.Teachers.Count > 0)
        {
            throw new NotSupportedException("Remapping may only be configured before adding teachers.");
        }
        configure(s.Remappings);
    }
}

public delegate void ConfigureRemappingsDelegate(Remappings remap);

public enum TeacherNameRemapStatus
{
    None,
    LastName,
    FullName,
}
