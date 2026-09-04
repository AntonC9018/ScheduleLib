using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using ScheduleLib.Helper;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public ListBuilder<WeeklyLessonBuilderModel> WeeklyLessons = new();
}

public struct LessonModelMergeMask()
{
    public bool Teachers;
    public bool Groups;
}

public record struct LessonModelDiffMask()
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
        Period,
        Date,
        Count,
    }

    public EnumBitArray<BitIndex> Impl = default;

    public bool Course
    {
        get => Impl.IsSet(BitIndex.Course);
        set => Impl.Set(BitIndex.Course, value);
    }
    public bool OneTeacher
    {
        get => Impl.IsSet(BitIndex.OneTeacher);
        set => Impl.Set(BitIndex.OneTeacher, value);
    }

    public bool AllTeachers
    {
        get => Impl.IsSet(BitIndex.AllTeachers);
        set => Impl.Set(BitIndex.AllTeachers, value);
    }

    public bool Room
    {
        get => Impl.IsSet(BitIndex.Room);
        set => Impl.Set(BitIndex.Room, value);
    }

    public bool LessonType
    {
        get => Impl.IsSet(BitIndex.Type);
        set => Impl.Set(BitIndex.Type, value);
    }

    public bool OneGroup
    {
        get => Impl.IsSet(BitIndex.OneGroup);
        set => Impl.Set(BitIndex.OneGroup, value);
    }

    public bool AllGroups
    {
        get => Impl.IsSet(BitIndex.AllGroups);
        set => Impl.Set(BitIndex.AllGroups, value);
    }

    public bool SubGroup
    {
        get => Impl.IsSet(BitIndex.SubGroup);
        set => Impl.Set(BitIndex.SubGroup, value);
    }

    public bool Day
    {
        get => Impl.IsSet(BitIndex.Day);
        set => Impl.Set(BitIndex.Day, value);
    }

    public bool TimeSlot
    {
        get => Impl.IsSet(BitIndex.TimeSlot);
        set => Impl.Set(BitIndex.TimeSlot, value);
    }

    public bool Parity
    {
        get => Impl.IsSet(BitIndex.Parity);
        set => Impl.Set(BitIndex.Parity, value);
    }

    public bool Period
    {
        get => Impl.IsSet(BitIndex.Period);
        set => Impl.Set(BitIndex.Period, value);
    }

    public bool Date
    {
        get => Impl.IsSet(BitIndex.Date);
        set => Impl.Set(BitIndex.Date, value);
    }

    [Pure]
    public readonly LessonModelDiffMask Intersect(LessonModelDiffMask mask)
    {
        return new()
        {
            Impl = Impl.Intersect(mask.Impl),
        };
    }

    [Pure]
    public readonly LessonModelDiffMask Union(LessonModelDiffMask mask)
    {
        return new()
        {
            Impl = Impl.UnionWith(mask.Impl),
        };
    }

    [Pure]
    public readonly LessonModelDiffMask Remove(LessonModelDiffMask mask)
    {
        return new()
        {
            Impl = Impl.Remove(mask.Impl),
        };
    }

    public readonly bool TheyAreEqual => Impl.IsEmpty;
    public readonly bool TheyDiffer => !Impl.IsEmpty;
}

public struct LessonBuilderModelDataBase()
{
    public LessonBuilderGeneralData General = new();
    public LessonBuilderGroupData Group = new();
}

public struct OneTimeLessonBuilderModelData()
{
    public LessonBuilderModelDataBase Base = new();
    public OneTimeLessonDateBuilderModel Date = new();
}

public struct WeeklyLessonBuilderModelData()
{
    public LessonBuilderModelDataBase Base = new();
    public RegularLessonDateBuilderModel Date = new();
}

public struct LessonBuilderGroupData()
{
    public LessonGroups Groups = new();
    public SubGroup SubGroup = SubGroup.All;
    public Specialization Specialization = Specialization.All;
    public Alternative Alternative = Alternative.All;
}
public struct LessonBuilderGeneralData()
{
    public CourseId? Course;
    public List<TeacherId> Teachers = new();
    public RoomId Room;
    public LessonType Type = LessonType.Unspecified;
    public PeriodId Period = PeriodId.Unspecified;
}

public interface ILessonBuilderModel
{
    public ref LessonBuilderModelDataBase Base { get; }
    public LessonRegularity Regularity { get; }
}

public sealed class WeeklyLessonBuilderModel : ILessonBuilderModel
{
    public WeeklyLessonBuilderModelData Data = new();
    public ref LessonBuilderModelDataBase Base => ref Data.Base;
    public ref LessonBuilderGeneralData General => ref Data.Base.General;
    public ref RegularLessonDateBuilderModel Date => ref Data.Date;
    public ref LessonBuilderGroupData Group => ref Data.Base.Group;
    public LessonRegularity Regularity => LessonRegularity.Weekly;
}

public sealed class OneTimeLessonBuilderModel : ILessonBuilderModel
{
    public OneTimeLessonBuilderModelData Data = new();
    public ref LessonBuilderModelDataBase Base => ref Data.Base;
    public ref LessonBuilderGeneralData General => ref Data.Base.General;
    public ref OneTimeLessonDateBuilderModel Date => ref Data.Date;
    public ref LessonBuilderGroupData Group => ref Data.Base.Group;
    public LessonRegularity Regularity => LessonRegularity.OneTime;
}

public struct RegularLessonDateBuilderModel()
{
    public Parity? Parity;
    public DayOfWeek? DayOfWeek;
    public TimeSlot? TimeSlot;
}

public struct OneTimeLessonDateBuilderModel()
{
    public DateOnly? Date;
    public TimeSlot? TimeSlot;
}

public interface ILessonBuilder<out T> where T : ILessonBuilderModel
{
    public T Model { get; }
}

public class LessonBuilder<T> : ILessonBuilder<T>
    where T : ILessonBuilderModel
{
    public LessonBuilder(
        ScheduleBuilder s,
        T model,
        int id)
    {
        Schedule = s;
        Model = model;
        Id = id;
    }
    public ScheduleBuilder Schedule { get; }
    public T Model { get; }
    public int Id { get; internal set; }
    public static implicit operator int(LessonBuilder<T> r) => r.Id;
}

public static class LessonBuilderHelper
{
    internal const int UninitializedId = -1;
    extension(ILessonBuilder<WeeklyLessonBuilderModel> b)
    {
        public void DayOfWeek(DayOfWeek dayOfWeek)
        {
            b.Model.Date.DayOfWeek = dayOfWeek;
        }

        public void TimeSlot(TimeSlot timeSlot)
        {
            b.Model.Date.TimeSlot = timeSlot;
        }

        public void Parity(Parity parity)
        {
            b.Model.Date.Parity = parity;
        }

        public void Date(RegularLessonDateBuilderModel date)
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
    }

    extension(ILessonBuilder<OneTimeLessonBuilderModel> b)
    {
        public void Date(DateOnly date)
        {
            // if (date == default)
            // {
            //     throw new ArgumentException("Date must not be default", nameof(date));
            // }
            b.Model.Date.Date = date;
        }

        public void TimeSlot(TimeSlot timeSlot)
        {
            b.Model.Date.TimeSlot = timeSlot;
        }
    }

    extension(ILessonBuilder<ILessonBuilderModel> b)
    {
        public void SubGroup(SubGroup subGroup)
        {
            b.Model.Base.Group.SubGroup = subGroup;
        }
        public void Specialization(Specialization specialization)
        {
            b.Model.Base.Group.Specialization = specialization;
        }
        public void Alternative(Alternative alternative)
        {
            b.Model.Base.Group.Alternative = alternative;
        }
        public void Group(GroupId group, SubGroup? subGroup = null)
        {
            b.Model.Base.Group.Groups = [group];
            if (subGroup is { } s)
            {
                b.SubGroup(s);
            }
        }

        public void Groups(ReadOnlySpan<GroupId> groups)
        {
            if (groups.Length == 0)
            {
                throw new ArgumentException("At least one group must be specified.");
            }

            var g = new LessonGroups();
            if (groups.Length > g.Capacity)
            {
                throw new ArgumentException($"The maximum number of groups is {g.Capacity}.");
            }

            for (int i = 0; i < groups.Length; i++)
            {
                g[i] = groups[i];
            }
            b.Model.Base.Group.Groups = g;
        }

        public void Teacher(TeacherId teacher) => b.Model.Base.General.Teachers.Add(teacher);
        public void Room(RoomId room) => b.Model.Base.General.Room = room;
        public void Type(LessonType type) => b.Model.Base.General.Type = type;
        public void Course(CourseId course)
        {
            var prev = b.Model.Base.General.Course;
            b.Model.Base.General.Course = course;

            if (b is not LessonBuilder<ILessonBuilderModel> b1)
            {
                return;
            }

            b1.UpdateLookup(prev);
        }

        public void Period(PeriodId period)
        {
            b.Model.Base.General.Period = period;
        }
    }

    extension<T>(LessonBuilder<T> b) where T : ILessonBuilderModel
    {
        public void UpdateLookup(CourseId? prevCourseId)
        {
            if (b.Schedule.LookupModule is not { } lookupModule)
            {
                return;
            }
            if (b.Id == UninitializedId)
            {
                return;
            }

            var id = new AnyLessonId(b.Model.Regularity, b.Id);
            if (prevCourseId is { } p)
            {
                lookupModule.LessonsByCourse[p.Id].Remove(id);
            }
            if (b.Model.Base.General.Course is { } p1)
            {
                lookupModule.LessonsByCourse[p1.Id].Add(id);
            }
        }
        public void InitLookup() => b.UpdateLookup(prevCourseId: null);
    }

    internal static void ValidateLessons(ScheduleBuilder s)
    {
        var groupIdValidationSet = new HashSet<GroupId>();

        {
            var span = CollectionsMarshal.AsSpan(s.WeeklyLessons.List);
            for (int index = 0; index < span.Length; index++)
            {
                var lesson = span[index];
                ValidateBase(lesson.Base);

                if (lesson.Date.TimeSlot is null)
                {
                    throw new UninitializedScheduleModelException("The lesson date must be initialized.");
                }

                if (lesson.Date.DayOfWeek is null)
                {
                    throw new UninitializedScheduleModelException("The lesson date must be initialized.");
                }
            }
        }

        {
            var span = CollectionsMarshal.AsSpan(s.OneTimeLessons.List);
            for (int index = 0; index < span.Length; index++)
            {
                var lesson = span[index];
                ValidateBase(lesson.Base);

                if (lesson.Date.TimeSlot is null)
                {
                    throw new UninitializedScheduleModelException("The lesson date must be initialized.");
                }

                if (lesson.Date.Date is null)
                {
                    throw new UninitializedScheduleModelException("The lesson date must be initialized.");
                }
            }
        }

        void ValidateBase(in LessonBuilderModelDataBase lesson)
        {
            if (lesson.General.Type == LessonType.Consultation)
            {
                if (lesson.Group.Groups.Count != 0)
                {
                    throw new InvalidLessonGroupsException("Consultation lessons must have no groups attached");
                }
            }
            else
            {
                if (lesson.Group.Groups.Group0 == GroupId.Invalid)
                {
                    throw new UninitializedScheduleModelException("The lesson group must be initialized.");
                }

                var hs = groupIdValidationSet;
                hs.Clear();
                foreach (var g in lesson.Group.Groups)
                {
                    if (!hs.Add(g))
                    {
                        var course = s.Courses.Ref(lesson.General.Course!.Value.Id);
                        _ = course;
                        throw new InvalidLessonGroupsException("Duplicate group in the same lesson");
                    }
                }

                foreach (var groupId in lesson.Group.Groups)
                {
                    if (groupId.Value >= s.Groups.Count || groupId.Value < 0)
                    {
                        throw new InvalidLessonGroupsException("Invalid group id in lesson");
                    }
                }

                Group Group1(in LessonBuilderModelDataBase lesson, int i)
                {
                    return s.Groups.Ref(lesson.Group.Groups[i].Value);
                }

                var g0 = Group1(lesson, 0);
                var allowedModes = EnumBitArray<AttendanceMode>.Empty;
                if (g0.AttendanceMode is AttendanceMode.Dual or AttendanceMode.Zi)
                {
                    allowedModes.Set(AttendanceMode.Dual);
                    allowedModes.Set(AttendanceMode.Zi);
                }
                else
                {
                    allowedModes.Set(g0.AttendanceMode);
                }

                for (int i = 1; i < lesson.Group.Groups.Count; i++)
                {
                    var gi = Group1(lesson, i);
                    var ami = gi.AttendanceMode;
                    if (!allowedModes.Contains(ami))
                    {
                        throw new InvalidLessonGroupsException(
                            $"Mixed attendance modes for a lesson are not allowed, attendances '{g0.AttendanceMode}' and '{gi.AttendanceMode}', groups '{g0.Name}' and '{gi.Name}'!");
                    }
                }
            }

            if (lesson.General.Course is not { } courseId)
            {
                throw new UninitializedScheduleModelException("The lesson course must be initialized.");
            }
            if (courseId.IsInvalid || courseId.Id < 0 || courseId.Id >= s.Courses.Count)
            {
                throw new UninitializedScheduleModelException("The lesson course must refer to a known course.");
            }
            // Invariant for AssignImplicitSplits: every referenced course carries at
            // least one name, so the split pass can index Names[0] for diagnostics.
            if (s.Courses.Ref(courseId.Id).Names.Length == 0)
            {
                throw new UninitializedScheduleModelException("The lesson course must have at least one name.");
            }
        }
    }

    public static LessonBuilder<WeeklyLessonBuilderModel> RegularLesson(this ScheduleBuilder s)
    {
        var r = s.WeeklyLessons.New();
        r.Value = new();
        return new(s, s.WeeklyLessons.Ref(r.Id), r.Id);
    }

    public static LessonBuilder<WeeklyLessonBuilderModel> DetachedRegularLesson(this ScheduleBuilder s)
    {
        var builder = new LessonBuilder<WeeklyLessonBuilderModel>(s, new(), UninitializedId);
        return builder;
    }

    public static void Attach(this LessonBuilder<WeeklyLessonBuilderModel> builder)
    {
        if (builder.Id != UninitializedId)
        {
            Debug.Assert(false, "Attach must only be called on a detached builder.");
            throw new InvalidOperationException("Can only attach a detached builder.");
        }
        var x = builder.Schedule.WeeklyLessons.New();
        x.Value = builder.Model;
        builder.Id = x.Id;

        InitLookup(builder);
    }

    public static LessonBuilder<WeeklyLessonBuilderModel> RegularLesson(
        this ScheduleBuilder s,
        in WeeklyLessonBuilderModelData modelData)
    {
        var ret = RegularLesson(s);
        ret.Model.Data = modelData;

        ref var subGroup = ref ret.Model.Group.SubGroup;
        subGroup = s.RemapSubGroup(subGroup);

        ret.InitLookup();
        return ret;
    }

    public static LessonBuilder<WeeklyLessonBuilderModel> RegularLesson(
        this ScheduleBuilder s,
        Action<LessonBuilder<WeeklyLessonBuilderModel>> b)
    {
        var ret = RegularLesson(s);
        b(ret);
        return ret;
    }

    public static LessonBuilder<OneTimeLessonBuilderModel> OneTimeLesson(this ScheduleBuilder s)
    {
        var r = s.OneTimeLessons.New();
        r.Value = new();
        return new(s, r.Value, r.Id);
    }

    public static LessonModelDiffMask Diff(
        in LessonBuilderModelDataBase a,
        in LessonBuilderModelDataBase b,
        LessonModelDiffMask whatToDiff)
    {
        var ret = new LessonModelDiffMask();
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
            in LessonBuilderModelDataBase a,
            in LessonBuilderModelDataBase b)
        {
            foreach (var teach1 in a.General.Teachers)
            {
                if (!HasTeacher(b.General.Teachers))
                {
                    return false;
                }
                bool HasTeacher(List<TeacherId> other)
                {
                    foreach (var teach2 in other)
                    {
                        if (teach1 == teach2)
                        {
                            return true;
                        }
                    }
                    return false;
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
            in LessonBuilderModelDataBase a,
            in LessonBuilderModelDataBase b)
        {
            foreach (var g in a.Group.Groups)
            {
                if (!HasGroup(b.Group.Groups))
                {
                    return false;
                }

                bool HasGroup(in LessonGroups other)
                {
                    foreach (var g1 in other)
                    {
                        if (g == g1)
                        {
                            return true;
                        }
                    }
                    return false;
                }
            }
            return true;
        }

        if (whatToDiff.SubGroup)
        {
            if (a.Group.SubGroup != b.Group.SubGroup
                || a.Group.Specialization != b.Group.Specialization
                || a.Group.Alternative != b.Group.Alternative)
            {
                ret.SubGroup = true;
            }
        }

        if (whatToDiff.Period)
        {
            if (a.General.Period != b.General.Period)
            {
                ret.Period = true;
            }
        }

        return ret;
    }

    public static LessonModelDiffMask Diff(
        in WeeklyLessonBuilderModelData a,
        in WeeklyLessonBuilderModelData b,
        LessonModelDiffMask whatToDiff)
    {
        var ret = new LessonModelDiffMask();
        {
            var other = Diff(in a.Base, in b.Base, whatToDiff);
            ret = ret.Union(other);
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

    // TODO: Move this out of this class.
    public static LessonModelDiffMask Diff(
        in LessonData a,
        in LessonData b,
        LessonModelDiffMask whatToDiff)
    {
        var ret = new LessonModelDiffMask();
        if (whatToDiff.Course)
        {
            if (a.Course != b.Course)
            {
                ret.Course = true;
            }
        }
        if (whatToDiff.OneTeacher)
        {
            if (!a.Teachers.SequenceEqual(b.Teachers))
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
        bool AllTeachersNotEqual(
            in LessonData a,
            in LessonData b)
        {
            foreach (var teach1 in a.Teachers)
            {
                foreach (var teach2 in b.Teachers)
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
            if (a.Room != b.Room)
            {
                ret.Room = true;
            }
        }
        if (whatToDiff.LessonType)
        {
            if (a.Type != b.Type)
            {
                ret.LessonType = true;
            }
        }
        if (whatToDiff.OneGroup)
        {
            if (a.Groups != b.Groups)
            {
                ret.OneGroup = true;
            }
        }

        if (whatToDiff.AllGroups)
        {
            // note: the groups are ordered which is why this works.
            if (a.Groups != b.Groups)
            {
                ret.AllGroups = true;
            }
        }

        if (whatToDiff.SubGroup)
        {
            if (a.GroupPartitionKey != b.GroupPartitionKey)
            {
                ret.SubGroup = true;
            }
        }
        return ret;
    }

    // TODO: Move this out of this class.
    public static LessonModelDiffMask Diff(
        WeeklyLessonAccessor a,
        WeeklyLessonAccessor b,
        LessonModelDiffMask whatToDiff)
    {
        var ret = new LessonModelDiffMask();
        {
            var other = Diff(a.Lesson, b.Lesson, whatToDiff);
            ret = ret.Union(other);
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
        if (whatToDiff.Period)
        {
            if (a.Date.Period != b.Date.Period)
            {
                ret.Period = true;
            }
        }

        return ret;
    }

    public static void Merge(
        ref LessonBuilderModelDataBase to,
        in LessonBuilderModelDataBase from,
        LessonModelMergeMask merge)
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
                    in LessonBuilderModelDataBase to,
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
                    in LessonBuilderModelDataBase to,
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

    public static void Merge(
        ref WeeklyLessonBuilderModelData to,
        in WeeklyLessonBuilderModelData from,
        LessonModelMergeMask merge)
    {
        Merge(ref to.Base, from.Base, merge);
    }
}
