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
    public readonly Dictionary<string, string> TeacherLastNameRemappings = new(IgnoreDiacriticsAndCaseComparer.Instance);
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
    public static Schedule Build(this ScheduleBuilder s)
    {
        s.Validate();

        var regularLessons = s.RegularLessons.Build(x =>
        {
            var ret = new RegularLesson
            {
                Date = new()
                {
                    TimeSlot = x.Date.TimeSlot!.Value,
                    DayOfWeek = x.Date.DayOfWeek!.Value,
                    Parity = x.Date.Parity ?? Parity.EveryWeek,
                },
                Lesson = new()
                {
                    Period = x.General.Period,
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
                    Name = x.Name.Name.Map(x1 => x1 with
                    {
                        Short = ShortFirstName(x1),
                    }),
                    LastName = x.Name.LastName!,
                },
            };
            return ret;

            static string? ShortFirstName(OptionalFirstNamePart x)
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

    public static void Validate(this ScheduleBuilder s)
    {
        GroupBuilderHelper.ValidateGroups(s);
        LessonBuilderHelper.ValidateLessons(s);
        TeacherBuilderHelper.ValidateTeachers(s);
        PeriodBuilderHelper.ValidatePeriods(s);
    }

    public static CourseId Course(this ScheduleBuilder s, params string[] names)
    {
        Debug.Assert(names.Length > 0, "Must provide a course name");

        {
            if (s.LookupModule is { } lookupModule)
            {
                if (lookupModule.Courses.TryGetValue(names[0], out int val))
                {
                    return new(val);
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
                    lookupModule.Courses.Add(name, r.Id);
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

    public static string RemapTeacherName(this ScheduleBuilder s, string lastName)
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
