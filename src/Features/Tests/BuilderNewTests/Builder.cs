using Anton.LayeredData;
using ScheduleLib.Application.Core.Topics;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Application.Config;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Scraping.Common.Config;

public static class TestBuilderHelper
{
    // Builder + dynamic object so that it could be configured from a UI.
    // Allows to get immutable config for specific things on demand
    // (so that running tasks are never affected).
    public static TreeBuilder Configure(TreeBuilder b)
    {
        b.Defaults.Configure(defaults =>
        {
            defaults.Registry()
                .Configure(x =>
                {
                    x.ExtraLessonAction(ExtraLessonInstanceAction.LeaveAlone);
                    x.CommandDerivation<AnyDayDerivation>();

                    x.ProcessingFlags(CommandProcessingConfig.Process
                        .WithLog(LessonEquationCommandTypes.All));
                });

            defaults.LessonTopics()
                .Manifest(topics =>
                {
                    // If someone else tries using topics.Manifest()
                    // in their source config, it would bind to this one.
                    _ = topics;
                });

            defaults.Registry()
                .Credentials()
                .FromConfig();
            defaults.Moodle()
                .Credentials()
                .FromConfig();
        });

        b.Defaults.TeacherLayer(
            "Anton Curmanschii",
            t =>
            {
                t.GoogleDrive();

                t.Registry()
                    .Configure(x =>
                    {
                        x.ProcessingFlags(CommandProcessingConfig.None);
                    });

                t.LessonAttendance()
                    .Source(
                        Path.Join(
                            path1: Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            path2: "Desktop",
                            path3: "lipse.xlsx"),
                        attendance =>
                        {
                            attendance.RepeatedCourseBehavior = RepeatedCourseBehavior.Error;
                        });
                t.LessonTopics()
                    .Configure(topics =>
                    {
                        topics.Manifest();
                        topics.FallbackProvider<NoNameProvider>(LessonType.Lab);
                        t.LessonTopics()
                            .Manifest(m => m.Path("path.xlsx"));
                    });
                t.Moodle()
                    .Credentials()
                    .FromConfig();
            });

        b.Defaults.TeacherLayer(
            "Tamara Iatasina",
            t =>
            {
                t.Moodle()
                    .Remove();
                t.LessonTopics()
                    .Remove();
            });

        return b;
    }
}
