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
        // AppTask.UploadDocsToDrive,
        // AppTask.AllTeachersExcel,
        // AppTask.PerGroupAndPerTeacherPdfs,
        // AppTask.FreeRooms,
        // AppTask.CreateLessonsInRegistry,
        // AppTask.TableOfAllLabLessons,
        AppTask.JsonSchedulesForWebsite,
        // AppTask.CopyGradesFromMoodleToRegistry,
        // AppTask.UpdateCalendar,
        // AppTask.ListOfThesesPerTeacherForWebsite,
    ],
    OutputDirectory = new OutputDirectory("output"),
    FreeRoomsExcelOutputFileName = "free_rooms.xlsx",
    AllTeachersOutputFileName = "all_teachers_orar.xlsx",
    CancellationToken = cancellationToken,
    RootServiceProvider = serviceProvider,
    TeacherName = NameHelper.Parse("Curmanschii Anton"),
    MoodleQuizId = "317382",
};
await AppTasks.ExecuteMenu(appExecutionContext);
return;
