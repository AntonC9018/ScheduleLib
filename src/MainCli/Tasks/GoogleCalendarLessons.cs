using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using Google.Apis.Calendar.v3;
using MainCli.BuilderNew.Impl;
using ScheduleLib.OnlineRegistry;

namespace MainCli;

[AutoConstructor]
public sealed partial class GoogleCalendarLessons
{
    private readonly ScheduledTimeEventsProvider _eventsProvider;
    private readonly ConfigProvider<GoogleCalendarConfig> _configProvider;

    private static readonly string[] Scopes = [
        CalendarService.Scope.Calendar,
    ];

    public async Task Create()
    {
        var config = _configProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("No google drive config found.");
        }


        return;
    }
}
