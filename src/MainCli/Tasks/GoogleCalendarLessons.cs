using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using Google.Apis.Calendar.v3;
using MainCli.BuilderNew.Impl;
using ScheduleLib.OnlineRegistry;

namespace MainCli;

[AutoConstructor]
public sealed partial class GoogleCalendarLessons
{
    private readonly ScheduledDateTimeProvider _dateTimeProvider;
    private readonly ConfigProvider<BuiltGoogleDriveConfig> _driveConfigProvider;

    private static readonly string[] Scopes = [
        CalendarService.Scope.Calendar,
    ];
#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    public async Task Create()
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
    {
        var config = _driveConfigProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("No google drive config found.");
        }
        return;
    }
}
