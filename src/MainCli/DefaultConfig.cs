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

                var f = CommandProcessingConfig.DryRun
                    .WithLog(LessonEquationCommandTypes.All);
                x.ProcessingFlags(f);
            });

            defaults.LessonTopics().Manifest(topics =>
            {
                _ = topics;
            });

            defaults.Registry().Credentials().FromConfig();
            defaults.Moodle().Credentials().FromConfig(isRequired: true);

            defaults.DeadlinesExcel().ConfigureValue(x =>
            {
                x.BadColor = Color.Red;
                x.GoodColor = Color.LightGreen;
                x.LessonDelayLimit = 3;
                x.MaxTaskRows = 40;
                x.ColumnWidth = 5;
            });

            defaults.GoogleDrive().ConfigureValue(x =>
            {
                x.CredentialsPath = "google_token_store";
                x.DriveFolderName = "orar";
                x.SaveCredentials = true;
                x.ApiKeysSource = new ConfigurationApiKeysSource();
            });
        });

        b.Defaults.TeacherLayer("Curmanschii Anton", t =>
        {
            t.GoogleDrive();

            t.Registry().ConfigureLayer(x =>
            {
                _ = x;
            });
            t.LessonAttendance().Source(@"C:\Users\Anton\Desktop\lipse.xlsx", attendance =>
            {
                attendance.RepeatedCourseBehavior = RepeatedCourseBehavior.Error;
            });
            t.LabTasks().ConfigureValue(x =>
            {
                const string baseUrl = "https://github.com/AntonC9018/uniCourse_dataStructuresAndAlgorithms/blob/master/ru/labs/";

                x.Course("C++")
                    .Option(opt =>
                    {
                        opt.Language(Language.Ru);
                    })
                    .ManualSource(s =>
                    {
                        s.Add(new()
                        {
                            Name = "Архитектура компьютера",
                            Difficulty = 1,
                            Url = $"{baseUrl}common/01_computer_architecture.md",
                        });
                        s.Add(new()
                        {
                            Name = "lab 3",
                        });
                        s.Add(new()
                        {
                            Name = "lab 2",
                        });
                    });
            });
        });

        b.Defaults.TeacherLayer("Iatasina Tamara", t =>
        {
            t.LessonTopics().ConfigureLayer(x =>
            {
                x.FallbackProvider(LessonType.Lab, new LabAutoNumberingNameProvider());
            });
        });

        b.Defaults.TeacherLayer("Nartea Nichita", t =>
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
