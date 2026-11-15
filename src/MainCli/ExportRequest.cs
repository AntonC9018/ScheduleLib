namespace ScheduleLib.Cli;

internal sealed record ExportRequest(
    SourceArguments? Source,
    OutputArguments Destination,
    ResultArguments Result,
    SettingsArguments Settings);

internal enum ScheduleExportKind
{
    Pdf,
    Ics,
}

internal enum TeacherExportKind
{
    LabDeadlines,
    PreDefense,
}
