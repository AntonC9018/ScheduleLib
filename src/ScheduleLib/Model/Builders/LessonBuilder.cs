using System.Runtime.InteropServices;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public ListBuilder<RegularLessonBuilderModel> RegularLessons = new();
}

public struct RegularLessonModelMergeMask()
{
    public bool Teachers;
    public bool Groups;
}

public record struct RegularLessonModelDiffMask()
{
    public enum BitIndex
    {
        Course,
        OneTeacher,
        AllTeachers,
        Room,
        Type,
        OneGroup,
        AllGroups,
        SubGroup,
        Day,
        TimeSlot,
        Parity,
        Count,
    }

    public BitArray32 Bits = BitArray32.Empty((int) BitIndex.Count);

    public bool Course
    {
        get => Bits.IsSet((int) BitIndex.Course);
        set => Bits.Set((int) BitIndex.Course, value);
    }
    public bool OneTeacher
    {
        get => Bits.IsSet((int) BitIndex.OneTeacher);
        set => Bits.Set((int) BitIndex.OneTeacher, value);
    }

    public bool AllTeachers
    {
        get => Bits.IsSet((int) BitIndex.AllTeachers);
        set => Bits.Set((int) BitIndex.AllTeachers, value);
    }

    public bool Room
    {
        get => Bits.IsSet((int) BitIndex.Room);
        set => Bits.Set((int) BitIndex.Room, value);
    }

    public bool LessonType
    {
        get => Bits.IsSet((int) BitIndex.Type);
        set => Bits.Set((int) BitIndex.Type, value);
    }

    public bool OneGroup
    {
        get => Bits.IsSet((int) BitIndex.OneGroup);
        set => Bits.Set((int) BitIndex.OneGroup, value);
    }

    public bool AllGroups
    {
        get => Bits.IsSet((int) BitIndex.AllGroups);
        set => Bits.Set((int) BitIndex.AllGroups, value);
    }

    public bool SubGroup
    {
        get => Bits.IsSet((int) BitIndex.SubGroup);
        set => Bits.Set((int) BitIndex.SubGroup, value);
    }

    public bool Day
    {
        get => Bits.IsSet((int) BitIndex.Day);
        set => Bits.Set((int) BitIndex.Day, value);
    }

    public bool TimeSlot
    {
        get => Bits.IsSet((int) BitIndex.TimeSlot);
        set => Bits.Set((int) BitIndex.TimeSlot, value);
    }

    public bool Parity
    {
        get => Bits.IsSet((int) BitIndex.Parity);
        set => Bits.Set((int) BitIndex.Parity, value);
    }

    public RegularLessonModelDiffMask Intersect(RegularLessonModelDiffMask mask)
    {
        return new()
        {
            Bits = Bits.Intersect(mask.Bits),
        };
    }

    public readonly bool TheyAreEqual => Bits.IsEmpty;
    public readonly bool TheyDiffer => !Bits.IsEmpty;
}

public struct RegularLessonBuilderModelData()
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
        public List<TeacherId> Teachers = new();
        public RoomId Room;
        public LessonType Type = LessonType.Unspecified;
    }
}

public sealed class RegularLessonBuilderModel
{
    public RegularLessonBuilderModelData Data = new();
    public ref RegularLessonBuilderModelData.GeneralData General => ref Data.General;
    public ref RegularLessonDateBuilderModel Date => ref Data.Date;
    public ref RegularLessonBuilderModelData.GroupData Group => ref Data.Group;

    public void CopyFrom(in RegularLessonBuilderModelData model)
    {
        General = model.General with
        {
            Teachers = [..model.General.Teachers],
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

    // NOTE: to make this more generic, can make the whole state of the builder model a struct.
    public void UpdateLookup(CourseId? prevCourseId)
    {
        if (Schedule.LookupModule is not { } lookupModule)
        {
            return;
        }

        if (prevCourseId is { } p)
        {
            lookupModule.LessonsByCourse[p.Id].Remove(Id);
        }
        if (Model.General.Course is { } p1)
        {
            lookupModule.LessonsByCourse[p1.Id].Add(Id);
        }
    }

    public void InitLookup()
    {
        if (Schedule.LookupModule is not { } lookupModule)
        {
            return;
        }

        if (Model.General.Course is { } p1)
        {
            lookupModule.LessonsByCourse[p1.Id].Add(Id);
        }
    }
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

    public static void Teacher(this ILessonBuilder b, TeacherId teacher) => b.Model.General.Teachers.Add(teacher);
    public static void Room(this ILessonBuilder b, RoomId room) => b.Model.General.Room = room;
    public static void Type(this ILessonBuilder b, LessonType type) => b.Model.General.Type = type;
    public static void Course(this ILessonBuilder b, CourseId course)
    {
        var prev = b.Model.General.Course;
        b.Model.General.Course = course;

        if (b is not RegularLessonBuilder b1)
        {
            return;
        }

        b1.UpdateLookup(prev);
    }

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

    public static RegularLessonBuilder RegularLesson(
        this ScheduleBuilder s,
        in RegularLessonBuilderModelData modelData)
    {
        var ret = RegularLesson(s);
        ret.Model.Data = modelData;
        ret.InitLookup();
        return ret;
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
        scope1.Model.CopyFrom(s.Model.Data);
        configure(scope1);
        return scope1;
    }

    public static RegularLessonBuilder RegularLesson(this LessonConfigScope scope)
    {
        var lesson = RegularLesson(scope.Schedule);
        lesson.Model.CopyFrom(scope.Defaults.Data);
        lesson.InitLookup();
        return lesson;
    }

    public static RegularLessonBuilder RegularLesson(this LessonConfigScope scope, Action<RegularLessonBuilder> b)
    {
        var ret = RegularLesson(scope);
        b(ret);
        return ret;
    }

    public static RegularLessonModelDiffMask Diff(
        in RegularLessonBuilderModelData a,
        in RegularLessonBuilderModelData b,
        RegularLessonModelDiffMask whatToDiff)
    {
        var ret = new RegularLessonModelDiffMask();
        if (whatToDiff.Course)
        {
            if (a.General.Course != b.General.Course)
            {
                ret.Course = true;
            }
        }
        if (whatToDiff.OneTeacher)
        {
            if (!a.General.Teachers.SequenceEqual(b.General.Teachers))
            {
                ret.OneTeacher = true;
            }
        }

        if (whatToDiff.AllTeachers)
        {
            if (AllTeachersNotEqual(a, b))
            {
                ret.AllTeachers = true;
            }
        }
        static bool AllTeachersNotEqual(
            in RegularLessonBuilderModelData a,
            in RegularLessonBuilderModelData b)
        {
            foreach (var teach1 in a.General.Teachers)
            {
                foreach (var teach2 in b.General.Teachers)
                {
                    if (teach1 == teach2)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        if (whatToDiff.Room)
        {
            if (a.General.Room != b.General.Room)
            {
                ret.Room = true;
            }
        }
        if (whatToDiff.LessonType)
        {
            if (a.General.Type != b.General.Type)
            {
                ret.LessonType = true;
            }
        }
        if (whatToDiff.OneGroup)
        {
            if (a.Group.Groups != b.Group.Groups)
            {
                ret.OneGroup = true;
            }
        }

        if (whatToDiff.AllGroups)
        {
            if (AllGroupsNotEqual(a, b))
            {
                ret.AllGroups = true;
            }
        }

        bool AllGroupsNotEqual(
            in RegularLessonBuilderModelData a,
            in RegularLessonBuilderModelData b)
        {
            foreach (var g in a.Group.Groups)
            {
                foreach (var g1 in b.Group.Groups)
                {
                    if (g == g1)
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        if (whatToDiff.SubGroup)
        {
            if (a.Group.SubGroup != b.Group.SubGroup)
            {
                ret.SubGroup = true;
            }
        }
        if (whatToDiff.Day)
        {
            if (a.Date.DayOfWeek != b.Date.DayOfWeek)
            {
                ret.Day = true;
            }
        }
        if (whatToDiff.TimeSlot)
        {
            if (a.Date.TimeSlot != b.Date.TimeSlot)
            {
                ret.TimeSlot = true;
            }
        }
        if (whatToDiff.Parity)
        {
            if (a.Date.Parity != b.Date.Parity)
            {
                ret.Parity = true;
            }
        }

        return ret;
    }

    public static void Merge(
        ref RegularLessonBuilderModelData to,
        in RegularLessonBuilderModelData from,
        RegularLessonModelMergeMask merge)
    {
        if (merge.Teachers)
        {
            foreach (var teach in from.General.Teachers)
            {
                if (!ContainsTeacher(to, teach))
                {
                    to.General.Teachers.Add(teach);
                }
                static bool ContainsTeacher(
                    in RegularLessonBuilderModelData to,
                    TeacherId teach1)
                {
                    foreach (var teach2 in to.General.Teachers)
                    {
                        if (teach1 == teach2)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
        }
        if (merge.Groups)
        {
            foreach (var g in from.Group.Groups)
            {
                if (!ContainsGroup(to, g))
                {
                    to.Group.Groups.Add(g);
                }
                static bool ContainsGroup(
                    in RegularLessonBuilderModelData to,
                    GroupId g1)
                {
                    foreach (var g2 in to.Group.Groups)
                    {
                        if (g1 == g2)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
        }
    }
}

