namespace ScheduleLib.Cli;

/// <summary>The data and presentation of a completed invocation. The context owns emission.</summary>
public sealed record CliCompletion<T>(int ExitCode, string[] Errors, T Data)
{
    public object? Summary { get; init; }

    public string[] Paths { get; init; } = [];

    public string[] Actions { get; init; } = [];

    public IReadOnlyList<ICliMessage>? ActionMessages { get; init; }

    public string[]? Warnings { get; init; }

    public string? Status { get; init; }

    public string? JsonStatus { get; init; }

    public Action<string>? RenderHuman { get; init; }

    public bool WarningsFirst { get; init; }

    public Func<string, bool>? ShouldWriteWarning { get; init; }
}
