using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public LookupModule? LookupModule = null;
}

public sealed class LessonsByCourseMap : List<List<AnyLessonId>>
{
    public List<AnyLessonId> this[CourseId courseId]
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

public static partial class ScheduleBuilderHelper
{
    public static LookupModule EnableLookupModule(this ScheduleBuilder s)
    {
        if (s.LookupModule is not null)
        {
            return s.LookupModule;
        }

        var lookupModule = s.LookupModule = new();
        s.InitLookup(lookupModule);
        s.LookupModule = lookupModule;
        return lookupModule;
    }

    public static void RefreshLookup(this ScheduleBuilder s)
    {
        if (s.LookupModule is null)
        {
            s.EnableLookupModule();
            return;
        }

        s.LookupModule.Clear();
        s.InitLookup(s.LookupModule);
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
            for (int i = 0; i < s.WeeklyLessons.Count; i++)
            {
                ref var lesson = ref s.WeeklyLessons.Ref(i);
                if (lesson.General.Course is not { } courseId)
                {
                    continue;
                }
                var list = lookup.LessonsByCourse[courseId.Id];
                list.Add(new(LessonRegularity.Weekly, i));
            }
            for (int i = 0; i < s.OneTimeLessons.Count; i++)
            {
                ref var lesson = ref s.OneTimeLessons.Ref(i);
                if (lesson.General.Course is not { } courseId)
                {
                    continue;
                }
                var list = lookup.LessonsByCourse[courseId.Id];
                list.Add(new(LessonRegularity.OneTime, i));
            }
        }
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
