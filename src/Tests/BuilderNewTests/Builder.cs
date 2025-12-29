using MainCli.Topics;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;
using MainCli.BuilderNew.Impl;
using OnlineRegistry.AttendanceExcel;
using ScheduleLib.Scraping.Common.Config;

namespace Anton.LayeredConfig;

public static class TestBuilderHelper
{
    // Builder + dynamic object so that it could be configured from a UI.
    // Allows to get immutable config for specific things on demand
    // (so that running tasks are never affected).
    public static ApplicationConfigBuilder Configure(ApplicationConfigBuilder b)
    {
        // allows to configure the defaults at this level
        // they will take effect if later they are not overriden,
        b.Defaults.Configure(defaults =>
        {
            defaults.Registry().ConfigureLayer(x =>
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
            });

            defaults.Registry().Credentials().FromConfig();
            defaults.Moodle().Credentials().FromConfig(isRequired: true);

            // this is allowed too
            // t.Registry.Credentials.FromConfig(isRequired: true);
            // defaults.Registry.Configure(x =>
            // {
            //     x.Credentials.FromConfig(isRequired: true);
            // });
        });

        // As an idea (ignore for now)
        // var scope = defaults.Scope(scope =>
        // {
        //     // This will always be added to all, even if not overriddent.
        //     scope.LessonTopics.Manifest();
        // });
        b.Defaults.TeacherLayer("Anton Curmanschii", t =>
        {
            t.GoogleDrive();

            t.Registry().ConfigureLayer(x =>
            {
                x.ProcessingFlags(CommandProcessingConfig.None);
            });

            t.LessonAttendance().Source(@"C:\Users\Anton\Desktop\lipse.xlsx", attendance =>
            {
                attendance.RepeatedCourseBehavior = RepeatedCourseBehavior.Error;
                // a.Format(...) allows to reset the format.
                // each format has specific configurations like header configs for excel.
                // attendance.SetPath();
            });
            t.LessonTopics().ConfigureLayer(topics =>
            {
                topics.Manifest();
                // If this is set, it won't register the default source.
                // topics.SetPath();

                topics.FallbackProvider(LessonType.Lab, new NoNameProvider());

                // to configure for all, use
                // topics.FallbackProvider(new NoNameProvider);

                // also allowed:
                // topics.FallbackProvider<NoNameProvider>(LessonType.Lab);
            });

            t.Moodle().Credentials().FromConfig(isRequired: true);
            // t.Moodle.Configure(moodle =>
            // {
            //     moodle.Credentials.FromConfig(isRequired: true);
            // });
        });

        b.Defaults.TeacherLayer("Tamara Iatasina", t =>
        {
            t.Moodle().Remove();
            t.LessonTopics().Remove();
        });

        return b;
    }
}
