using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace ScheduleLib.Application.Config;

public static class ConsoleLogging
{
    /// <summary>Writes every log event to stderr in the human-readable format.</summary>
    public static LoggerConfiguration WriteToConsole(this LoggerConfiguration configuration)
    {
        configuration.Enrich.FromLogContext();
        configuration.WriteTo.Logger(w =>
        {
            w.Enrich.With(new RemovePropertyEnricher("EventId"));
            w.Enrich.With(new RemovePropertyEnricher("SourceContext"));
            w.WriteTo.Console(
                standardErrorFromLevel: LogEventLevel.Verbose,
                outputTemplate:
                "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        });
        return configuration;
    }
}

file sealed class RemovePropertyEnricher : ILogEventEnricher
{
    private readonly string _propertyName;
    public RemovePropertyEnricher(string propertyName) => _propertyName = propertyName;

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory factory)
        => logEvent.RemovePropertyIfPresent(_propertyName);
}
