using System.Diagnostics;
using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Topics;
using ScheduleLib.Dates;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Application.Core;

[AutoConstructor]
public sealed partial class AddLessonsToOnlineRegistryForCurrentTeacherTaskHandler
{
    private readonly ContextProvider _context;
    private readonly IOptions<StudyYearOptions> _studyYear;
    private readonly AddLessonsToOnlineRegistryTaskHandler _handler;
    private readonly StudentAttendanceLoader _attendanceLoader;
    private readonly CurrentTeacherLessonFilterProvider _lessonFilterProvider;
    private readonly LessonTopicsOfCurrentTeacherLoader _topicsLoader;

    // Maybe make a builder, make it possible to override default stuff,
    // then execute from builder (so it's not forced for the current teacher only).
    // Do the same thing for
    public async ValueTask Execute(CancellationToken cancellationToken)
    {
        var attendance = _attendanceLoader.Load();
        var topics = await _topicsLoader.Load(cancellationToken);

        // Passed manually, because this might be reconfigured to target another semester.
        var semester = _studyYear.Value.Semester;
        var handler = _handler;
        var navigator = await _context.CreateRegistryNavigator(cancellationToken);
        var lessonFilter = _lessonFilterProvider.Get();

        await handler.Run(new()
        {
            Navigator = navigator,
            Attendance = attendance,
            LessonTopics = topics,
            Semester = semester,
            LessonFilter = lessonFilter,
        });
    }
}

// Make a simple abstraction for now, because I don't know what I'm going to want to do.
public sealed class ContextProvider(IServiceProvider sp) : IDisposable
{
    // Don't even reuse for now.
    private RegistryScrapingContext _c1;

    private ValueTask<RegistryScrapingContext> MakeRegistryContext(CancellationToken cancellationToken)
    {
        return sp.MakeRegistryContext(cancellationToken);
    }

    public async ValueTask<OnlineRegistryNavigator> CreateRegistryNavigator(CancellationToken cancellationToken)
    {
        Debug.Assert(_c1.IsNull);
        _c1 = await MakeRegistryContext(cancellationToken);
        var ret = _c1.Navigator(sp, cancellationToken);
        return ret;
    }

    public void Dispose()
    {
        if (!_c1.IsNull)
        {
            _c1.Dispose();
        }
    }
}

[AutoConstructor]
public sealed partial class LessonTopicsOfCurrentTeacherLoader
{
    private readonly IServiceProvider _sp;
    private readonly DataProvider<LessonTopicsConfig> _lessonTopicsConfig;
    private readonly ScopeFilteredScheduleProvider _scheduleProvider;

    public async ValueTask<ILessonTopics> Load(CancellationToken cancellationToken)
    {
        var config1 = _lessonTopicsConfig.Get();
        if (config1 == null)
        {
            return NoLessonTopics.Instance;
        }

        var filteredSchedule = _scheduleProvider.Get();
        var builder = ActivatorUtilities.CreateInstance<AllLessonTopicsDatabaseBuilder>(_sp, filteredSchedule);
        foreach (var x in config1.Sources)
        {
            var source = x.Create(_sp);
            await source.Configure(builder, cancellationToken);
        }
        foreach (var x in config1.FallbackProviders)
        {
            builder.FallbackProvider(x.LessonType, x.Provider);
        }
        var topics = builder.Build();
        return topics;
    }
}

[AutoConstructor]
public sealed partial class CurrentTeacherLessonFilterProvider
{
    private readonly DataProvider<RegistryLessonFilterConfig> _lessonFilterConfig;
    private readonly IServiceProvider _sp;

    public ILessonFilter Get()
    {
        var b = _sp.LessonFilterBuilder().CurrentTeacher();

        if (_lessonFilterConfig.Get() is { } filterConfig)
        {
            if (filterConfig.SkipAttendance is { } att)
            {
                b = b.SkipAttendance(att);
            }
        }
        return b.Create();
    }
}
