using Anton.LayeredConfig;
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
    public static ApplicationConfigBuilder Configure(ApplicationConfigBuilder b)
    {
        // Allows to configure the defaults at this level.
        // they will take effect if later they are not overriden.
        b.Defaults.Configure(defaults =>
        {
            defaults.Registry().Configure(x =>
            {
                x.ExtraLessonAction(ExtraLessonInstanceAction.LeaveAlone);
                x.CommandDerivation<AnyDayDerivation>();
                x.ProcessingFlags(CommandProcessingConfig.Process
                    .WithLog(LessonEquationCommandTypes.All));
            });

            // Adds default source.
            // b.LessonTopics.AddSource<ManifestLessonTopicSource>(topics => ...)
            defaults.LessonTopics().Manifest(topics =>
            {
                // If someone else tries using topics.Manifest()
                // in their source config, it would bind to this one.
                _ = topics;
            });

            defaults.Registry().Credentials().FromConfig();
            defaults.Moodle().Credentials().FromConfig(isRequired: true);
        });

        // As an idea (ignore for now)
        // var scope = defaults.Scope(scope =>
        // {
        //     // This will always be added to all, even if not overridden.
        //     scope.LessonTopics.Manifest();
        // });

        b.Defaults.TeacherLayer("Anton Curmanschii", t =>
        {
            t.GoogleDrive();

            t.Registry().Configure(x =>
            {
                x.ProcessingFlags(CommandProcessingConfig.None);
            });

            t.LessonAttendance().Source(@"C:\Users\Anton\Desktop\lipse.xlsx", attendance =>
            {
                attendance.RepeatedCourseBehavior = RepeatedCourseBehavior.Error;
            });
            t.LessonTopics().Configure(topics =>
            {
                topics.Manifest();
                topics.FallbackProvider<NoNameProvider>(LessonType.Lab);
                t.LessonTopics().Manifest(m => m.Path("path.xlsx"));
            });
            t.Moodle().Credentials().FromConfig(isRequired: true);
        });

        b.Defaults.TeacherLayer("Tamara Iatasina", t =>
        {
            t.Moodle().Remove();
            t.LessonTopics().Remove();
        });

        return b;
    }
}
