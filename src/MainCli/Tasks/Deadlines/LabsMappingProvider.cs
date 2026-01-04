using System.Runtime.InteropServices;
using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using MainCli.BuilderNew.Impl;
using Microsoft.Extensions.Logging;
using ScheduleLib;
using ScheduleLib.Builders;

namespace MainCli;

using T = Dictionary<CourseId, List<LabsMappingProvider.LabMapping>>;

[AutoConstructor]
public sealed partial class LabsMappingProvider
{
    private readonly LookupFacade _lookup;
    private readonly ConfigProvider<LabTasksDatabaseConfig> _configProvider;
    private readonly ILogger _logger;
    private readonly IServiceProvider _sp;

    public async ValueTask<LabsValue> Get()
    {
        var config = _configProvider.Get();
        if (config == null)
        {
            return LabsValue.Empty;
        }
        var builder = new Dictionary<(CourseId CourseId, Option Option), LabTasksSource>();
        foreach (var s in config.Sources)
        {
            if (_lookup.Course(s.Key.CourseName.AsMemory()) is not { } courseId)
            {
                LogCourseNotFound(s.Key.CourseName);
                continue;
            }

            ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(
                builder,
                (courseId, s.Key.Option),
                out bool exists);
            if (exists)
            {
                if (s.Key == val!.Key)
                {
                    LogDuplicateKeys(s.Key, val.Key);
                }
                else
                {
                    LogCourseNamesMatch(s.Key.CourseName, val.Key.CourseName);
                }
            }
            val = s;
        }

        var ret = new T();
        foreach (var (k, s) in builder)
        {
            var list = await s.GetTasks(_sp);

            ref var val = ref CollectionsMarshal.GetValueRefOrAddDefault(ret, k.CourseId, out bool exists);
            if (!exists)
            {
                val = new();
            }

            var mapping = new LabMapping(k.Option, list);
            val!.Add(mapping);
        }

        return new(ret);
    }

    public readonly record struct LabMapping(
        Option Option,
        List<LabTask> Tasks);

    public readonly struct LabsValue
    {
        internal LabsValue(T dict) => _dict = dict;
        private readonly T _dict;

        public List<LabMapping>? Get(CourseId c) => _dict.GetValueOrDefault(c);
        public static LabsValue Empty => new([]);
    }

    [LoggerMessage(LogLevel.Warning, "No such course found {CourseName}")]
    partial void LogCourseNotFound(string CourseName);

    [LoggerMessage(LogLevel.Information, "Duplicate keys `{KeyA}` and `{KeyB}`. Selecting last")]
    partial void LogDuplicateKeys(SetKey KeyA, SetKey KeyB);

    [LoggerMessage(LogLevel.Warning, "Courses `{CourseNameA}` and `{CourseNameB}` mapped to the same course. Selecting last")]
    partial void LogCourseNamesMatch(string CourseNameA, string CourseNameB);
}
