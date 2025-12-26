using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public LookupModule? LookupModule = null;
}

public sealed class LessonsByCourseMap : List<List<RegularLessonId>>
{
    public List<RegularLessonId> this[CourseId courseId]
    {
        get
        {
            Debug.Assert(!courseId.IsInvalid);
            return this[courseId.Id];
        }
    }
}

public sealed class LookupModule()
{
    public readonly LessonsByCourseMap LessonsByCourse = new();
    public readonly Dictionary<string, CourseId> Courses = new(StringComparer.CurrentCultureIgnoreCase);
    public readonly TeachersByLastName TeachersByLastName = new();
    public readonly Dictionary<string, GroupId> Groups = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        Courses.Clear();
        TeachersByLastName.Clear();
        Groups.Clear();
        LessonsByCourse.Clear();
    }
}

public sealed class LookupFacade(ScheduleBuilder s)
{
    public IEnumerable<TeacherId> Teachers(LastName lastName)
    {
        if (LookupModule.TeachersByLastName.Get(lastName) is not { } ids)
        {
            return [];
        }
        return ids.Select(id => new TeacherId(id));
    }

    public TeacherId? Teacher(LastName lastName)
    {
        using var e = Teachers(lastName).GetEnumerator();
        if (!e.MoveNext())
        {
            return null;
        }
        return e.Current;
    }

    public TeacherId? Teacher(TeacherBuilderModel.NameModel name)
    {
        if (LookupModule.TeachersByLastName.Get(name.LastName) is not { } ids)
        {
            return null;
        }

        var firstNameParts = name.FirstName.Map(x =>
        {
            if (x.Longer is not { } part)
            {
                return Word.Empty;
            }
            return new Word(part);
        });
        int i = TeacherLookupHelper.FindIndexOfBestMatch(s, ids, firstNameParts);
        return new(ids[i]);
    }

    public TeacherId? Teacher(string firstName, string lastName)
    {
        if (LookupModule.TeachersByLastName.Get(lastName) is not { } ids)
        {
            return null;
        }

        var firstNameParts = ParseTeacherFirstName(firstName);
        int i = TeacherLookupHelper.FindIndexOfBestMatch(s, ids, firstNameParts);
        return new(ids[i]);
    }

    private static NameParts<Word> ParseTeacherFirstName(string firstName)
    {
        var firstNameParts = default(NameParts<Word>);
        var firstNameSpan = firstName.AsSpan();
        var splitName = firstNameSpan.Split(NameConstants.DoubleNameSeparator);
        var firstNameE = firstNameParts.AsRef().GetEnumerator();

        foreach (var partRange in splitName)
        {
            var span = firstNameSpan[partRange];
            if (span.Length == 0)
            {
                continue;
            }
            span = span.Trim();
            if (span.Length == 0)
            {
                throw new ArgumentException(
                    message: "Don't use double dashes in the names, only use single dashes",
                    paramName: nameof(firstName));
            }

            bool nextNamePartOk = firstNameE.MoveNext();
            if (!nextNamePartOk)
            {
                throw new ArgumentException(
                    message: "Too many name parts",
                    paramName: nameof(firstName));
            }

            var part = span.ToString();
            firstNameE.Current = new(part);
        }

        while (firstNameE.MoveNext())
        {
            firstNameE.Current = Word.Empty;
        }

        return firstNameParts;
    }

    public GroupId? Group(string fullName)
    {
        var group = s.ParseGroup(fullName);
        return Find(LookupModule.Groups, group.Name);
    }

    public LookupModule LookupModule
    {
        get
        {
            var l = s.LookupModule;
            Debug.Assert(l != null);
            return l;
        }
    }

    private T? Find<T>(Dictionary<string, T> dict, ReadOnlySpan<char> val)
        where T : struct
    {
        bool t = dict.TryGetAlternateLookup<ReadOnlySpan<char>>(out var d);
        Debug.Assert(t);
        _ = t;

        if (!d.TryGetValue(val, out var id))
        {
            return null;
        }
        Debug.Assert(Marshal.SizeOf<T>() == sizeof(int));
        return id;
    }

    public IReadOnlyList<RegularLessonId> LessonsOfCourse(CourseId courseId)
    {
        return LookupModule.LessonsByCourse[courseId];
    }
}


public static partial class ScheduleBuilderHelper
{
    public static LookupModule EnableLookupModule(this ScheduleBuilder s)
    {
        if (s.LookupModule is not null)
        {
            return s.LookupModule;
        }

        var lookupModule = s.LookupModule = new();
        InitLookup(s, lookupModule);
        s.LookupModule = lookupModule;
        return lookupModule;
    }

    public static void RefreshLookup(this ScheduleBuilder s)
    {
        if (s.LookupModule is null)
        {
            EnableLookupModule(s);
            return;
        }

        s.LookupModule.Clear();
        InitLookup(s, s.LookupModule);
    }

    private static void InitLookup(this ScheduleBuilder s, LookupModule lookup)
    {
        {
            var coursesMap = lookup.Courses;
            for (int i = 0; i < s.Courses.Count; i++)
            {
                ref var course = ref s.Courses.Ref(i);
                foreach (var name in course.Names)
                {
                    coursesMap.Add(name, new(i));
                }
            }
        }
        {
            var teachersMap = lookup.TeachersByLastName;
            for (int i = 0; i < s.Teachers.Count; i++)
            {
                ref var teacher = ref s.Teachers.Ref(i);
                if (teacher.Name.LastName == default)
                {
                    continue;
                }
                var list = teachersMap.AddOrGet(teacher.Name.LastName);
                list.Add(i);
            }
        }
        {
            var groupsMap = lookup.Groups;
            for (int i = 0; i < s.Groups.Count; i++)
            {
                ref var group = ref s.Groups.Ref(i);
                groupsMap.Add(group.Name, new(i));
            }
        }
        {
            var courseCount = s.Courses.Count;
            CollectionsMarshal.SetCount(lookup.LessonsByCourse, courseCount);
            foreach (ref var it in CollectionsMarshal.AsSpan(lookup.LessonsByCourse))
            {
                it = new();
            }
        }
        {
            for (int i = 0; i < s.RegularLessons.Count; i++)
            {
                ref var lesson = ref s.RegularLessons.Ref(i);
                if (lesson.General.Course is not { } courseId)
                {
                    continue;
                }
                var list = lookup.LessonsByCourse[courseId.Id];
                list.Add(new(i));
            }
        }
    }

    [DebuggerStepThrough]
    public static LookupFacade Lookup(this ScheduleBuilder s)
    {
        s.EnableLookupModule();
        return new(s);
    }

    public static TeacherBuilderModel.NameModel ToNameModel(this Name name)
    {
        var ret = new TeacherBuilderModel.NameModel();
        ret.FirstName = name.FirstName.Map(x => new OptionalNamePart
        {
            Short = null,
            Full = x,
        });
        ret.LastName = new(name.LastName);
        return ret;
    }
}
