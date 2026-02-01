using Anton.LayeredConfig;

namespace ScheduleLib.Application.Core.Config.Impl.Impl;

public static partial class Extensions
{
    public static readonly LayerName TeacherLayerKey = LayerName.Registry.Register("ProgrammableTeacher");

    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<LessonTopicsConfig> LessonTopics() => builder.Builder<LessonTopicsConfig>();
        public ConfigBuilder<MoodleConfig> Moodle() => builder.Builder<MoodleConfig>();
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance() => builder.Builder<LessonAttendanceConfig>();
        public ConfigBuilder<GoogleDriveConfig> GoogleDrive() => builder.Builder(GoogleDriveConfig.Key);
        public ConfigBuilder<GoogleCalendarConfig> GoogleCalendar() => builder.Builder(GoogleCalendarConfig.Key);
        public ConfigBuilder<DeadlinesExcelConfig> DeadlinesExcel() => builder.Builder(DeadlinesExcelConfig.Key);
        public ConfigBuilder<LabTasksDatabaseConfig> LabTasks() => builder.Builder(LabTasksDatabaseConfig.Key);
    }
}
