using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using Event = Google.Apis.Calendar.v3.Data.Event;

namespace ScheduleLib.Cli;

public sealed class GoogleCalendarSyncProvider(CalendarService service) : ICalendarSyncProvider
{
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Ownership transfers to the returned disposable provider.")]
    public static async Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
    {
        var helper = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GoogleApiHelper>(services);
        var credential = await helper.CredentialResolver.Resolve(config.Credentials.Build(), [CalendarService.Scope.Calendar], token);
        return new GoogleCalendarSyncProvider(new CalendarService(helper.CreateServiceInitializer(credential)));
    }
    public async Task<string> GetAccount(CancellationToken token) => (await service.Calendars.Get("primary").ExecuteAsync(token)).Id;
    public async Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token)
    {
        var result = new List<CalendarDestination>();
        string? page = null;
        do
        {
            var request = service.CalendarList.List();
            request.PageToken = page;
            var response = await request.ExecuteAsync(token);
            result.AddRange((response.Items ?? []).Select(x => new CalendarDestination(x.Id, x.Summary, x.Primary == true)));
            page = response.NextPageToken;
        } while (page is not null);
        return result;
    }
    public async Task<IReadOnlyList<CalendarEventIdentity>> ListEvents(string calendarId, CancellationToken token)
    {
        var result = new List<CalendarEventIdentity>();
        string? page = null;
        do
        {
            var request = service.Events.List(calendarId);
            request.PageToken = page;
            var response = await request.ExecuteAsync(token);
            result.AddRange((response.Items ?? []).Select(x => new CalendarEventIdentity(x.Id, x.Summary)));
            page = response.NextPageToken;
        } while (page is not null);
        return result;
    }
    public async Task DeleteCalendar(string calendarId, CancellationToken token) => await service.Calendars.Delete(calendarId).ExecuteAsync(token);
    public async Task<string> CreateCalendar(string summary, CancellationToken token) => (await service.Calendars.Insert(new Calendar
    { Summary = summary, Location = "Chișinău, Moldova", TimeZone = "Europe/Chisinau" }).ExecuteAsync(token)).Id;
    public async Task<string> CreateEvent(string calendarId, Event desired, CancellationToken token) => (await service.Events.Insert(desired, calendarId).ExecuteAsync(token)).Id;
    public void Dispose() => service.Dispose();
}
