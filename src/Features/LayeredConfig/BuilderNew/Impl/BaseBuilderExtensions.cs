using Anton.LayeredData;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Application.Config;

public static class LayerKeys
{
    public static readonly Layer TeacherLayerKey = Layer.Registry.Register("ProgrammableTeacher");
}
public static partial class Extensions
{
    extension (NodeBuilder builder)
    {
        public NodeDataBuilder<LessonTopicsConfig> LessonTopics() => builder.Builder<LessonTopicsConfig>();
        public NodeDataBuilder<MoodleConfig> Moodle() => builder.Builder<MoodleConfig>();
        public NodeDataBuilder<LessonAttendanceConfig> LessonAttendance() => builder.Builder<LessonAttendanceConfig>();
        public NodeDataBuilder<GoogleDriveConfig> GoogleDrive() => builder.Builder(GoogleDriveConfig.Key);
        public NodeDataBuilder<GoogleCalendarConfig> GoogleCalendar() => builder.Builder(GoogleCalendarConfig.Key);
        public NodeDataBuilder<DeadlinesExcelConfig> DeadlinesExcel() => builder.Builder(DeadlinesExcelConfig.Key);
        public NodeDataBuilder<LabTasksDatabaseConfig> LabTasks() => builder.Builder(LabTasksDatabaseConfig.Key);
    }
}
