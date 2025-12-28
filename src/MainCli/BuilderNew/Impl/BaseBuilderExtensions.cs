using Anton.LayeredConfig;

namespace MainCli.BuilderNew.Impl;

public static partial class Extensions
{
    public static readonly Layer TeacherLayerKey = Layer.Registry.Register("Teacher");

    extension (ApplicationConfigLayerBuilder builder)
    {
        public ConfigBuilder<LessonTopicsConfig> LessonTopics() => builder.Builder<LessonTopicsConfig>();
        public ConfigBuilder<MoodleConfig> Moodle() => builder.Builder<MoodleConfig>();
        public ConfigBuilder<LessonAttendanceConfig> LessonAttendance() => builder.Builder<LessonAttendanceConfig>();
        public ConfigBuilder<GoogleDriveConfig> GoogleDrive() => builder.Builder(GoogleDriveConfig.Key);
        public ConfigBuilder<DeadlinesExcelConfig> DeadlinesExcel() => builder.Builder(DeadlinesExcelConfig.Key);
        public ConfigBuilder<LabTasksDatabaseConfig> LabTasks() => builder.Builder(LabTasksDatabaseConfig.Key);
    }
}
