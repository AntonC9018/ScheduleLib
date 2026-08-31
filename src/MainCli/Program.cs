using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Parsing;

var services = new ServiceCollection();
AppConfiguration.ConfigureServices(services);
var serviceProvider = AppConfiguration.BuildServiceProvider(services);
AppConfiguration.ConfigureLayeredConfig(serviceProvider);

var cancellationToken = CancellationToken.None;
_ = cancellationToken;

var appExecutionContext = new AppTasksExecutionContext
{
    SelectedOptions = [
        AppTask.UploadDocsToDrive,
        // AppTask.AllTeachersExcel,
        // AppTask.PerGroupAndPerTeacherPdfs,
        // AppTask.FreeRooms,
        // AppTask.CreateLessonsInRegistry,
        // AppTask.TableOfAllLabLessons,
        // AppTask.JsonSchedulesForWebsite,
        // AppTask.CopyGradesFromMoodleToRegistry,
        AppTask.UpdateCalendar,
        // AppTask.ListOfThesesPerTeacherForWebsite,
        // AppTask.CreatePredzashitaExcels,
        // AppTask.Query,
    ],
    OutputDirectory = new OutputDirectory("output"),
    FreeRoomsExcelOutputFileName = "free_rooms.xlsx",
    AllTeachersOutputFileName = "all_teachers_orar.xlsx",
    CancellationToken = cancellationToken,
    RootServiceProvider = serviceProvider,
    TeacherName = NameHelper.Parse("Curmanschii Anton"),
    MoodleQuizId = "317382",
};

// For now just do this, we're only executing it locally currently.
Directory.SetCurrentDirectory(AppContext.BaseDirectory);

await AppTasks.ExecuteMenu(appExecutionContext);
return;
