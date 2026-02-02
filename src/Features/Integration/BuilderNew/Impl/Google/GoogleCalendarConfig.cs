using Anton.LayeredConfig;
using Anton.LayeredConfig.Retrieval;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Application.Config;

public sealed class GoogleCalendarConfig : IConfig<GoogleCalendarConfig>
{
    public static LayerConfigKey<GoogleCalendarConfig> Key { get; } = LayerConfigKey.Registry.Register<GoogleCalendarConfig>();
    public GoogleCredentialsConfig? Credentials { get; set; }
    public string? CalendarName { get; set; }

    public static void Register(IServiceCollection services)
    {
        services.RegisterBasicOperationsAndMergers<GoogleCalendarConfig>();
        services.AddConfigProvider(GoogleCalendarConfig.Key);
        services.AddConfigProvider(BuiltGoogleCalendarConfig.Key);
        services.AddMapper<GoogleCalendarConfigMapper>();
    }
}

public sealed class BuiltGoogleCalendarConfig
{
    public static LayerConfigKey<BuiltGoogleCalendarConfig> Key { get; set; } = new (GoogleCalendarConfig.Key.Value);
    public required GoogleCredentialsConfig Credentials { get; set; }
    public required string CalendarName { get; set; }
}

public sealed class GoogleCalendarConfigMapper : IConfigMapper<GoogleCalendarConfig, BuiltGoogleCalendarConfig>
{
    public BuiltGoogleCalendarConfig Map(GoogleCalendarConfig input)
    {
        if (input.Credentials is not { } credentials)
        {
            throw new InvalidOperationException("Credentials not provided for Calendar");
        }
        if (input.CalendarName is not { } calendarName)
        {
            calendarName = "Lessons";
        }
        else if (calendarName == "primary")
        {
            throw new InvalidOperationException("Using `primary` calendar might clear all your events! Don't");
        }

        return new()
        {
            Credentials = credentials,
            CalendarName = calendarName,
        };
    }
}

