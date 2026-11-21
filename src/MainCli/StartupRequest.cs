using CommandDotNet;

namespace ScheduleLib.Cli;

internal readonly record struct StartupRequest(bool Json, bool Jsonl, bool HelpRequested)
{
    public static StartupRequest None => default;

    public bool HasFormat => Json || Jsonl;

    public bool ConflictingFormats => Json && Jsonl;

    public bool CapturesDiagnostics
    {
        get
        {
            if (HelpRequested)
            {
                return false;
            }

            if (ConflictingFormats)
            {
                return true;
            }

            if (Jsonl)
            {
                return true;
            }

            return false;
        }
    }

    public bool UsesJsonlOutput
    {
        get
        {
            if (HelpRequested)
            {
                return false;
            }

            if (Json)
            {
                return false;
            }

            if (Jsonl)
            {
                return true;
            }

            return false;
        }
    }

    public bool RejectsFormatConflict
    {
        get
        {
            if (HelpRequested)
            {
                return false;
            }

            if (ConflictingFormats)
            {
                return true;
            }

            return false;
        }
    }

    public bool ShouldSuppressFailureHelp(bool commandInvoked, int exit)
    {
        if (commandInvoked)
        {
            return false;
        }

        if (exit == CliExit.Success)
        {
            return false;
        }

        if (HelpRequested)
        {
            return false;
        }

        if (HasFormat)
        {
            return true;
        }

        return false;
    }

    public static StartupRequest FromParsedInput(CommandDotNet.Parsing.ParseResult? parseResult)
    {
        var json = parseResult?.TargetCommand.HasInputValues("json") == true;
        var jsonl = parseResult?.TargetCommand.HasInputValues("jsonl") == true;
        var helpRequested = parseResult?.HelpWasRequested() == true;
        return new(
            Json: json,
            Jsonl: jsonl,
            HelpRequested: helpRequested);
    }
}
