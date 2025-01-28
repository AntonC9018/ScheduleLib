using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public LookupModule? LookupModule = null;
}

public sealed class LookupModule()
{
    public readonly Dictionary<string, int> Courses = new(StringComparer.CurrentCultureIgnoreCase);
    public readonly TeachersByLastName TeachersByLastName = new();
    public readonly Dictionary<string, int> Groups = new(StringComparer.OrdinalIgnoreCase);
}

public struct LookupFacade(ScheduleBuilder s)
{
    public CourseId? Course(string name) => Find<CourseId>(Lookup.Courses, name);
    public TeacherId? Teacher(Word firstName, string lastName)
    {
        if (Lookup.TeachersByLastName.Get(lastName) is not { } ids)
        {
            return null;
        }
        int i = TeacherLookupHelper.FindIndexOfBestMatch(s, ids, firstName);
        return new(ids[i]);
    }

    public GroupId? Group(string name) => Find<GroupId>(Lookup.Groups, name);

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

        {
            var coursesMap = lookupModule.Courses;
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
            var teachersMap = lookupModule.TeachersByLastName;
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
            var groupsMap = lookupModule.Groups;
            for (int i = 0; i < s.Groups.Count; i++)
            {
                ref var group = ref s.Groups.Ref(i);
                groupsMap.Add(group.Name, i);
            }
        }
    }

    public static LookupFacade Lookup(this ScheduleBuilder s)
    {
        s.EnableLookupModule();
        return new(s);
    }
}
