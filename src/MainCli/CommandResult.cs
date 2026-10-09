namespace ScheduleLib.Cli;

public sealed record CommandResult<T>(int SchemaVersion, string Command, string RunId, string Status,
    int ExitCode, string[] Outputs, string[] Actions, string[] Warnings, string[] Errors, T Data);
