using Microsoft.Extensions.Logging;

/// <summary>Records every log entry, including Debug, with its exception text, so tests can prove
/// that diagnostics are emitted and that nothing private reaches any log sink.</summary>
public sealed class DebugLogCapture : ILogger, ILoggerProvider
{
    public List<string> Entries { get; } = [];

    public string All => string.Join('\n', Entries);

    public ILogger CreateLogger(string categoryName) => this;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add($"{logLevel}: {formatter(state, exception)} {exception}");

    public void Dispose() => Entries.Clear();
}
