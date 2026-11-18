using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib.Application.Config;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Hosting;
using Serilog.Extensions.Logging;
using Serilog.Formatting.Display;

namespace ScheduleLib.Cli;

/// <summary>
/// One process-wide logger. Each command reloads it to write to its own output: JSONL records
/// for <c>--jsonl</c>, otherwise human-readable lines on stderr.
/// </summary>
public static class CliLogging
{
    private static readonly ReloadableLogger Logger = new LoggerConfiguration().WriteToConsole()
        .CreateBootstrapLogger();

    private static readonly SerilogLoggerFactory LoggerFactory = new(Logger);

    // Restored when a nested command finishes; commands run one at a time per process.
    private static CliOutput? _output;

    public static void Register(IServiceCollection services)
    {
        services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(Logger);
        });
    }

    public static Microsoft.Extensions.Logging.ILogger CreateLogger(string category) =>
        LoggerFactory.CreateLogger(category);

    /// <summary>Routes logs to <paramref name="output"/> until the result is disposed.</summary>
    public static IDisposable Use(CliOutput output)
    {
        var previous = _output;
        Apply(output);
        return new Restore(previous);
    }

    private static void Apply(CliOutput? output)
    {
        _output = output;
        Logger.Reload(configuration =>
        {
            if (output is { Jsonl: true } jsonl)
            {
                configuration.Enrich.FromLogContext();
                configuration.WriteTo.Sink(new CliOutputSink(jsonl));
                return configuration;
            }

            return configuration.WriteToConsole();
        });
    }

    private sealed class Restore(CliOutput? previous) : IDisposable
    {
        public void Dispose() => Apply(previous);
    }
}

/// <summary>Writes log events as JSONL records of a command's output.</summary>
public sealed class CliOutputSink(CliOutput output) : ILogEventSink
{
    private const string CategoryProperty = "SourceContext";
    private const string ScopeProperty = "Scope";
    private const string EventIdProperty = "EventId";

    // ":l" renders string values literally, without the quotes RenderMessage adds,
    // so messages read as they did through Microsoft.Extensions.Logging.
    private static readonly MessageTemplateTextFormatter MessageFormatter = new("{Message:l}");

    public void Emit(LogEvent logEvent)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (name, value) in logEvent.Properties)
        {
            if (name is CategoryProperty or ScopeProperty or EventIdProperty)
            {
                continue;
            }

            properties[name] = ToObject(value);
        }

        var message = RenderMessage(logEvent);
        var structuredProperties = properties.Count > 0 ? properties : null;
        var scopes = Scopes(logEvent);
        var category = Category(logEvent);
        output.WriteStructuredLog(
            Level(logEvent.Level),
            message: message,
            exception: logEvent.Exception?.ToString(),
            structuredProperties,
            scopes,
            category: category,
            timestamp: logEvent.Timestamp);
    }

    private static string RenderMessage(LogEvent logEvent)
    {
        using var writer = new StringWriter();
        MessageFormatter.Format(logEvent, writer);
        return writer.ToString();
    }

    private static LogLevel Level(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Verbose => LogLevel.Trace,
            LogEventLevel.Debug => LogLevel.Debug,
            LogEventLevel.Information => LogLevel.Information,
            LogEventLevel.Warning => LogLevel.Warning,
            LogEventLevel.Error => LogLevel.Error,
            _ => LogLevel.Critical,
        };
    }

    private static IReadOnlyList<string>? Scopes(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue(ScopeProperty, out var value))
        {
            return null;
        }

        if (value is not SequenceValue sequence)
        {
            return null;
        }

        return sequence.Elements
            .Select(element => ToObject(element)?.ToString() ?? string.Empty)
            .ToArray();
    }

    private static string? Category(LogEvent logEvent)
    {
        if (!logEvent.Properties.TryGetValue(CategoryProperty, out var value))
        {
            return null;
        }

        if (value is not ScalarValue { Value: string category })
        {
            return null;
        }

        return category;
    }

    private static object? ToObject(LogEventPropertyValue value)
    {
        return value switch
        {
            ScalarValue scalar => scalar.Value,
            SequenceValue sequence => sequence.Elements
                .Select(ToObject)
                .ToArray(),
            StructureValue structure => structure.Properties.ToDictionary(
                keySelector: property => property.Name,
                elementSelector: property => ToObject(property.Value),
                StringComparer.Ordinal),
            DictionaryValue dictionary => dictionary.Elements.ToDictionary(
                keySelector: pair => pair.Key.Value?.ToString() ?? string.Empty,
                elementSelector: pair => ToObject(pair.Value),
                StringComparer.Ordinal),
            _ => value.ToString(),
        };
    }
}
