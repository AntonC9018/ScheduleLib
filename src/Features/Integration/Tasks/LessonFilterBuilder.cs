using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Application.Core;

public readonly struct LessonFilterBuilder
{
    public ILessonFilter Current { get; init; } = ShouldAlwaysProcessLessonFilter.Instance;
    public readonly IServiceProvider ServiceProvider;

    public LessonFilterBuilder(IServiceProvider serviceProvider)
    {
        ServiceProvider = serviceProvider;
    }

    public LessonFilterBuilder WithCurrent(ILessonFilter newCurrent)
    {
        return new(ServiceProvider)
        {
            Current = newCurrent,
        };
    }
}

public static class LessonFilterBuilderExtensions
{
    extension (IServiceProvider sp)
    {
        public LessonFilterBuilder LessonFilterBuilder() => new(sp);
    }
    extension (LessonFilterBuilder builder)
    {
        public ILessonFilter Create() => builder.Current;

        public LessonFilterBuilder Wrap<T>(T value) where T : ILessonFilter
        {
            return builder.WithCurrent(new LessonFilterDecorator<T>
            {
                Main = value,
                Wrapped = builder.Current,
            });
        }

        public LessonFilterBuilder SkipAttendance(AttendanceMode attendance)
        {
            return builder.Wrap(new SkipAttendanceLessonFilter(attendance));
        }

        public LessonFilterBuilder CurrentTeacher()
        {
            var n = ActivatorUtilities.CreateInstance<CurrentTeacherLessonFilter>(builder.ServiceProvider);
            return builder.Wrap(n);
        }
    }
}

public sealed class LessonFilterDecorator<T> : ILessonFilter
    where T : ILessonFilter
{
    public required T Main { get; init; }
    public required ILessonFilter Wrapped { get; init; }

    public LessonValidity Filter(LessonFilterContext c)
    {
        var x = Main.Filter(c);
        if (x.Decision != LessonValidityDecision.None)
        {
            return x;
        }
        x = Wrapped.Filter(c);
        return x;
    }
}

[AutoConstructor]
public sealed partial class SkipAttendanceLessonFilter : ILessonFilter
{
    public AttendanceMode Attendance { get; }

    public LessonValidity Filter(LessonFilterContext c)
    {
        var g = c.Schedule.Get(c.Filter.Groups.Value.Group0);
        if (g.AttendanceMode == Attendance)
        {
            return LessonValidity.Skip;
        }
        return LessonValidity.None;
    }
}

[AutoConstructor]
public sealed partial class CurrentTeacherLessonFilter : ILessonFilter
{
    private readonly CurrentTeacherIdProvider _currentTeacherIdProvider;

    public LessonValidity Filter(LessonFilterContext c)
    {
        var teacherId = _currentTeacherIdProvider.Get();
        var s = c.Schedule;
        if (c.Lessons.Count == 0)
        {
            return LessonValidity.None;
        }

        if (AreAllForOtherTeachers(c))
        {
            var lessonId = c.Lessons[0];
            ref readonly var lesson = ref c.Schedule.Get(lessonId).Lesson;
            return LessonValidity.Error(new
            {
                Message = "Lesson belongs to another teacher",
                Lesson = new
                {
                    Id = c.Lessons[0],
                    Course = c.Schedule.Get(lesson.Course).FullName,
                    Teachers = string.Join(",", lesson.Teachers.Select(x => s.Get(x).PersonName.ToString())),
                    Groups = string.Join(",", lesson.Groups.Select(x => s.Get(x).Name.ToString())),
                    Type = lesson.Type,
                },
            });
        }
        {
            // Remove lessons for other teachers.
            c.Lessons.RemoveAll(x => !s.Get(x).Lesson.Teachers.Contains(teacherId));
        }
        return LessonValidity.None;

        bool AreAllForOtherTeachers(LessonFilterContext c1)
        {
            foreach (var lid in c1.Lessons)
            {
                var lesson = c1.Schedule.Get(lid);
                if (lesson.Lesson.Teachers.Contains(teacherId))
                {
                    return false;
                }
            }
            return true;
        }
    }
}

public sealed class RegistryLessonFilterConfig : INodeData<RegistryLessonFilterConfig>
{
    public static NodeDataKey<RegistryLessonFilterConfig> Key { get; } = NodeDataKey.Registry.Register<RegistryLessonFilterConfig>();
    public AttendanceMode? SkipAttendance { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.RegisterBasicOperationsAndMergers<RegistryLessonFilterConfig>();
        services.AddConfigProvider(RegistryLessonFilterConfig.Key);
    }
}
