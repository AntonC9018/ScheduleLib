using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ScheduleLib.Parsing;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public LookupModule? LookupModule = null;
}

public sealed class LessonsByCourseMap : List<List<RegularLessonId>>
{
    public List<RegularLessonId> this[CourseId courseId] => this[courseId.Id];
}

public sealed class LookupModule()
{
    public readonly LessonsByCourseMap LessonsByCourse = new();
    public readonly Dictionary<string, int> Courses = new(StringComparer.CurrentCultureIgnoreCase);
    public readonly TeachersByLastName TeachersByLastName = new();
    public readonly Dictionary<string, int> Groups = new(StringComparer.OrdinalIgnoreCase);

    public void Clear()
    {
        Courses.Clear();
        TeachersByLastName.Clear();
        Groups.Clear();
        LessonsByCourse.Clear();
    }
}

public struct LookupFacade(ScheduleBuilder s)
{
    public CourseId? Course(string name) => Find<CourseId>(Lookup.Courses, name);

    public IEnumerable<TeacherId> Teachers(string lastName)
    {
        if (Lookup.TeachersByLastName.Get(lastName) is not { } ids)
        {
            return [];
        }
        return ids.Select(id => new TeacherId(id));
    }

    public TeacherId? Teacher(string lastName)
    {
        using var e = Teachers(lastName).GetEnumerator();
        if (!e.MoveNext())
        {
            return null;
        }
        return e.Current;
    }

    public TeacherId? Teacher(string firstName, string lastName)
    {
        if (Lookup.TeachersByLastName.Get(lastName) is not { } ids)
        {
            return null;
        }

        // TODO: Name separator constant.
        var firstNameParts = default(FirstNameParts<Word>);
        {
            var firstNameSpan = firstName.AsSpan();
            var splitName = firstNameSpan.Split(TeacherConstants.DoubleNameSeparator);
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
        }

        int i = TeacherLookupHelper.FindIndexOfBestMatch(s, ids, firstNameParts);
        return new(ids[i]);
    }

    public GroupId? Group(string fullName)
    {
        var group = s.ParseGroup(fullName);
        return Find<GroupId>(Lookup.Groups, group.Name);
    }

    private LookupModule Lookup
    {
        get
        {
            var l = s.LookupModule;
            Debug.Assert(l != null);
            return l;
        }
    }

    private T? Find<T>(Dictionary<string, int> dict, string val)
        where T : struct
    {
        if (!dict.TryGetValue(val, out var id))
        {
            return null;
        }
        Debug.Assert(Marshal.SizeOf<T>() == sizeof(int));
        T ret = Unsafe.As<int, T>(ref id);
        return ret;
    }
}


public static partial class ScheduleBuilderHelper
{
    public static void EnableLookupModule(this ScheduleBuilder s)
    {
        if (s.LookupModule is not null)
        {
            return;
        }

        var lookupModule = s.LookupModule = new();
        InitLookup(s, lookupModule);
        s.LookupModule = lookupModule;
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
                    coursesMap.Add(name, i);
                }
            }
        }
        {
            var teachersMap = lookup.TeachersByLastName;
            for (int i = 0; i < s.Teachers.Count; i++)
            {
                ref var teacher = ref s.Teachers.Ref(i);
                if (teacher.Name.LastName is not { } lastName)
                {
                    continue;
                }
                var list = teachersMap.AddOrGet(lastName);
                list.Add(i);
            }
        }
        {
            var groupsMap = lookup.Groups;
            for (int i = 0; i < s.Groups.Count; i++)
            {
                ref var group = ref s.Groups.Ref(i);
                groupsMap.Add(group.Name, i);
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
}
