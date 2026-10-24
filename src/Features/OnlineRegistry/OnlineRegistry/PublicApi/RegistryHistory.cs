using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace ScheduleLib.OnlineRegistry;

public enum RegistryHistoryFailure { MarkupContract, AccessRejected, AuthenticationNotConfirmed, RequestedYearUnavailable, YearNotConfirmed, TransportStopped }
public enum RegistryHistoryPhase { Unknown, Login, Index, Groups, Lessons }
public enum RegistryHistoryDiagnostic
{
    None, YearSelector, YearSelection, CourseContainers, CourseLayout, CourseLink, CourseName,
    GroupContainer, GroupLayout, GroupRowLinks, GroupLink, GroupName, TopicTable,
    YearSelectionNotConfirmed, AuthenticationPage, HttpRejected, Transport, RequestPolicy
}
public sealed class RegistryHistoryException(
    string message,
    RegistryHistoryFailure failure = RegistryHistoryFailure.MarkupContract,
    RegistryHistoryPhase phase = RegistryHistoryPhase.Unknown,
    int? statusCode = null,
    RegistryHistoryDiagnostic diagnostic = RegistryHistoryDiagnostic.None) : IOException(message)
{
    public RegistryHistoryFailure Failure { get; } = failure;
    public RegistryHistoryPhase Phase { get; } = phase;
    public int? StatusCode { get; } = statusCode;
    public RegistryHistoryDiagnostic Diagnostic { get; } = diagnostic;
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
            throw new RegistryHistoryException("Academic-year GET selector is missing or unsupported; navigation requires inspection.", phase: RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.YearSelector);
        var action = SafeUri(address, form.GetAttribute("action") ?? address.AbsoluteUri);
        if (!action.AbsolutePath.Equals(address.AbsolutePath, StringComparison.Ordinal) || action.Query.Length != 0 || action.Fragment.Length != 0)
            throw new RegistryHistoryException("Academic-year form action is unsupported; navigation requires inspection.", phase: RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.YearSelector);
        var result = new List<RegistryHistoryYear>();
        foreach (var option in select.Options)
        {
            if (option.IsDisabled || string.IsNullOrWhiteSpace(option.Value)) continue;
            if (result.Any(x => x.Value == option.Value)) continue;
            result.Add(new(option.Value, option.IsSelected, new((string) $"{action.AbsoluteUri}?AnUniv={Uri.EscapeDataString(option.Value)}")));
        }
        if (result.Count == 0 || result.Count(x => x.Selected) != 1)
            throw new RegistryHistoryException("Academic-year selection is empty or ambiguous.", phase: RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.YearSelection);
        return result;
    }

    public static IReadOnlyList<RegistryHistoryCourse> Courses(IDocument document, Uri address)
    {
        if (document.GetElementById("nav-1") is null || document.GetElementById("nav-2") is null)
            throw new RegistryHistoryException("Semester course containers are missing; historical markup requires inspection.", phase: RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.CourseContainers);
        var result = new List<RegistryHistoryCourse>();
        for (var semester = 1; semester <= 2; semester++)
        {
            var expected = document.QuerySelectorAll($"#nav-{semester} > div > span:nth-of-type(2) > a");
            if (document.QuerySelectorAll($"#nav-{semester} a[href]").Length != expected.Length)
                throw new RegistryHistoryException("Course container layout is unsupported; historical markup requires inspection.", phase: RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.CourseLayout);
            foreach (var anchor in expected)
            {
                var uri = SafeUri(address, anchor.GetAttribute("href"));
                if (!uri.AbsolutePath.StartsWith((string) $"{Root(address)}LessonAttendance/groups/", StringComparison.Ordinal))
                    throw new RegistryHistoryException("Course link route is unsupported.", phase: RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.CourseLink);
                var name = anchor.TextContent.Trim();
                if (name.Length == 0) throw new RegistryHistoryException("Course link has no name.", phase: RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.CourseName);
                if (!result.Any(x => x.Uri == uri && x.Semester == semester)) result.Add(new(name, semester, uri));
            }
        }
        return result;
    }

    public static IReadOnlyList<RegistryHistoryGroup> Groups(IDocument document, Uri address)
    {
        var container = document.QuerySelector("form[name='lesson'] > div.row:nth-of-type(2) > div.col:nth-of-type(1)");
        if (container is null)
        {
            if (IsEmptyHistoricalGroupForm(document)) return [];
            throw new RegistryHistoryException("Group container is missing; historical markup requires inspection.", phase: RegistryHistoryPhase.Groups, diagnostic: RegistryHistoryDiagnostic.GroupContainer);
        }
        var result = new List<RegistryHistoryGroup>();
        var rows = container.QuerySelectorAll(":scope > div.row");
        if (rows.Sum(x => x.QuerySelectorAll("a").Length) != container.QuerySelectorAll("a").Length)
            throw new RegistryHistoryException("Group container layout is unsupported; historical markup requires inspection.", phase: RegistryHistoryPhase.Groups, diagnostic: RegistryHistoryDiagnostic.GroupLayout);
        foreach (var row in rows)
        {
            var anchors = row.QuerySelectorAll("a");
            if (anchors.Length != 2) throw new RegistryHistoryException("Group row has an unsupported link layout.", phase: RegistryHistoryPhase.Groups, diagnostic: RegistryHistoryDiagnostic.GroupRowLinks);
            var uri = SafeUri(address, anchors[0].GetAttribute("href"));
            if (!uri.AbsolutePath.StartsWith((string) $"{Root(address)}LessonAttendance/LessonsList/", StringComparison.Ordinal))
                throw new RegistryHistoryException("Group link route is unsupported.", phase: RegistryHistoryPhase.Groups, diagnostic: RegistryHistoryDiagnostic.GroupLink);
            var name = anchors[0].TextContent.Trim();
            if (name.Length == 0) throw new RegistryHistoryException("Group link has no name.", phase: RegistryHistoryPhase.Groups, diagnostic: RegistryHistoryDiagnostic.GroupName);
            if (!result.Any(x => x.Uri == uri)) result.Add(new(name, uri));
        }
        return result;
    }

    private static bool IsEmptyHistoricalGroupForm(IDocument document)
    {
        var forms = document.QuerySelectorAll("form[name='lesson']");
        if (forms.Length != 1 || forms[0].Children.Length != 1) return false;
        var input = forms[0].Children[0];
        // Observed historical courses without assigned groups retain only the
        // MVC verification field. Its value is never read or retained.
        return input.LocalName == "input" && input.GetAttribute("type") == "hidden"
            && input.GetAttribute("name") == "__RequestVerificationToken"
            && !document.QuerySelectorAll("a[href]").Any(anchor =>
                anchor.GetAttribute("href")?.Contains("/LessonAttendance/LessonsList/", StringComparison.OrdinalIgnoreCase) == true);
    }

    internal static string Root(Uri address)
    {
        var index = address.AbsolutePath.IndexOf("/LessonAttendance", StringComparison.Ordinal);
        if (index < 0) throw new RegistryHistoryException("Registry page address is unsupported.", diagnostic: RegistryHistoryDiagnostic.RequestPolicy);
        return address.AbsolutePath[..(index + 1)];
    }

    private static Uri SafeUri(Uri address, string? href)
    {
        if (string.IsNullOrWhiteSpace(href) || !Uri.TryCreate(baseUri: address, href, result: out var uri)
            || uri.Scheme != address.Scheme || uri.Authority != address.Authority || uri.UserInfo.Length != 0
            || !uri.AbsolutePath.StartsWith(Root(address), StringComparison.Ordinal) || uri.Fragment.Length != 0)
            throw new RegistryHistoryException("Registry navigation link is missing or outside the configured registry.", diagnostic: RegistryHistoryDiagnostic.RequestPolicy);
        return uri;
    }
}

/// <summary>One global request gate covers authentication and reads across history accounts in this process.</summary>
public sealed class RegistryHistoryRequestHandler : DelegatingHandler
{
    private static readonly SemaphoreSlim Gate = new(initialCount: 1, maxCount: 1);
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
        if (spacing < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(spacing));
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
            var phase = login ? RegistryHistoryPhase.Login : uri is not null && IsLessonList(uri)
                ? RegistryHistoryPhase.Lessons : uri is not null
                && uri.AbsolutePath.StartsWith((string) $"{RegistryHistoryHtml.Root(_index)}LessonAttendance/groups/", StringComparison.Ordinal)
                    ? RegistryHistoryPhase.Groups
                    : uri == _index || uri is not null && uri.AbsolutePath == _index.AbsolutePath
                        ? RegistryHistoryPhase.Index
                        : RegistryHistoryPhase.Unknown;
            if (_stopped || uri is null || uri.Scheme != _index.Scheme || uri.Authority != _index.Authority || uri.UserInfo.Length != 0
                || !login && (request.Method != HttpMethod.Get || !AllowedRead(uri)))
                throw new RegistryHistoryException("History session stopped or disallowed a request.", RegistryHistoryFailure.TransportStopped,
                    phase, diagnostic: RegistryHistoryDiagnostic.RequestPolicy);
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
                    throw new RegistryHistoryException("History transport failed; session stopped without replay.", RegistryHistoryFailure.TransportStopped,
                        phase, diagnostic: RegistryHistoryDiagnostic.Transport);
                }
                finally { _lastCompletion = DateTimeOffset.UtcNow; }
                if (login && response.StatusCode == HttpStatusCode.Redirect) return response;
                if (!login && response.IsSuccessStatusCode) return response;
                var retry = !login && attempt == 0 && response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
                var statusCode = (int)response.StatusCode;
                response.Dispose();
                if (retry) continue;
                _stopped = true;
                throw new RegistryHistoryException("Registry rejected history access; session stopped without authentication replay or redirect navigation.",
                    RegistryHistoryFailure.AccessRejected, phase, statusCode, RegistryHistoryDiagnostic.HttpRejected);
            }
        }
        finally { Gate.Release(); }
    }

    private bool AllowedRead(Uri uri)
    {
        return IsIndex(uri)
            || IsNumericRoute(uri, "groups", allowGroupQuery: false)
            || IsLessonList(uri);
    }

    private bool IsIndex(Uri uri)
    {
        if (uri.AbsolutePath != _index.AbsolutePath || uri.Fragment.Length != 0) return false;
        if (uri.Query.Length == 0) return true;
        const string yearQuery = "?AnUniv=";
        if (!uri.Query.StartsWith(yearQuery, StringComparison.Ordinal)) return false;
        var value = Uri.UnescapeDataString(uri.Query[yearQuery.Length..]);
        if (value.Length != 7 || value[4] != '/' || value.AsSpan(start: 0, length: 4).IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9') >= 0
            || value.AsSpan(start: 5, length: 2).IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9') >= 0) return false;
        var start = int.Parse(value.AsSpan(start: 0, length: 4), System.Globalization.CultureInfo.InvariantCulture);
        var end = int.Parse(value.AsSpan(start: 5, length: 2), System.Globalization.CultureInfo.InvariantCulture);
        return (start + 1) % 100 == end && uri.Query == (string) $"{yearQuery}{Uri.EscapeDataString(value)}";
    }

    private bool IsLessonList(Uri uri) => IsNumericRoute(uri, "LessonsList", allowGroupQuery: true);

    private bool IsNumericRoute(Uri uri, string route, bool allowGroupQuery)
    {
        var prefix = (string) $"{RegistryHistoryHtml.Root(_index)}LessonAttendance/{route}/";
        if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal) || uri.Fragment.Length != 0) return false;
        var id = uri.AbsolutePath.AsSpan(prefix.Length);
        if (id.Length == 0 || id.IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9') >= 0) return false;
        if (uri.Query.Length == 0) return true;
        if (!allowGroupQuery) return false;
        const string groupQuery = "?groupId=";
        if (!uri.Query.StartsWith(groupQuery, StringComparison.Ordinal)) return false;
        var groupId = uri.Query.AsSpan(groupQuery.Length);
        return groupId.Length > 0 && groupId.IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9') < 0;
    }
}

public sealed class RegistryHistoryReader(HttpClient client, Uri index)
{
    public async Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(string? year, CancellationToken token)
    {
        using var initial = await Read(index, token);
        var years = RegistryHistoryHtml.Years(initial, index);
        if (year == "current") year = years.Single(x => x.Selected).Value;
        if (year is not null && !years.Any(x => x.Value == year))
            throw new RegistryHistoryException("Requested academic year is not offered by the registry selector.", RegistryHistoryFailure.RequestedYearUnavailable);
        var summaries = new List<RegistryHistoryYearSummary>();
        var initialYearActive = true;
        foreach (var selected in years.Where(x => year is null || x.Value == year))
        {
            // Historical selection changes server session state, even though the initial document remains unchanged.
            using var fetched = selected.Selected && initialYearActive ? null : await Read(selected.Uri, token);
            if (fetched is not null) initialYearActive = selected.Selected;
            var document = fetched ?? initial;
            // Detect ignored year parameters rather than labelling current data as historical.
            if (!RegistryHistoryHtml.Years(document, index).Any(x => x.Selected && x.Value == selected.Value))
                throw new RegistryHistoryException("Registry did not confirm the requested academic year; discovery stopped.", RegistryHistoryFailure.YearNotConfirmed,
                    RegistryHistoryPhase.Index, diagnostic: RegistryHistoryDiagnostic.YearSelectionNotConfirmed);
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

    internal async Task<IDocument> Read(Uri uri, CancellationToken token)
    {
        using var response = await client.GetAsync(uri, token);
        if (!response.IsSuccessStatusCode) throw new RegistryHistoryException("History page request failed.", RegistryHistoryFailure.AccessRejected,
            Phase(uri), (int)response.StatusCode, RegistryHistoryDiagnostic.HttpRejected);
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var document = await new HtmlParser().ParseDocumentAsync(stream, token);
        if (document.QuerySelector("input[type='password']") is not null
            || !document.QuerySelectorAll("a[href]").Any(x => x.GetAttribute("href")?.EndsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase) == true))
        {
            document.Dispose();
            throw new RegistryHistoryException("History page did not confirm authentication; discovery stopped.", RegistryHistoryFailure.AuthenticationNotConfirmed,
                Phase(uri), diagnostic: RegistryHistoryDiagnostic.AuthenticationPage);
        }
        return document;
    }

    private RegistryHistoryPhase Phase(Uri uri)
    {
        return uri.AbsolutePath == index.AbsolutePath ? RegistryHistoryPhase.Index
            : uri.AbsolutePath.StartsWith((string) $"{RegistryHistoryHtml.Root(index)}LessonAttendance/groups/", StringComparison.Ordinal)
                ? RegistryHistoryPhase.Groups : RegistryHistoryPhase.Lessons;
    }
}
