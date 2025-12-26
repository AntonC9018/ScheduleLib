using System.Drawing;
using Anton.LayeredConfig;
using MainCli.BuilderNew.Impl;
using MainCli.Topics;
using OnlineRegistry.AttendanceExcel;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common.Config;

namespace MainCli;

public static class DefaultConfig
{
    public static void AddDefaultConfig(this ApplicationConfigBuilder b)
    {
        b.Defaults.Configure(defaults =>
        {
            defaults.Registry().ConfigureLayer(x =>
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

            defaults.DeadlinesExcel().Configure(x =>
            {
                x.BadColor = Color.Red;
                x.GoodColor = Color.LightGreen;
                x.LessonDelayLimit = 3;
                x.MaxTaskRows = 40;
                x.ColumnWidth = 5;
            });
        });

        b.Defaults.TeacherLayer("Anton Curmanschii", t =>
        {
            t.Drive();
            t.Registry().ConfigureLayer(x =>
            {
                x.ProcessingFlags(CommandProcessingConfig.None);
            });
            t.LessonAttendance().Source(@"C:\Users\Anton\Desktop\lipse.xlsx", attendance =>
            {
                attendance.RepeatedCourseBehavior = RepeatedCourseBehavior.Error;
            });
        });

        b.Defaults.TeacherLayer("Tamara Iatasina", t =>
        {
            t.LessonTopics().ConfigureLayer(x =>
            {
                x.FallbackProvider(LessonType.Lab, new LabAutoNumberingNameProvider());
            });
        });

        b.Defaults.TeacherLayer("Nichita Nartea", t =>
        {
            t.Moodle().NoInherit();

            t.LessonTopics().ConfigureLayer(x =>
            {
                x.FallbackProvider(LessonType.Curs, new NoNameProvider());
                x.FallbackProvider(LessonType.Lab, new NoNameProvider());
            });
        });
    }
}
