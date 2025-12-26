using MainCli;
using MainCli.BuilderNew.Impl;
using MainCli.Helper;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib;
using ScheduleLib.Parsing;

var services = new ServiceCollection();
services.AddScheduleServices();
services.AddConfigsServices();
services.AddTaskHandlers();
services.AddGlobalConfiguration();

services.Configure<ManifestDirectoriesOptions>(x =>
{
    x.Directories.Add("data/topics");
});
services.Configure<StudyYearOptions>(x =>
{
    x.StudyYear = 2025;
    x.Semester = Semester.Sem2;
});
services.Configure<RegularSeminarDateConfig>(x =>
{
    x.Day = DayOfWeek.Wednesday;
    x.Time = new(hour: 15, minute: 00);
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
        AppTask.AllTeachersExcel,
        // Option.PerGroupAndPerTeacherPdfs,
        // Option.FreeRooms,
        // Option.CreateLessonsInRegistry,
        // Option.TableOfAllLabLessons,
        // Option.JsonSchedulesForWebsite,
        // Option.CopyGradesFromMoodleToRegistry,
    ],
    OutputDirectory = new OutputDirectory("output"),
    FreeRoomsExcelOutputFileName = "free_rooms.xlsx",
    AllTeachersOutputFileName = "all_teachers_orar.xlsx",
    CancellationToken = cancellationToken,
    RootServiceProvider = serviceProvider,
    TeacherName = NameHelper.Parse("Curmanschii Anton"),
};
await AppTasks.ExecuteMenu(appExecutionContext);
return;
