using ScheduleLib.Curriculum.Download;

var cancellationToken = CancellationToken.None;
var c = CredentialHelper.CreateDefaultConfiguration(
    typeof(Program).Assembly);
await CurriculaDownloadTasks.PullCurriculaToDisk(
    c.GetMicrosoftGraphAuth(),
    cancellationToken: cancellationToken);

return;
