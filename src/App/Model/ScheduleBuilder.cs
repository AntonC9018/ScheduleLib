using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace App;

public readonly struct ListBuilder<T>()
{
    public readonly List<T> List = new();

    public readonly ref struct AddResult
    {
        public readonly ref T Value;
        public readonly int Id;

        public AddResult(int id, ref T value)
        {
            Id = id;
            Value = ref value;
        }
    }

    // The reference is invalidated after another call.
    public AddResult New()
    {
        List.Add(default!);
        var span = CollectionsMarshal.AsSpan(List);
        return new(List.Count - 1, ref span[^1]);
    }

    public ImmutableArray<T> Build() => [.. List];
    public ImmutableArray<U> Build<U>(Func<T, U> f)
    {
        var builder = ImmutableArray.CreateBuilder<U>(List.Count);
        foreach (var item in List)
        {
            builder.Add(f(item));
        }
        return builder.ToImmutable();
    }

    public int Count => List.Count;

    public ref T Ref(int id) => ref CollectionsMarshal.AsSpan(List)[id];
}

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
    PossiblyIncreaseSubGroupCount = ValidationMode.AttemptAutoFix,
}

public sealed class ValidationSettings()
{
    public SubGroupValidationMode SubGroup = SubGroupValidationMode.Strict;
}

public sealed class ScheduleBuilder()
{
    public ListBuilder<RegularLessonBuilderModel> RegularLessons = new();
    public List<OneTimeLesson> OneTimeLessons = new();
    public ListBuilder<Group> Groups = new();
    public ListBuilder<Teacher> Teachers = new();
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

public struct GroupBuilder
{
    public required ScheduleBuilder Schedule { get; init; }
    public required GroupId Id { get; init; }

    public ref Group Ref => ref Schedule.Groups.Ref(Id.Value);
    public static implicit operator GroupId(GroupBuilder g) => g.Id;
}

public sealed class RegularLessonBuilderModel
{
    public GeneralData General = new();
    public RegularLessonDateBuilderModel Date = new();
    public GroupData Group = new();

    public struct GroupData()
    {
        public LessonGroups Groups = new();
        public SubGroupNumber SubGroup = SubGroupNumber.All;
    }

    public struct GeneralData()
    {
        public CourseId? Course;
        public TeacherId? Teacher;
        public RoomId? Room;
        public LessonType Type = LessonType.Unspecified;
    }

    public void CopyFrom(RegularLessonBuilderModel model)
    {
        General = model.General;
        Date = model.Date;
        Group = model.Group;
    }
}

public struct RegularLessonDateBuilderModel()
{
    public Parity? Parity;
    public DayOfWeek? DayOfWeek;
    public TimeSlot? TimeSlot;
}

public interface ILessonBuilder
{
    RegularLessonBuilderModel Model { get; }
}

public static class Helper1
{
    public static void DayOfWeek(this ILessonBuilder b, DayOfWeek dayOfWeek) => b.Model.Date.DayOfWeek = dayOfWeek;
    public static void TimeSlot(this ILessonBuilder b, TimeSlot timeSlot) => b.Model.Date.TimeSlot = timeSlot;
    public static void Parity(this ILessonBuilder b, Parity parity) => b.Model.Date.Parity = parity;

    public static void Date(this ILessonBuilder b, RegularLessonDateBuilderModel date)
    {
        if (date.Parity is { } p)
        {
            b.Model.Date.Parity = p;
        }
        if (date.DayOfWeek is { } d)
        {
            b.Model.Date.DayOfWeek = d;
        }
        if (date.TimeSlot is { } t)
        {
            b.Model.Date.TimeSlot = t;
        }
    }

    public static void Group(this ILessonBuilder b, GroupId group, SubGroupNumber? subGroup = null)
    {
        b.Model.Group.Groups = new()
        {
            Group0 = group,
        };
        b.Model.Group.SubGroup = subGroup ?? SubGroupNumber.All;
    }

    public static void Groups(this ILessonBuilder b, ReadOnlySpan<GroupId> groups)
    {
        if (groups.Length > 3)
        {
            throw new ArgumentException("The maximum number of groups is 3.");
        }
        if (groups.Length == 0)
        {
            throw new ArgumentException("At least one group must be specified.");
        }
        if (groups.Length != 1 && b.Model.Group.SubGroup != SubGroupNumber.All)
        {
            throw new ArgumentException("Subgroup can only be specified for a single group.");
        }

        var g = new LessonGroups();
        for (int i = 0; i < groups.Length; i++)
        {
            g[i] = groups[i];
        }
        b.Model.Group.Groups = g;
    }

    public static void Teacher(this ILessonBuilder b, TeacherId teacher) => b.Model.General.Teacher = teacher;
    public static void Room(this ILessonBuilder b, RoomId room) => b.Model.General.Room = room;
    public static void Type(this ILessonBuilder b, LessonType type) => b.Model.General.Type = type;
    public static void Course(this ILessonBuilder b, CourseId course) => b.Model.General.Course = course;
}

public sealed class RegularLessonBuilder : ILessonBuilder
{
    public required ScheduleBuilder Schedule { get; init; }
    public required int Id { get; init; }
    public ref RegularLessonBuilderModel Ref => ref Schedule.RegularLessons.Ref(Id);
    public static implicit operator int(RegularLessonBuilder r) => r.Id;
    public RegularLessonBuilderModel Model => Ref;
}

public static class ScheduleBuilderHelper
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
                    Groups = x.Group.Groups,
                    SubGroup = x.Group.SubGroup,
                    Course = x.General.Course!.Value,
                    Room = x.General.Room!.Value,
                    Teacher = x.General.Teacher!.Value,
                    Type = x.General.Type,
                },
            };
            return ret;
        });
        var oneTimeLessons = s.OneTimeLessons.ToImmutableArray();
        var groups = s.Groups.Build();
        var teachers = s.Teachers.Build();
        var courses = s.Courses.Build();

        return new Schedule
        {
            RegularLessons = regularLessons,
            OneTimeLessons = oneTimeLessons,
            Groups = groups,
            Teachers = teachers,
            Courses = courses,
        };
    }

    public static void SetStudyYear(this ScheduleBuilder s, int year)
    {
        if (s.Groups.Count != 0)
        {
            throw new InvalidOperationException("The year must be initialized prior to creating groups.");
        }
        s.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = year,
        });
    }

    private static int DetermineStudyYear()
    {
        var now = DateTime.Now;
        if (now.Month >= 8 && now.Month <= 12)
        {
            return now.Year;
        }
        return now.Year - 1;
    }

    public static GroupBuilder Group(this ScheduleBuilder s, string fullName)
    {
        s.GroupParseContext ??= GroupParseContext.Create(new()
        {
            CurrentStudyYear = DetermineStudyYear(),
        });
        var result = s.Groups.New();
        result.Value = s.GroupParseContext.Parse(fullName);
        return new()
        {
            Id = new(result.Id),
            Schedule = s,
        };
    }

    public static GroupBuilder Group(this ScheduleBuilder s)
    {
        var result = s.Groups.New();
        return new()
        {
            Id = new(result.Id),
            Schedule = s,
        };
    }

    public static void Configure(this GroupBuilder g, Action<GroupBuilder> action)
    {
        action(g);
    }

    public static void Validate(this ScheduleBuilder s)
    {
        foreach (ref var group in CollectionsMarshal.AsSpan(s.Groups.List))
        {
            if (group.Name == null)
            {
                throw new InvalidOperationException("The group name must be initialized.");
            }

            if (group.Grade == 0)
            {
                throw new InvalidOperationException("The group grade must be initialized.");
            }
        }

        foreach (ref var lesson in CollectionsMarshal.AsSpan(s.RegularLessons.List))
        {
            if (lesson.Date.TimeSlot is null)
            {
                throw new InvalidOperationException("The lesson date must be initialized.");
            }

            if (lesson.Date.DayOfWeek is null)
            {
                throw new InvalidOperationException("The lesson date must be initialized.");
            }


            {
                if (lesson.Group.Groups.Group0 == GroupId.Invalid)
                {
                    throw new InvalidOperationException("The lesson group must be initialized.");
                }

                foreach (var groupId in lesson.Group.Groups)
                {
                    if (groupId.Value >= s.Groups.Count || groupId.Value < 0)
                    {
                        throw new InvalidOperationException("Invalid group id in lesson");
                    }
                }
            }

            ValidateSubGroup(ref lesson);
            void ValidateSubGroup(ref RegularLessonBuilderModel lesson1)
            {
                var subgroupValidation = s.ValidationSettings.SubGroup;
                if (subgroupValidation == SubGroupValidationMode.None)
                {
                    return;
                }

                var groups = lesson1.Group.Groups;
                var subgroup = lesson1.Group.SubGroup;
                if (subgroup == SubGroupNumber.All)
                {
                    return;
                }

                int subgroupIndex = subgroup.Value - 1;

                if (!groups.IsSingleGroup)
                {
                    throw new InvalidOperationException("Subgroup only supported for a singular group");
                }
                var groupId = groups.Group0;
                var group = s.Groups.Ref(groupId.Value);
                if (subgroupIndex < group.SubGroupCount)
                {
                    return;
                }

                switch (subgroupValidation)
                {
                    case SubGroupValidationMode.Strict:
                    {
                        throw new InvalidOperationException("Invalid subgroup number");
                    }
                    case SubGroupValidationMode.PossiblyIncreaseSubGroupCount:
                    {
                        group.SubGroupCount = subgroupIndex + 1;
                        return;
                    }
                    default:
                    {
                        Debug.Fail("Invalid validation mode");
                        return;
                    }
                }
            }

            if (lesson.General.Course == null)
            {
                throw new InvalidOperationException("The lesson course must be initialized.");
            }

            if (lesson.General.Room == null)
            {
                throw new InvalidOperationException("The lesson room must be initialized.");
            }

            if (lesson.General.Teacher == null)
            {
                throw new InvalidOperationException("The lesson teacher must be initialized.");
            }
        }
    }

    public static TeacherId Teacher(this ScheduleBuilder s, string name)
    {
        var r = s.Teachers.New();
        r.Value = new()
        {
            Name = name,
        };
        return new(r.Id);
    }

    public static CourseId Course(this ScheduleBuilder s, params string[] names)
    {
        var r = s.Courses.New();
        r.Value = new()
        {
            Names = names,
        };
        return new(r.Id);
    }

    public static RoomId Room(this ScheduleBuilder s, string id)
    {
        return new(id);
    }

    public static RegularLessonBuilder RegularLesson(this ScheduleBuilder s)
    {
        var r = s.RegularLessons.New();
        r.Value = new();
        return new()
        {
            Id = r.Id,
            Schedule = s,
        };
    }

    public static RegularLessonBuilder RegularLesson(this ScheduleBuilder s, Action<RegularLessonBuilder> b)
    {
        var ret = RegularLesson(s);
        b(ret);
        return ret;
    }

    public static LessonConfigScope Scope(this ScheduleBuilder s)
    {
        return new()
        {
            Defaults = new(),
            Schedule = s,
        };
    }

    public static LessonConfigScope Scope(this ScheduleBuilder s, Action<LessonConfigScope> configure)
    {
        var ret = Scope(s);
        configure(ret);
        return ret;
    }

    public static LessonConfigScope Scope(this LessonConfigScope s, Action<LessonConfigScope> configure)
    {
        var scope1 = Scope(s.Schedule);
        scope1.Model.CopyFrom(s.Model);
        configure(scope1);
        return scope1;
    }

    public static RegularLessonBuilder RegularLesson(this LessonConfigScope scope)
    {
        var lesson = RegularLesson(scope.Schedule);
        lesson.Ref.CopyFrom(scope.Defaults);
        return lesson;
    }

    public static RegularLessonBuilder RegularLesson(this LessonConfigScope scope, Action<RegularLessonBuilder> b)
    {
        var ret = RegularLesson(scope);
        b(ret);
        return ret;
    }
}

// Allows to specify defaults.
public sealed class LessonConfigScope : ILessonBuilder
{
    public required RegularLessonBuilderModel Defaults;
    public required ScheduleBuilder Schedule;

    public RegularLessonBuilderModel Model => Defaults;
}


