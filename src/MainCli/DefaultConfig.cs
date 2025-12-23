using MainCli.BuilderNew;
using MainCli.BuilderNew.Impl;
using MainCli.Topics;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;

namespace MainCli;

public static class DefaultConfig
{
    public static void AddDefaultConfig(this ApplicationConfigBuilder b)
    {
        b.Defaults.Configure(defaults =>
        {
            defaults.Registry().Configure(x =>
            {
                x.ExtraLessonAction(ExtraLessonInstanceAction.LeaveAlone);
                x.CommandDerivation<AnyDayDerivation>();
                x.ProcessingFlags(CommandProcessingConfig.Process
                    .WithLog(LessonEquationCommandTypes.All));
            });

            defaults.LessonTopics().Manifest(topics =>
            {
                _ = topics;
            });

            defaults.Registry().Credentials().FromConfig();
            defaults.Moodle().Credentials().FromConfig(isRequired: true);
        });

        b.Defaults.TeacherLayer("Anton Curmanschii", t =>
        {
            t.Drive();
            t.Registry().Configure(x =>
            {
                x.ProcessingFlags(CommandProcessingConfig.None);
            });
            t.LessonAttendance().Source(@"C:\Users\Anton\Desktop\lipse.xlsx", attendance =>
            {
                _ = attendance;
            });
        });

        b.Defaults.TeacherLayer("Tamara Iatasina", t =>
        {
            t.LessonTopics().Configure(x =>
            {
                x.FallbackProvider(LessonType.Lab, new LabAutoNumberingNameProvider());
            });
        });

        b.Defaults.TeacherLayer("Nichita Nartea", t =>
        {
            t.Moodle().NoInherit();

            t.LessonTopics().Configure(x =>
            {
                x.FallbackProvider(LessonType.Curs, new NoNameProvider());
                x.FallbackProvider(LessonType.Lab, new NoNameProvider());
            });
        });
    }
}
