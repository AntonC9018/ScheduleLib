using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace ScheduleLib.OnlineRegistry;

public enum RegistryHistoryFailure
{
    MarkupContract, AccessRejected, AuthenticationNotConfirmed, RequestedYearUnavailable, YearNotConfirmed,
    TransportStopped,
}

public enum RegistryHistoryPhase { Unknown, Login, Index, Groups, Lessons }

public enum RegistryHistoryDiagnostic
{
    None, YearSelector, YearSelection, CourseContainers, CourseLayout, CourseLink, CourseName,
    GroupContainer, GroupLayout, GroupRowLinks, GroupLink, GroupName, TopicTable,
    YearSelectionNotConfirmed, AuthenticationPage, HttpRejected, Transport, RequestPolicy,
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

        static RegistryHistoryException InvalidYearSelector()
        {
            return new(
                "Academic-year GET selector is missing or unsupported; navigation requires inspection.",
                phase: RegistryHistoryPhase.Index,
                diagnostic: RegistryHistoryDiagnostic.YearSelector);
        }

        if (select is null)
        {
            throw InvalidYearSelector();
        }
        var form = select.Form;
        if (form is null)
        {
            throw InvalidYearSelector();
        }
        var method = form.GetAttribute("method");
        if (method is not null)
        {
            if (!method.Equals("get", StringComparison.OrdinalIgnoreCase))
            {
                throw InvalidYearSelector();
            }
        }
        var hasOtherInputs = form.QuerySelectorAll("input, select, textarea, button")
            .Any(input =>
            {
                if (input == select)
                {
                    return false;
                }

                if (input.HasAttribute("name"))
                {
                    return true;
                }

                return false;
            });
        if (hasOtherInputs)
        {
            throw InvalidYearSelector();
        }

        var action = SafeUri(address, form.GetAttribute("action") ?? address.AbsoluteUri);

        bool CheckCondition()
        {
            if (!action.AbsolutePath.Equals(address.AbsolutePath, StringComparison.Ordinal))
            {
                return true;
            }

            if (action.Query.Length != 0)
            {
                return true;
            }

            if (action.Fragment.Length != 0)
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            throw new RegistryHistoryException(
                "Academic-year form action is unsupported; navigation requires inspection.",
                phase: RegistryHistoryPhase.Index,
                diagnostic: RegistryHistoryDiagnostic.YearSelector);
        }

        var result = new List<RegistryHistoryYear>();
        foreach (var option in select.Options)
        {
            bool CalculateConditionResult()
            {
                if (option.IsDisabled)
                {
                    return true;
                }

                if (string.IsNullOrWhiteSpace(option.Value))
                {
                    return true;
                }

                return false;
            }

            bool conditionResult = CalculateConditionResult();


            if (conditionResult)
            {
                continue;
            }

            if (result.Any(x => x.Value == option.Value))
            {
                continue;
            }

            result.Add(new(
                option.Value,
                option.IsSelected,
                new($"{action.AbsoluteUri}?AnUniv={Uri.EscapeDataString(option.Value)}")));
        }
        if (result.Count == 0 || result.Count(x => x.Selected) != 1)
        {
            throw new RegistryHistoryException(
                "Academic-year selection is empty or ambiguous.",
                phase: RegistryHistoryPhase.Index,
                diagnostic: RegistryHistoryDiagnostic.YearSelection);
        }

        return result;
    }

    public static IReadOnlyList<RegistryHistoryCourse> Courses(IDocument document, Uri address)
    {
        if (document.GetElementById("nav-1") is null || document.GetElementById("nav-2") is null)
        {
            throw new RegistryHistoryException(
                "Semester course containers are missing; historical markup requires inspection.",
                phase: RegistryHistoryPhase.Index,
                diagnostic: RegistryHistoryDiagnostic.CourseContainers);
        }

        var result = new List<RegistryHistoryCourse>();
        for (var semester = 1; semester <= 2; semester++)
        {
            var iDocument = document;
            string selectors = $"#nav-{semester} > div > span:nth-of-type(2) > a";
            var expected = iDocument.QuerySelectorAll(selectors);
            var iDocument2 = document;
            string selectors2 = $"#nav-{semester} a[href]";
            var querySelectorAll =
                iDocument2.QuerySelectorAll(selectors2);
            if (querySelectorAll.Length != expected.Length)
            {
                throw new RegistryHistoryException(
                    "Course container layout is unsupported; historical markup requires inspection.",
                    phase: RegistryHistoryPhase.Index,
                    diagnostic: RegistryHistoryDiagnostic.CourseLayout);
            }

            foreach (var anchor in expected)
            {
                var address2 = address;
                string? href = anchor.GetAttribute("href");
                var uri = SafeUri(address2, href);
                if (!uri.AbsolutePath.StartsWith(
                        $"{Root(address)}LessonAttendance/groups/",
                        StringComparison.Ordinal))
                {
                    throw new RegistryHistoryException(
                        "Course link route is unsupported.",
                        phase: RegistryHistoryPhase.Index,
                        diagnostic: RegistryHistoryDiagnostic.CourseLink);
                }

                var name = anchor.TextContent.Trim();
                if (name.Length == 0)
                {
                    throw new RegistryHistoryException(
                        "Course link has no name.",
                        phase: RegistryHistoryPhase.Index,
                        diagnostic: RegistryHistoryDiagnostic.CourseName);
                }

                if (!result.Any(x => x.Uri == uri && x.Semester == semester))
                {
                    var
                        list = result;
                    RegistryHistoryCourse item = new(
                        name,
                        semester,
                        uri);
                    list.Add(item);
                }
            }
        }
        return result;
    }

    public static IReadOnlyList<RegistryHistoryGroup> Groups(IDocument document, Uri address)
    {
        var container = document.QuerySelector("form[name='lesson'] > div.row:nth-of-type(2) > div.col:nth-of-type(1)");
        if (container is null)
        {
            if (IsEmptyHistoricalGroupForm(document))
            {
                return [];
            }

            throw new RegistryHistoryException(
                "Group container is missing; historical markup requires inspection.",
                phase: RegistryHistoryPhase.Groups,
                diagnostic: RegistryHistoryDiagnostic.GroupContainer);
        }
        var result = new List<RegistryHistoryGroup>();
        var rows = container.QuerySelectorAll(":scope > div.row");
        if (rows.Sum(x => x.QuerySelectorAll("a").Length) != container.QuerySelectorAll("a").Length)
        {
            throw new RegistryHistoryException(
                "Group container layout is unsupported; historical markup requires inspection.",
                phase: RegistryHistoryPhase.Groups,
                diagnostic: RegistryHistoryDiagnostic.GroupLayout);
        }

        foreach (var row in rows)
        {
            var anchors = row.QuerySelectorAll("a");
            if (anchors.Length != 2)
            {
                throw new RegistryHistoryException(
                    "Group row has an unsupported link layout.",
                    phase: RegistryHistoryPhase.Groups,
                    diagnostic: RegistryHistoryDiagnostic.GroupRowLinks);
            }

            var address2 = address;
            string? href = anchors[0].GetAttribute("href");
            var uri = SafeUri(address2, href);
            if (!uri.AbsolutePath.StartsWith(
                    $"{Root(address)}LessonAttendance/LessonsList/",
                    StringComparison.Ordinal))
            {
                throw new RegistryHistoryException(
                    "Group link route is unsupported.",
                    phase: RegistryHistoryPhase.Groups,
                    diagnostic: RegistryHistoryDiagnostic.GroupLink);
            }

            var name = anchors[0].TextContent.Trim();
            if (name.Length == 0)
            {
                throw new RegistryHistoryException(
                    "Group link has no name.",
                    phase: RegistryHistoryPhase.Groups,
                    diagnostic: RegistryHistoryDiagnostic.GroupName);
            }

            if (!result.Any(x => x.Uri == uri))
            {
                var list =
                    result;
                RegistryHistoryGroup item = new(name, uri);
                list.Add(item);
            }
        }
        return result;
    }

    private static bool IsEmptyHistoricalGroupForm(IDocument document)
    {
        var forms = document.QuerySelectorAll("form[name='lesson']");

        bool CalculateConditionResult()
        {
            if (forms.Length != 1)
            {
                return true;
            }

            if (forms[0].Children.Length != 1)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            return false;
        }

        var input = forms[0].Children[0];
        // Observed historical courses without assigned groups retain only the
        // MVC verification field. Its value is never read or retained.
        bool conditionResult2;
        if (input.LocalName == "input")
        {
            if (input.GetAttribute("type") == "hidden")
            {
                if (input.GetAttribute("name") == "__RequestVerificationToken")
                {
                    conditionResult2 = document.QuerySelectorAll("a[href]")
                        .Any(anchor =>
                            anchor.GetAttribute("href")
                                ?.Contains("/LessonAttendance/LessonsList/", StringComparison.OrdinalIgnoreCase)
                            == true);
                    conditionResult2 = !conditionResult2;
                }
                else
                {
                    conditionResult2 = false;
                }
            }
            else
            {
                conditionResult2 = false;
            }
        }
        else
        {
            conditionResult2 = false;
        }

        return conditionResult2;
    }

    internal static string Root(Uri address)
    {
        var index = address.AbsolutePath.IndexOf("/LessonAttendance", StringComparison.Ordinal);
        if (index < 0)
        {
            throw new RegistryHistoryException(
                "Registry page address is unsupported.",
                diagnostic: RegistryHistoryDiagnostic.RequestPolicy);
        }

        return address.AbsolutePath[..(index + 1)];
    }

    private static Uri SafeUri(Uri address, string? href)
    {
        static RegistryHistoryException InvalidNavigation()
        {
            return new(
                "Registry navigation link is missing or outside the configured registry.",
                diagnostic: RegistryHistoryDiagnostic.RequestPolicy);
        }

        if (string.IsNullOrWhiteSpace(href))
        {
            throw InvalidNavigation();
        }
        if (!Uri.TryCreate(
                baseUri: address,
                href,
                result: out var uri))
        {
            throw InvalidNavigation();
        }
        if (uri.Scheme != address.Scheme)
        {
            throw InvalidNavigation();
        }
        if (uri.Authority != address.Authority)
        {
            throw InvalidNavigation();
        }
        if (uri.UserInfo.Length != 0)
        {
            throw InvalidNavigation();
        }
        var root = Root(address);
        if (!uri.AbsolutePath.StartsWith(root, StringComparison.Ordinal))
        {
            throw InvalidNavigation();
        }
        if (uri.Fragment.Length != 0)
        {
            throw InvalidNavigation();
        }

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

    public RegistryHistoryRequestHandler(
        HttpMessageHandler inner,
        Uri index,
        Uri login,
        TimeSpan spacing) : base(inner)
    {
        if (spacing < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(spacing));
        }

        _index = index;
        _login = login;
        _spacing = spacing;
    }

    public void CompleteAuthentication() => _authenticated = true;

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        await Gate.WaitAsync(token);
        try
        {
            var uri = request.RequestUri;

            bool CalculateConditionResult2()
            {
                if (_authenticated)
                {
                    return false;
                }

                if (_loginAttempted)
                {
                    return false;
                }

                if (!(request.Method == HttpMethod.Post))
                {
                    return false;
                }

                if (uri == _login)
                {
                    return true;
                }

                return false;
            }

            bool conditionResult2 = CalculateConditionResult2();


            var login = conditionResult2;
            RegistryHistoryPhase registryHistoryPhase;
            if (login)
            {
                registryHistoryPhase = RegistryHistoryPhase.Login;
            }
            else
            {
                bool CalculateCondition()
                {
                    if (uri is null)
                    {
                        return false;
                    }

                    if (IsLessonList(uri))
                    {
                        return true;
                    }

                    return false;
                }

                bool condition = CalculateCondition();


                RegistryHistoryPhase registryHistoryPhase2;
                if (condition)
                {
                    registryHistoryPhase2 = RegistryHistoryPhase.Lessons;
                }
                else
                {
                    bool condition2;
                    if (uri is not null)
                    {
                        string text = uri.AbsolutePath;
                        string root = RegistryHistoryHtml.Root(_index);
                        string text2 = $"{root}LessonAttendance/groups/";
                        string value = text2;
                        var comparisonType = StringComparison.Ordinal;
                        condition2 = text.StartsWith(value, comparisonType);
                    }
                    else
                    {
                        condition2 = false;
                    }

                    RegistryHistoryPhase registryHistoryPhase3;
                    if (condition2)
                    {
                        registryHistoryPhase3 = RegistryHistoryPhase.Groups;
                    }
                    else
                    {
                        bool condition3;
                        if (uri == _index)
                        {
                            condition3 = true;
                        }
                        else
                        {
                            bool CalculateCondition4()
                            {
                                if (uri is null)
                                {
                                    return false;
                                }

                                if (uri.AbsolutePath == _index.AbsolutePath)
                                {
                                    return true;
                                }

                                return false;
                            }

                            bool condition4 = CalculateCondition4();


                            condition3 = condition4;
                        }

                        RegistryHistoryPhase registryHistoryPhase4;
                        if (condition3)
                        {
                            registryHistoryPhase4 = RegistryHistoryPhase.Index;
                        }
                        else
                        {
                            registryHistoryPhase4 = RegistryHistoryPhase.Unknown;
                        }

                        registryHistoryPhase3 = registryHistoryPhase4;
                    }

                    registryHistoryPhase2 = registryHistoryPhase3;
                }

                registryHistoryPhase = registryHistoryPhase2;
            }

            var phase = registryHistoryPhase;
            bool conditionResult;
            if (_stopped)
            {
                conditionResult = true;
            }
            else
            {
                if (uri is null)
                {
                    conditionResult = true;
                }
                else
                {
                    if (uri.Scheme != _index.Scheme)
                    {
                        conditionResult = true;
                    }
                    else
                    {
                        if (uri.Authority != _index.Authority)
                        {
                            conditionResult = true;
                        }
                        else
                        {
                            if (uri.UserInfo.Length != 0)
                            {
                                conditionResult = true;
                            }
                            else
                            {
                                if (!login)
                                {
                                    if (request.Method != HttpMethod.Get)
                                    {
                                        conditionResult = true;
                                    }
                                    else
                                    {
                                        conditionResult = AllowedRead(uri);
                                        conditionResult = !conditionResult;
                                    }
                                }
                                else
                                {
                                    conditionResult = false;
                                }
                            }
                        }
                    }
                }
            }

            if (conditionResult)
            {
                throw new RegistryHistoryException(
                    "History session stopped or disallowed a request.",
                    RegistryHistoryFailure.TransportStopped,
                    phase,
                    diagnostic: RegistryHistoryDiagnostic.RequestPolicy);
            }

            if (login)
            {
                _loginAttempted = true;
            }

            for (var attempt = 0; ; attempt++)
            {
                var delay = _sent ? _lastCompletion + _spacing - DateTimeOffset.UtcNow : _spacing;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, token);
                }

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
                    throw new RegistryHistoryException(
                        "History transport failed; session stopped without replay.",
                        RegistryHistoryFailure.TransportStopped,
                        phase,
                        diagnostic: RegistryHistoryDiagnostic.Transport);
                }
                finally { _lastCompletion = DateTimeOffset.UtcNow; }
                if (login)
                {
                    if (response.StatusCode == HttpStatusCode.Redirect)
                    {
                        return response;
                    }
                }
                if (!login)
                {
                    if (response.IsSuccessStatusCode)
                    {
                        return response;
                    }
                }

                bool CalculateConditionResult3()
                {
                    if (login)
                    {
                        return false;
                    }

                    if (attempt != 0)
                    {
                        return false;
                    }

                    if (response.StatusCode is HttpStatusCode.BadGateway
                        or HttpStatusCode.ServiceUnavailable
                        or HttpStatusCode.GatewayTimeout)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult3 = CalculateConditionResult3();


                var retry = conditionResult3;
                var statusCode = (int) response.StatusCode;
                response.Dispose();
                if (retry)
                {
                    continue;
                }

                _stopped = true;
                throw new RegistryHistoryException(
                    "Registry rejected history access; session stopped without authentication replay or redirect navigation.",
                    RegistryHistoryFailure.AccessRejected,
                    phase,
                    statusCode,
                    RegistryHistoryDiagnostic.HttpRejected);
            }
        }
        finally { Gate.Release(); }
    }

    private bool AllowedRead(Uri uri)
    {
        if (IsIndex(uri))
        {
            return true;
        }

        if (IsNumericRoute(
                uri,
                "groups",
                allowGroupQuery: false))
        {
            return true;
        }

        if (IsLessonList(uri))
        {
            return true;
        }

        return false;
    }

    private bool IsIndex(Uri uri)
    {
        if (uri.AbsolutePath != _index.AbsolutePath || uri.Fragment.Length != 0)
        {
            return false;
        }

        if (uri.Query.Length == 0)
        {
            return true;
        }

        const string yearQuery = "?AnUniv=";
        if (!uri.Query.StartsWith(yearQuery, StringComparison.Ordinal))
        {
            return false;
        }

        var value = Uri.UnescapeDataString(uri.Query[yearQuery.Length..]);

        bool CalculateConditionResult()
        {
            if (value.Length != 7)
            {
                return true;
            }
            if (value[4] != '/')
            {
                return true;
            }
            if (value.AsSpan(start: 0, length: 4)
                    .IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9')
                >= 0)
            {
                return true;
            }

            if (value.AsSpan(start: 5, length: 2)
                    .IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9')
                >= 0)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            return false;
        }

        var start = int.Parse(value.AsSpan(start: 0, length: 4), System.Globalization.CultureInfo.InvariantCulture);
        var end = int.Parse(value.AsSpan(start: 5, length: 2), System.Globalization.CultureInfo.InvariantCulture);
        if ((start + 1) % 100 == end && uri.Query == $"{yearQuery}{Uri.EscapeDataString(value)}")
        {
            return true;
        }

        return false;
    }

    private bool IsLessonList(Uri uri)
    {
        return IsNumericRoute(
            uri,
            "LessonsList",
            allowGroupQuery: true);
    }

    private bool IsNumericRoute(
        Uri uri,
        string route,
        bool allowGroupQuery)
    {
        var prefix = $"{RegistryHistoryHtml.Root(_index)}LessonAttendance/{route}/";

        bool CalculateConditionResult()
        {
            if (!uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal))
            {
                return true;
            }

            if (uri.Fragment.Length != 0)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        if (conditionResult)
        {
            return false;
        }

        var id = uri.AbsolutePath.AsSpan(prefix.Length);
        if (id.Length == 0 || id.IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9') >= 0)
        {
            return false;
        }

        if (uri.Query.Length == 0)
        {
            return true;
        }

        if (!allowGroupQuery)
        {
            return false;
        }

        const string groupQuery = "?groupId=";
        if (!uri.Query.StartsWith(groupQuery, StringComparison.Ordinal))
        {
            return false;
        }

        var groupId = uri.Query.AsSpan(groupQuery.Length);
        if (groupId.Length > 0 && groupId.IndexOfAnyExceptInRange(lowInclusive: '0', highInclusive: '9') < 0)
        {
            return true;
        }

        return false;
    }
}

public sealed class RegistryHistoryReader(HttpClient client, Uri index)
{
    public async Task<IReadOnlyList<RegistryHistoryYearSummary>> Discover(string? year, CancellationToken token)
    {
        using var initial = await Read(index, token);
        var years = RegistryHistoryHtml.Years(initial, index);
        if (year == "current")
        {
            year = years.Single(x => x.Selected).Value;
        }

        if (year is not null && !years.Any(x => x.Value == year))
        {
            throw new RegistryHistoryException(
                "Requested academic year is not offered by the registry selector.",
                RegistryHistoryFailure.RequestedYearUnavailable);
        }

        var summaries = new List<RegistryHistoryYearSummary>();
        var initialYearActive = true;
        foreach (var selected in years.Where(x => year is null || x.Value == year))
        {
            // Historical selection changes server session state, even though the initial document remains unchanged.
            bool CalculateConditionResult()
            {
                if (!selected.Selected)
                {
                    return false;
                }

                return initialYearActive;
            }

            bool conditionResult = CalculateConditionResult();


            using var fetched = conditionResult ? null : await Read(selected.Uri, token);
            if (fetched is not null)
            {
                initialYearActive = selected.Selected;
            }

            var document = fetched ?? initial;
            // Detect ignored year parameters rather than labelling current data as historical.
            if (!RegistryHistoryHtml.Years(document, index)
                    .Any(x => x.Selected && x.Value == selected.Value))
            {
                throw new RegistryHistoryException(
                    "Registry did not confirm the requested academic year; discovery stopped.",
                    RegistryHistoryFailure.YearNotConfirmed,
                    RegistryHistoryPhase.Index,
                    diagnostic: RegistryHistoryDiagnostic.YearSelectionNotConfirmed);
            }

            var courses = new List<RegistryHistoryCourseSummary>();
            foreach (var course in RegistryHistoryHtml.Courses(document, index))
            {
                using var groups = await Read(course.Uri, token);
                var
                    list = courses;
                string name = course.Name;
                int semester = course.Semester;
                var groups2 =
                    RegistryHistoryHtml.Groups(groups, course.Uri);
                Func<RegistryHistoryGroup, string> selector = x =>
                    x.Name;
                var select = groups2.Select(selector);
                IReadOnlyList<string> groups3 = select.ToArray();
                RegistryHistoryCourseSummary item = new(
                    name,
                    semester,
                    groups3);
                list.Add(item);
            }
            var
                list2 = summaries;
            RegistryHistoryYearSummary item2 = new(selected.Value, courses);
            list2.Add(item2);
        }
        return summaries;
    }

    internal async Task<IDocument> Read(Uri uri, CancellationToken token)
    {
        using var response = await client.GetAsync(uri, token);
        if (!response.IsSuccessStatusCode)
        {
            string message = "History page request failed.";
            var failure = RegistryHistoryFailure.AccessRejected;
            var phase = Phase(uri);
            int? statusCode = (int) response.StatusCode;
            var diagnostic2 =
                RegistryHistoryDiagnostic.HttpRejected;
            throw new RegistryHistoryException(
                message,
                failure,
                phase,
                statusCode,
                diagnostic2);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(token);
        var document = await new HtmlParser().ParseDocumentAsync(stream, token);
        if (document.QuerySelector("input[type='password']") is not null
            || !document.QuerySelectorAll("a[href]")
                .Any(x => x.GetAttribute("href")
                        ?.EndsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase)
                    == true))
        {
            document.Dispose();
            string message2 = "History page did not confirm authentication; discovery stopped.";
            var failure2 =
                RegistryHistoryFailure.AuthenticationNotConfirmed;
            var phase2 = Phase(uri);
            var diagnostic3 =
                RegistryHistoryDiagnostic.AuthenticationPage;
            throw new RegistryHistoryException(
                message2,
                failure2,
                phase2,
                diagnostic: diagnostic3);
        }
        return document;
    }

    private RegistryHistoryPhase Phase(Uri uri)
    {
        RegistryHistoryPhase registryHistoryPhase;
        if (uri.AbsolutePath == index.AbsolutePath)
        {
            registryHistoryPhase = RegistryHistoryPhase.Index;
        }
        else
        {
            string text = uri.AbsolutePath;
            string root = RegistryHistoryHtml.Root(index);
            string text2 = $"{root}LessonAttendance/groups/";
            string value = text2;
            var comparisonType = StringComparison.Ordinal;
            RegistryHistoryPhase registryHistoryPhase2;
            if (text.StartsWith(value, comparisonType))
            {
                registryHistoryPhase2 = RegistryHistoryPhase.Groups;
            }
            else
            {
                registryHistoryPhase2 = RegistryHistoryPhase.Lessons;
            }

            registryHistoryPhase = registryHistoryPhase2;
        }

        return registryHistoryPhase;
    }
}
