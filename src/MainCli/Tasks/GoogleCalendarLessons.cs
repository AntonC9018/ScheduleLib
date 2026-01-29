using System.Diagnostics.CodeAnalysis;
using Anton.LayeredConfig.Retrieval;
using AutoConstructor.Attributes;
using Google;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Services;
using MainCli.BuilderNew.Impl;
using Microsoft.Extensions.Options;
using ScheduleLib;
using ScheduleLib.Generation;
using ScheduleLib.OnlineRegistry;
using Event = Google.Apis.Calendar.v3.Data.Event;

namespace MainCli;

[AutoConstructor]
public sealed partial class UpdateLessonsInGoogleCalendarTaskHandler
{
    private readonly ScheduledTimeEventsProvider _eventsProvider;
    private readonly ConfigProvider<BuiltGoogleCalendarConfig> _configProvider;
    private readonly IOptions<StudyYearOptions> _studyYearOptions;
    private readonly ScopeFilteredScheduleProvider _filteredScheduleProvider;
    private readonly LessonTextDisplayHandler.Services _lessonDisplayServices;
    private readonly LessonTimeConfig _timeConfig;
    private readonly GoogleApiHelper _helper;

    private static readonly string[] Scopes = [
        CalendarService.Scope.Calendar,
    ];

    public async Task Update(CancellationToken cancellationToken)
    {
        var config = _configProvider.Get();
        if (config is null)
        {
            throw new InvalidOperationException("No google drive config found.");
        }
        if (config.CalendarName == "primary")
        {
            throw new InvalidOperationException("Primary calendar not supported!");
        }

        var credential = await _helper.CredentialResolver.Resolve(
            config.Credentials.Build(),
            Scopes,
            cancellationToken);
        using var service = new CalendarService(_helper.CreateServiceInitializer(credential));

        var lessonDisplay = new LessonTextDisplayHandler(_lessonDisplayServices, new()
        {
            PrintsTeacherName = false,
            PrintsGroupNames = true,
            PrintsSubGroup = true,
        });

        var filteredSchedule = _filteredScheduleProvider.Get();
        var lessons = filteredSchedule.Lessons;
        var timeEvents = _eventsProvider
            .Get(new()
            {
                Semester = _studyYearOptions.Value.Semester,
                Lessons = lessons,
            })
            .OrderBy(x => x.Event.First);

        const string locationId = "Chișinău, Moldova";
        const string timeZoneId = "Europe/Chisinau";
        var calendarId = await service.MakeSureCleanCalendarWithSummary(new Calendar
        {
            Summary = config.CalendarName,
            Location = locationId,
            TimeZone = timeZoneId,
        }, cancellationToken);

        using var runner = _helper.RunnerProvider.Create(cancellationToken);
        foreach (var timeEvent in timeEvents)
        {
            // ReSharper disable once VariableHidesOuterVariable
            runner.Add([SuppressMessage("ReSharper", "AccessToDisposedClosure")] async (cancellationToken) =>
            {
                var schedule = filteredSchedule.Source;
                var lesson = schedule.Get(timeEvent.LessonId);
                var course = schedule.Get(lesson.Lesson.Course);

                DateTimeOffset DateTimeFirst(TimeOnly time)
                {
                    var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
                    var date = timeEvent.Event.First;
                    var dateTime = new DateTime(date, time);
                    return new(dateTime, zone.GetUtcOffset(dateTime));
                }
                // var date = timeEvent.Event.First.ToString("yyyy-MM-dd");
                var notRichText = new NotRichText();
                lessonDisplay.Handle(new()
                {
                    Lesson = lesson,
                    StringBuilder = new(),
                    ColumnWidth = 1,
                    LessonTimeConfig = _timeConfig,
                    Schedule = filteredSchedule.Source,
                    TextDescriptor = notRichText,
                });

                string[] GetRecurrence()
                {
                    var e = timeEvent.Event;
                    if (!e.IsRepeated)
                    {
                        return [];
                    }
                    return [$"RRULE:FREQ=DAILY;INTERVAL={e.DayInterval};COUNT={e.Count}"];
                }

                var ev = new Event
                {
                    Summary = course.FullName,
                    Start = new()
                    {
                        TimeZone = timeZoneId,
                        DateTimeDateTimeOffset = DateTimeFirst(timeEvent.TimeInterval.Start),
                    },
                    End = new()
                    {
                        TimeZone = timeZoneId,
                        DateTimeDateTimeOffset = DateTimeFirst(timeEvent.TimeInterval.End),
                    },
                    Description = notRichText.GetString(),
                    Location = locationId,
                    Recurrence = GetRecurrence(),
                    ColorId = "11", // tomato
                };

                await service.Events.Insert(ev, calendarId).ExecuteAsync(cancellationToken);
            });
        }
        await runner.WhenDone();
        return;
    }
}

internal static class GoogleCalendarServiceExtensions
{
    public static async Task<bool> CheckCalendarExists(
        this CalendarService service,
        string calendarName,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await service.CalendarList.Get(calendarName).ExecuteAsync(cancellationToken);
            _ = response;
            return true;
        }
        catch (GoogleApiException)
        {
            return false;
        }
    }

    public static async Task<string> MakeSureCleanCalendarWithSummary(
        this CalendarService service,
        Calendar calendar,
        CancellationToken cancellationToken)
    {
        var calendars = await service.CalendarList.List().ExecuteAsync(cancellationToken);

        if (calendars.Items.FirstOrDefault(x => x.Summary == calendar.Summary) is { } c)
        {
            await service.Calendars.Delete(c.Id).ExecuteAsync(cancellationToken);
        }
        var result = await service.Calendars.Insert(calendar).ExecuteAsync(cancellationToken);
        return result.Id;
    }
}
