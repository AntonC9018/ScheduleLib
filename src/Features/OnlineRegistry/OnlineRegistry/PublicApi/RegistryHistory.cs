using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace ScheduleLib.OnlineRegistry;

public enum RegistryHistoryFailure { MarkupContract, AccessRejected, AuthenticationNotConfirmed, RequestedYearUnavailable, YearNotConfirmed, TransportStopped }
public sealed class RegistryHistoryException(string message, RegistryHistoryFailure failure = RegistryHistoryFailure.MarkupContract) : IOException(message)
{
    public RegistryHistoryFailure Failure { get; } = failure;
}
public sealed record RegistryHistoryYear(string Value, bool Selected, Uri Uri);
public sealed record RegistryHistoryCourse(string Name, int Semester, Uri Uri);
public sealed record RegistryHistoryGroup(string Name, Uri Uri);
public sealed record RegistryHistoryCourseSummary(string Name, int Semester, IReadOnlyList<string> Groups);
public sealed record RegistryHistoryYearSummary(string Year, IReadOnlyList<RegistryHistoryCourseSummary> Courses);

/// <summary>Parses observed registry markup without schedule matching or student/attendance extraction.</summary>
public static class RegistryHistoryHtml
{
    public static IReadOnlyList<RegistryHistoryYear> Years(IDocument document, Uri address)
    {
        var select = document.QuerySelector<IHtmlSelectElement>("select#AnUniv[name='AnUniv']");
        var form = select?.Form;
        if (select is null || form is null || form.GetAttribute("method") is { } method && !method.Equals("get", StringComparison.OrdinalIgnoreCase)
            || form.QuerySelectorAll("input, select, textarea, button").Any(x => x != select && x.HasAttribute("name")))
            throw new RegistryHistoryException("Academic-year GET selector is missing or unsupported; navigation requires inspection.");
        var action = SafeUri(address, form.GetAttribute("action") ?? address.AbsoluteUri);
        if (!action.AbsolutePath.Equals(address.AbsolutePath, StringComparison.Ordinal) || action.Query.Length != 0 || action.Fragment.Length != 0)
            throw new RegistryHistoryException("Academic-year form action is unsupported; navigation requires inspection.");
        var result = new List<RegistryHistoryYear>();
        foreach (var option in select.Options)
        {
            if (option.IsDisabled || string.IsNullOrWhiteSpace(option.Value)) continue;
            if (result.Any(x => x.Value == option.Value)) continue;
            result.Add(new(option.Value, option.IsSelected, new(action.AbsoluteUri + "?AnUniv=" + Uri.EscapeDataString(option.Value))));
        }
        if (result.Count == 0 || result.Count(x => x.Selected) != 1)
            throw new RegistryHistoryException("Academic-year selection is empty or ambiguous.");
        return result;
    }

    public static IReadOnlyList<RegistryHistoryCourse> Courses(IDocument document, Uri address)
    {
        if (document.GetElementById("nav-1") is null || document.GetElementById("nav-2") is null)
            throw new RegistryHistoryException("Semester course containers are missing; historical markup requires inspection.");
        var result = new List<RegistryHistoryCourse>();
        for (var semester = 1; semester <= 2; semester++)
        {
            var expected = document.QuerySelectorAll($"#nav-{semester} > div > span:nth-of-type(2) > a");
            if (document.QuerySelectorAll($"#nav-{semester} a[href]").Length != expected.Length)
                throw new RegistryHistoryException("Course container layout is unsupported; historical markup requires inspection.");
            foreach (var anchor in expected)
            {
                var uri = SafeUri(address, anchor.GetAttribute("href"));
                if (!uri.AbsolutePath.StartsWith(Root(address) + "LessonAttendance/groups/", StringComparison.Ordinal))
                    throw new RegistryHistoryException("Course link route is unsupported.");
                var name = anchor.TextContent.Trim();
                if (name.Length == 0) throw new RegistryHistoryException("Course link has no name.");
                if (!result.Any(x => x.Uri == uri && x.Semester == semester)) result.Add(new(name, semester, uri));
            }
        }
        return result;
    }

    public static IReadOnlyList<RegistryHistoryGroup> Groups(IDocument document, Uri address)
    {
        var container = document.QuerySelector("form[name='lesson'] > div.row:nth-of-type(2) > div.col:nth-of-type(1)");
        if (container is null)
            throw new RegistryHistoryException("Group container is missing; historical markup requires inspection.");
        var result = new List<RegistryHistoryGroup>();
        var rows = container.QuerySelectorAll(":scope > div.row");
        if (rows.Sum(x => x.QuerySelectorAll("a").Length) != container.QuerySelectorAll("a").Length)
            throw new RegistryHistoryException("Group container layout is unsupported; historical markup requires inspection.");
        foreach (var row in rows)
        {
            var anchors = row.QuerySelectorAll("a");
            if (anchors.Length != 2) throw new RegistryHistoryException("Group row has an unsupported link layout.");
            var uri = SafeUri(address, anchors[0].GetAttribute("href"));
            if (!uri.AbsolutePath.StartsWith(Root(address) + "LessonAttendance/LessonsList/", StringComparison.Ordinal))
                throw new RegistryHistoryException("Group link route is unsupported.");
            var name = anchors[0].TextContent.Trim();
            if (name.Length == 0) throw new RegistryHistoryException("Group link has no name.");
            if (!result.Any(x => x.Uri == uri)) result.Add(new(name, uri));
        }
        return result;
    }

    internal static string Root(Uri address)
    {
        var index = address.AbsolutePath.IndexOf("/LessonAttendance", StringComparison.Ordinal);
        if (index < 0) throw new RegistryHistoryException("Registry page address is unsupported.");
        return address.AbsolutePath[..(index + 1)];
    }

    private static Uri SafeUri(Uri address, string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(address, href, out var uri)
            || uri.Scheme != address.Scheme || uri.Authority != address.Authority || uri.UserInfo.Length != 0
            || !uri.AbsolutePath.StartsWith(Root(address), StringComparison.Ordinal) || uri.Fragment.Length != 0)
            throw new RegistryHistoryException("Registry navigation link is missing or outside the configured registry.");
        return uri;
    }
}

/// <summary>One global request gate covers authentication and reads across history accounts in this process.</summary>
public sealed class RegistryHistoryRequestHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastCompletion;
    private readonly Uri _index;
    private readonly Uri _login;
    private readonly TimeSpan _spacing;
    private bool _authenticated;
    private bool _loginAttempted;
    private bool _stopped;
    private bool _sent;

    public RegistryHistoryRequestHandler(HttpMessageHandler inner, Uri index, Uri login, TimeSpan spacing) : base(inner)
    {
        if (spacing <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(spacing));
        _index = index; _login = login; _spacing = spacing;
    }

    public void CompleteAuthentication() => _authenticated = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            var uri = request.RequestUri;
            var login = !_authenticated && !_loginAttempted && request.Method == HttpMethod.Post && uri == _login;
            if (_stopped || uri is null || uri.Scheme != _index.Scheme || uri.Authority != _index.Authority || uri.UserInfo.Length != 0
                || !login && (request.Method != HttpMethod.Get || !AllowedRead(uri)))
                throw new RegistryHistoryException("History session stopped or disallowed a request.", RegistryHistoryFailure.TransportStopped);
            if (login) _loginAttempted = true;
            for (var attempt = 0; ; attempt++)
            {
                var delay = _sent ? _lastCompletion + _spacing - DateTimeOffset.UtcNow : _spacing;
                if (delay > TimeSpan.Zero) await Task.Delay(delay, token);
                _sent = true;
                HttpResponseMessage? response = null;
                try
                {
                    response = await base.SendAsync(request, token);
                    await response.Content.LoadIntoBufferAsync(token);
                }
                catch (OperationCanceledException)
                {
                    response?.Dispose();
                    _stopped = true;
                    throw;
                }
                catch
                {
                    response?.Dispose();
                    _stopped = true;
                    throw new RegistryHistoryException("History transport failed; session stopped without replay.", RegistryHistoryFailure.TransportStopped);
                }
                finally { _lastCompletion = DateTimeOffset.UtcNow; }
                if (login && response.StatusCode == HttpStatusCode.Redirect) return response;
                if (!login && response.IsSuccessStatusCode) return response;
                var retry = !login && attempt == 0 && response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
                response.Dispose();
                if (retry) continue;
                _stopped = true;
                throw new RegistryHistoryException("Registry rejected history access; session stopped without authentication replay or redirect navigation.", RegistryHistoryFailure.AccessRejected);
            }
        }
        finally { Gate.Release(); }
    }

    private bool AllowedRead(Uri uri) => uri.AbsolutePath == _index.AbsolutePath
        || uri.AbsolutePath.StartsWith(RegistryHistoryHtml.Root(_index) + "LessonAttendance/groups/", StringComparison.Ordinal);
}

public sealed class RegistryHistoryReader(HttpClient client, Uri index)
{
    public async Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(string? year, CancellationToken token)
    {
        using var initial = await Read(index, token);
        var years = RegistryHistoryHtml.Years(initial, index);
        if (year is not null && !years.Any(x => x.Value == year))
            throw new RegistryHistoryException("Requested academic year is not offered by the registry selector.", RegistryHistoryFailure.RequestedYearUnavailable);
        var summaries = new List<RegistryHistoryYearSummary>();
        foreach (var selected in years.Where(x => year is null || x.Value == year))
        {
            using var fetched = selected.Selected ? null : await Read(selected.Uri, token);
            var document = fetched ?? initial;
            // Detect ignored year parameters rather than labelling current data as historical.
            if (!RegistryHistoryHtml.Years(document, index).Any(x => x.Selected && x.Value == selected.Value))
                throw new RegistryHistoryException("Registry did not confirm the requested academic year; discovery stopped.", RegistryHistoryFailure.YearNotConfirmed);
            var courses = new List<RegistryHistoryCourseSummary>();
            foreach (var course in RegistryHistoryHtml.Courses(document, index))
            {
                using var groups = await Read(course.Uri, token);
                courses.Add(new(course.Name, course.Semester, RegistryHistoryHtml.Groups(groups, course.Uri).Select(x => x.Name).ToArray()));
            }
            summaries.Add(new(selected.Value, courses));
        }
        return summaries;
    }

    private async Task<IDocument> Read(Uri uri, CancellationToken token)
    {
        using var response = await client.GetAsync(uri, token);
        if (!response.IsSuccessStatusCode) throw new RegistryHistoryException("History page request failed.", RegistryHistoryFailure.AccessRejected);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var document = await new HtmlParser().ParseDocumentAsync(stream, token);
        if (document.QuerySelector("input[type='password']") is not null
            || !document.QuerySelectorAll("a[href]").Any(x => x.GetAttribute("href")?.EndsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase) == true))
        {
            document.Dispose();
            throw new RegistryHistoryException("History page did not confirm authentication; discovery stopped.", RegistryHistoryFailure.AuthenticationNotConfirmed);
        }
        return document;
    }
}
