using System.Runtime.InteropServices;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public ListBuilder<RegularLessonBuilderModel> RegularLessons = new();
}

public sealed class RegularLessonBuilderModel
{
    public GeneralData General = new();
    public RegularLessonDateBuilderModel Date = new();
    public GroupData Group = new();

    public struct GroupData()
    {
        public LessonGroups Groups = new();
        public SubGroup SubGroup = SubGroup.All;
    }

    public struct GeneralData()
    {
        public CourseId? Course;
        public List<TeacherId> Teacher = new();
        public RoomId Room;
        public LessonType Type = LessonType.Unspecified;
    }

    public void CopyFrom(RegularLessonBuilderModel model)
    {
        General = model.General with
        {
            Teacher = [..model.General.Teacher],
        };
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

public sealed class RegularLessonBuilder : ILessonBuilder
{
    public required ScheduleBuilder Schedule { get; init; }
    public required int Id { get; init; }
    public RegularLessonBuilderModel Model => Schedule.RegularLessons.Ref(Id);
    public static implicit operator int(RegularLessonBuilder r) => r.Id;
}

// Allows to specify defaults.
public sealed class LessonConfigScope : ILessonBuilder
{
    public required RegularLessonBuilderModel Defaults;
    public required ScheduleBuilder Schedule;

    public RegularLessonBuilderModel Model => Defaults;
}

public static class LessonBuilderHelper
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

    public static void Group(this ILessonBuilder b, GroupId group, SubGroup? subGroup = null)
    {
        b.Model.Group.Groups = new()
        {
            Group0 = group,
        };
        b.Model.Group.SubGroup = subGroup ?? SubGroup.All;
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

        var g = new LessonGroups();
        for (int i = 0; i < groups.Length; i++)
        {
            g[i] = groups[i];
        }
        b.Model.Group.Groups = g;
    }

    public static void Teacher(this ILessonBuilder b, TeacherId teacher) => b.Model.General.Teacher.Add(teacher);
    public static void Room(this ILessonBuilder b, RoomId room) => b.Model.General.Room = room;
    public static void Type(this ILessonBuilder b, LessonType type) => b.Model.General.Type = type;
    public static void Course(this ILessonBuilder b, CourseId course) => b.Model.General.Course = course;
    public static void Add<T>(this ILessonBuilder b, T id) where T : struct
    {
        if (typeof(T) == typeof(TeacherId))
        {
            b.Teacher((TeacherId) (object) id);
            return;
        }
        if (typeof(T) == typeof(RoomId))
        {
            b.Room((RoomId) (object) id);
            return;
        }
        if (typeof(T) == typeof(CourseId))
        {
            b.Course((CourseId) (object) id);
            return;
        }
        throw new ArgumentException("Invalid type");
    }

    public static void ValidateLessons(ScheduleBuilder s)
    {
        foreach (var lesson in CollectionsMarshal.AsSpan(s.RegularLessons.List))
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

            if (lesson.General.Course == null)
            {
                throw new InvalidOperationException("The lesson course must be initialized.");
            }
        }
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
        lesson.Model.CopyFrom(scope.Defaults);
        return lesson;
    }

    public static RegularLessonBuilder RegularLesson(this LessonConfigScope scope, Action<RegularLessonBuilder> b)
    {
        var ret = RegularLesson(scope);
        b(ret);
        return ret;
    }
}

