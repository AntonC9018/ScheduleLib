using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Application.Config;

public sealed class LessonAttendanceSource
{
    public string? FilePath { get; set; }
    public RepeatedCourseBehavior? RepeatedCourseBehavior { get; set; }
    public CellValueFormat? CellValueFormat { get; set; }
}

public sealed class LessonAttendanceConfig : INodeData<LessonAttendanceConfig>
{
    public static NodeDataKey<LessonAttendanceConfig> Key { get; } = NodeDataKey.Registry.Register<LessonAttendanceConfig>();
    // Need a way to allow to remove an item by key.
    public List<LessonAttendanceSource> Sources { get; set; } = new();
    public Attendance? MissingDaysFiller { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.AddKeyEqualityComparer((LessonAttendanceSource s) => s.FilePath);
        services.RegisterBasicOperationsAndMergers<LessonAttendanceConfig>();
        services.AddConfigProvider(LessonAttendanceConfig.Key);
    }
}

public partial class Extensions
{
    extension (NodeDataBuilder<LessonAttendanceConfig> builder)
    {
        public void Source(Action<LessonAttendanceSource> configure)
        {
            builder.Source(null, configure);
        }

        public void Source(string? filePath, Action<LessonAttendanceSource> configure)
        {
            var config = builder.Value();
            // for now, use the file path as the id.
            var other = config.Sources.Find(other => other.FilePath == filePath);
            if (other is null)
            {
                other = new();
                other.FilePath = filePath;
                config.Sources.Add(other);
            }
            configure(other);
        }
    }
}
