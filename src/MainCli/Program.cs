using MainCli;
using MainCli.BuilderNew.Impl;
using MainCli.Helper;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Parsing;

var services = new ServiceCollection();
services.AddAllServices();

services.Configure<ManifestDirectoriesOptions>(x =>
{
    x.Directories.Add("data/topics");
});
services.Configure<StudyYearOptions>(x =>
{
    x.StudyYear = 2025;
    x.Semester = Semester.Sem1;
});
services.Configure<RegularSeminarDateConfig>(x =>
{
    x.Day = DayOfWeek.Wednesday;
    x.Time = new(hour: 15, minute: 00);
});
services.Configure<ScheduleBuilderInitializerOptions>(x =>
{
    x.BypassCache = true;
});

var serviceProvider = services.BuildServiceProvider(new ServiceProviderOptions
{
    ValidateOnBuild = true,
    ValidateScopes = true,
});

{
    var b = serviceProvider.GetRequiredService<ApplicationConfigBuilder>();
    b.AddDefaultConfig();
}

var cancellationToken = CancellationToken.None;
_ = cancellationToken;

var appExecutionContext = new AppTasksExecutionContext
{
    SelectedOptions = [
        // AppTask.UploadDocsToDrive,
        // AppTask.AllTeachersExcel,
        // AppTask.PerGroupAndPerTeacherPdfs,
        // AppTask.FreeRooms,
        AppTask.CreateLessonsInRegistry,
        // AppTask.TableOfAllLabLessons,
        // AppTask.JsonSchedulesForWebsite,
        // AppTask.CopyGradesFromMoodleToRegistry,
    ],
    OutputDirectory = new OutputDirectory("output"),
    FreeRoomsExcelOutputFileName = "free_rooms.xlsx",
    AllTeachersOutputFileName = "all_teachers_orar.xlsx",
    CancellationToken = cancellationToken,
    RootServiceProvider = serviceProvider,
    TeacherName = NameHelper.Parse("Iatasina Tamara"),
    MoodleQuizId = "317382",
};
await AppTasks.ExecuteMenu(appExecutionContext);
return;
