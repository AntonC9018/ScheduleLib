using System.Collections.Immutable;
using System.Diagnostics;
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

public sealed class Remappings()
{
    public readonly TeacherNameRemappings TeacherLastNameRemappings = new();
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

public sealed partial class ScheduleBuilder()
{
    public Remappings Remappings = new();
    public List<OneTimeLesson> OneTimeLessons = new();
    public ListBuilder<Course> Courses = new();
    public ValidationSettings ValidationSettings = new();

    public GroupParseContext? GroupParseContext;

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
        s.SanityChecks();
        var ret = builder(s);
        return ret;
    }

    public static Schedule Build(this ScheduleBuilder s)
    {
        var ret = Build(s, CreateDefaultModel);
        return ret;
    }

    public static void Preprocess(this ScheduleBuilder b)
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

    public static void Validate(this ScheduleBuilder s)
    {
        GroupBuilderHelper.ValidateGroups(s);
        LessonBuilderHelper.ValidateLessons(s);
        TeacherBuilderHelper.ValidateTeachers(s);
        PeriodBuilderHelper.ValidatePeriods(s);
    }

    public static void SanityChecks(this ScheduleBuilder s)
    {
        // TODO
        _ = s;
    }

    public static Schedule CreateDefaultModel(ScheduleBuilder s)
    {
        var regularLessons = s.RegularLessons.Build(x =>
        {
            var ret = new RegularLesson
            {
                Date = new()
                {
                    TimeSlot = x.Date.TimeSlot!.Value,
                    DayOfWeek = x.Date.DayOfWeek!.Value,
                    Parity = x.Date.Parity ?? Parity.EveryWeek,
                    Period = x.General.Period,
                },
                Lesson = new()
                {
                    Groups = x.Group.Groups,
                    SubGroup = x.Group.SubGroup,
                    Course = x.General.Course!.Value,
                    Room = x.General.Room,
                    Teachers = [.. x.General.Teachers],
                    Type = x.General.Type,
                },
            };
            return ret;
        });
        var oneTimeLessons = s.OneTimeLessons.ToImmutableArray();
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
            RegularLessons = regularLessons,
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
    public static void UpdateLookupAfterCourseAdded(ScheduleBuilder s)
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

    public static LastName RemapTeacherName(this ScheduleBuilder s, LastName lastName)
    {
        return s.Remappings.TeacherLastNameRemappings.GetValueOrDefault(lastName, lastName);
    }

    public static void ConfigureRemappings(this ScheduleBuilder s, Action<Remappings> configure)
    {
        if (s.Teachers.Count > 0)
        {
            throw new NotSupportedException("Remapping may only be configured before adding teachers.");
        }
        configure(s.Remappings);
    }

}
