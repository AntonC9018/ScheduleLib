using Google.Apis.Auth.OAuth2;
using Google.Apis.Auth.OAuth2.Responses;
using Google.Apis.Calendar.v3;
using Google.Apis.Calendar.v3.Data;
using Google.Apis.Http;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using Event = Google.Apis.Calendar.v3.Data.Event;

namespace ScheduleLib.Cli;

public sealed class GoogleCalendarSyncProvider : ICalendarSyncProvider
{
    private readonly CalendarService _service;
    public GoogleCalendarSyncProvider(CalendarService service)
    {
        _service = service;
        // Keep pre-send refresh, but remove UserCredential's response-triggered 401 replay.
        if (service.HttpClient.MessageHandler.Credential is { } credential)
            service.HttpClient.MessageHandler.Credential = new SingleAttemptCredential(credential);
        service.HttpClient.MessageHandler.NumTries = 1;
        service.HttpClient.MessageHandler.FollowRedirect = false;
    }
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Reliability", "CA2000", Justification = "Ownership transfers to the returned disposable provider.")]
    public static async Task<ICalendarSyncProvider> Connect(IServiceProvider services, BuiltGoogleCalendarConfig config, CancellationToken token)
    {
        var helper = Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions.GetRequiredService<GoogleApiHelper>(services);
        var credential = await helper.CredentialResolver.Resolve(config.Credentials.Build(), [CalendarService.Scope.Calendar], token);
        var initializer = helper.CreateServiceInitializer(credential);
        initializer.DefaultExponentialBackOffPolicy = ExponentialBackOffPolicy.None;
        return new GoogleCalendarSyncProvider(new CalendarService(initializer));
    }
    public async Task<string> GetAccount(CancellationToken token) => (await _service.Calendars.Get("primary").ExecuteAsync(token)).Id;
    public async Task<IReadOnlyList<CalendarDestination>> ListCalendars(CancellationToken token)
    {
        var result = new List<CalendarDestination>();
        string? page = null;
        do
        {
            var request = _service.CalendarList.List();
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
            var request = _service.Events.List(calendarId);
            request.PageToken = page;
            var response = await request.ExecuteAsync(token);
            result.AddRange((response.Items ?? []).Select(x => new CalendarEventIdentity(x.Id, x.Summary)));
            page = response.NextPageToken;
        } while (page is not null);
        return result;
    }
    public async Task DeleteCalendar(string calendarId, CancellationToken token) => await _service.Calendars.Delete(calendarId).ExecuteAsync(token);
    public async Task<string> CreateCalendar(string summary, CancellationToken token) => RequireCreatedId((await _service.Calendars.Insert(new Calendar
    { Summary = summary, Location = "Chișinău, Moldova", TimeZone = "Europe/Chisinau" }).ExecuteAsync(token)).Id);
    public async Task<string> CreateEvent(string calendarId, Event desired, CancellationToken token) => RequireCreatedId((await _service.Events.Insert(desired, calendarId).ExecuteAsync(token)).Id);
    private static string RequireCreatedId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new IOException("Calendar create returned no identity; outcome is uncertain.");
        return id;
    }

    private sealed class SingleAttemptCredential(IHttpExecuteInterceptor credential) : IHttpExecuteInterceptor
    {
        public async Task InterceptAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // This specific SDK condition otherwise throws InvalidOperationException before sending.
            if (credential is UserCredential user && string.IsNullOrEmpty(user.Token?.RefreshToken)
                && (user.Token is null || user.Token.IsStale))
                throw AuthorizationFailure();
            try { await credential.InterceptAsync(request, cancellationToken); }
            catch (TokenResponseException) { throw AuthorizationFailure(); }
        }
        private static Google.GoogleApiException AuthorizationFailure() => new("calendar", "Google authorization failed.")
            { HttpStatusCode = System.Net.HttpStatusCode.Unauthorized };
    }

    public void Dispose() => _service.Dispose();
}
