using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry;

public enum Semester
{
    Sem1,
    Sem2,
    Count = 2,
    Invalid = -1,
}

public static class SemesterHelper
{
    public static int AsOrdinal(this Semester semester)
    {
        return (int) semester + 1;
    }
}

public struct AddLessonsToOnlineRegistryParams()
{
    public required CancellationToken CancellationToken;
    public required Credentials Credentials;
    /// <summary>
    /// Will be initialized to the default config if not provided.
    /// </summary>
    public JsonSerializerOptions? JsonOptions;
    /// <summary>
    /// Will be initialized to the default values if not provided.
    /// </summary>
    public NamesConfig? Names = null;

    public required Semester Semester;
    public required Schedule Schedule;
    public required IRegistryErrorHandler ErrorHandler;
    public required CourseNameUnifierModule CourseNameUnifier;
    public required GroupParseContext GroupParseContext;
    public required LookupModule LookupModule;
    public required IAllScheduledDateProvider DateProvider;
    public required LessonTimeConfig TimeConfig;
    public required SemesterIntervalProvider SemesterIntervalProvider;
    public CommandProcessingConfig ProcessingFlags = CommandProcessingConfig.DryRun;
}

public static partial class RegistryScraping
{
    public static async Task AddLessonsToOnlineRegistry(AddLessonsToOnlineRegistryParams p)
    {
        p.Names ??= NamesConfig.Default;

        using var context = await CreateContext();
        var lists = new MatchingLists();

        var courseLinks = await QueryCourseLinks();
        foreach (var courseLink in courseLinks)
        {
            var groupsUrl = courseLink.Url;
            var groups = await QueryGroupLinksOfCourse(groupsUrl);
            foreach (var group in groups)
            {
                var (existingLessonInstances, addLessonUri) = await QueryExistingLessonInstancesOfGroup(group.Uri);
                var lessons = MissingLessonDetection.MatchLessonsInSchedule(new()
                {
                    Lookup = p.LookupModule.LessonsByCourse,
                    Schedule = p.Schedule,
                    CourseId = courseLink.CourseId,
                    GroupId = group.GroupId,
                    SubGroup = group.SubGroup,
                });

                // Figure out the exact dates the lessons will occur on.
                var times = MissingLessonDetection.GetDateTimesOfScheduledLessons(new()
                {
                    Lessons = lessons,
                    Schedule = p.Schedule,
                    DateProvider = p.DateProvider,
                    TimeConfig = p.TimeConfig,
                    SemesterIntervalProvider = p.SemesterIntervalProvider,
                    Semester = p.Semester,
                });

                var equationCommands = MissingLessonDetection.GetLessonEquationCommands(new()
                {
                    Lists = lists,
                    Schedule = p.Schedule,
                    AllLessons = times,
                    ExistingLessons = existingLessonInstances,
                });
                foreach (var command in equationCommands)
                {
                    if (p.ProcessingFlags.HasDryRun(command.Type))
                    {
                        DryRun(command, courseLink.CourseId);
                        continue;
                    }

                    if (p.ProcessingFlags.HasProcess(command.Type))
                    {
                        await HandleCommand(command, addLessonUri);
                        continue;
                    }
                }
            }
        }
        return;

        void DryRun(LessonEquationCommand command, CourseId courseId)
        {
            var commandName = command.Type switch
            {
                LessonEquationCommandType.Create => "Create",
                LessonEquationCommandType.Update => "Update",
                LessonEquationCommandType.Delete => "Delete",
                _ => throw Unreachable(),
            };
            var date = command.HasAll ? command.All.DateTime : command.Existing.DateTime;
            var dateString = date.ToString("dd.MM.yy");
            var course = p.Schedule.Get(courseId);
            var lessonName = course.FullName;
            Console.WriteLine($"{commandName}: {dateString} - {lessonName}");
        }

        async ValueTask HandleCommand(LessonEquationCommand command, Uri addLessonUri)
        {
            switch (command.Type)
            {
                case LessonEquationCommandType.Create:
                {
                    await Create(addLessonUri, command.All);
                    break;
                }
                case LessonEquationCommandType.Update:
                {
                    await Update(command.Existing.EditUri, command.All);
                    break;
                }
                case LessonEquationCommandType.Delete:
                {
                    await HandleExtraLesson(command.Existing);
                    break;
                }
                default:
                {
                    Debug.Fail("Unreachable");
                    break;
                }
            }
        }

        async ValueTask HandleExtraLesson(LessonInstanceLink x)
        {
            var action = p.ErrorHandler.ExtraLessonInstanceFound(x.DateTime);
            if (action == ExtraLessonInstanceAction.Delete)
            {
                await Delete(x.ViewUri);
            }
            if (action == ExtraLessonInstanceAction.DeleteWithoutDataLoss)
            {
                throw new NotImplementedException("This will need some more scanning");
            }
        }

        async Task Update(Uri editUri, LessonInstance lessonInstance)
        {
            await CreateOrUpdate1(editUri, lessonInstance);
        }

        async Task Create(Uri addLessonUri, LessonInstance lessonInstance)
        {
            await CreateOrUpdate1(addLessonUri, lessonInstance);
        }

        async Task CreateOrUpdate(
            Uri uri,
            LessonInstance lesson,
            Schedule schedule,
            HttpClient client)
        {
            var doc = await GetHtml(uri);
            _ = client;
            await SendUpdatedForm(new()
            {
                // HttpClient = client,
                // Target = uri,
                Document = doc,
                Lesson = lesson,
                Schedule = schedule,
            });
        }

        Task CreateOrUpdate1(Uri uri, LessonInstance lesson)
        {
            // ReSharper disable once AccessToDisposedClosure
            return CreateOrUpdate(uri, lesson, p.Schedule, context.HttpClient);
        }

        async Task Delete(Uri detailsUri)
        {
            var doc = await GetHtml(detailsUri);
            var form = doc.QuerySelector<IHtmlFormElement>("""form[name="deleteLessonForm"]""")!;
            await form.SubmitAsync();
        }

        static async Task SendUpdatedForm(SendUpdatedFormParams p)
        {
            var lessonDateBox = (IHtmlInputElement) p.Document.GetElementById("LessonDate")!;
            lessonDateBox.Value = p.Lesson.DateTime.ToString("yyyy-MM-ddTHH:mm");
            Debug.Assert(lessonDateBox.Value is not null and not "");

            var lessonTypeBox = (IHtmlSelectElement) p.Document.GetElementById("LessonMode")!;
            var lessonType = p.Schedule.Get(p.Lesson.LessonId).Lesson.Type;
            var lessonName = GetLessonTypeName(lessonType);
            foreach (var option in lessonTypeBox.Options)
            {
                if (lessonName is null)
                {
                    option.IsSelected = false;
                    continue;
                }
                if (option.Value.Equals(lessonName, StringComparison.Ordinal))
                {
                    option.IsSelected = true;
                    continue;
                }
                option.IsSelected = false;
            }
            var form = lessonDateBox.Form!;
            var ret = await form.SubmitAsync();
            var validationErrors = ret.QuerySelectorAll<IHtmlDivElement>(".validation-summary-errors")
                .SelectMany(x => x.Children)
                .SelectMany(x => x.Children)
                .Select(x => x.Text())
                .ToArray();
            if (validationErrors.Length != 0)
            {
                throw new InvalidOperationException($"Validation errors: {string.Concat("\n", validationErrors)}");
            }
        }

        async Task<(IEnumerable<LessonInstanceLink> Lessons, Uri AddLessonLink)> QueryExistingLessonInstancesOfGroup(Uri groupUri)
        {
            var doc = await GetHtml(groupUri);
            var lessons = HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
            {
                Document = doc,
                ErrorHandler = p.ErrorHandler,
            });
            var addLessonLink = HtmlSearch.ScanForLessonAddLink(doc);
            return (lessons, addLessonLink);
        }

        async Task<IEnumerable<GroupLink>> QueryGroupLinksOfCourse(Uri courseUrl)
        {
            var doc = await GetHtml(courseUrl);
            var ret = HtmlSearch.ScanGroupsDocumentForLinks(new()
            {
                Document = doc,
                GroupParseContext = p.GroupParseContext,
                Schedule = p.Schedule,
                ErrorHandler = p.ErrorHandler,
            });
            return ret;
        }

        async Task<IEnumerable<CourseLink>> QueryCourseLinks()
        {
            var doc = await GetHtml(p.Names.LessonsUrl);
            var ret = HtmlSearch.ScanCoursesDocumentForLinks(new()
            {
                Document = doc,
                Semester = p.Semester,
                ErrorHandler = p.ErrorHandler,
                LookupModule = p.LookupModule,
                CourseNameUnifier = p.CourseNameUnifier,
            });
            return ret;
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task<IDocument> GetHtml(Uri uri)
        {
            var document = await context.Browser.OpenAsync(address: uri.ToString(), p.CancellationToken);
            return document;
        }

        async Task<RegistryScrapingContext> CreateContext()
        {
            var http = HttpClientContext.Create();
            try
            {
                var tokenContext = new TokenRetrievalContext(new()
                {
                    Credentials = p.Credentials,
                    Names = p.Names,
                    CookieContainer = http.CookieProvider.Container,
                    HttpClient = http.Client,
                    JsonOptions = p.JsonOptions,
                });
                await tokenContext.InitializeToken(p.CancellationToken);

                var c = RegistryScrapingContext.Create(http, tokenContext);
                return c;
            }
            catch
            {
                http.Dispose();
                throw;
            }
        }
    }
}

public readonly struct CommandProcessingConfig
{
    private int Bits { get; init; }

    public readonly CommandProcessingConfig WithProcess(LessonEquationCommandTypes types)
    {
        var newBits = Bits | ((int) types << ProcessOffset);
        return new()
        {
            Bits = newBits,
        };
    }

    public readonly CommandProcessingConfig WithDryRun(LessonEquationCommandTypes types)
    {
        var newBits = Bits | ((int) types << DryRunOffset);
        return new()
        {
            Bits = newBits,
        };
    }

    private const int ProcessOffset = 0;
    private const int ProcessMask = (1 << (int) LessonEquationCommandType.Count) - 1;
    private const int DryRunOffset = (int) 16;
    private const int DryRunMask = ProcessMask << DryRunOffset;


    public static CommandProcessingConfig None => new();
    public static CommandProcessingConfig Process => None.WithProcess(LessonEquationCommandTypes.All);
    public static CommandProcessingConfig DryRun => None.WithDryRun(LessonEquationCommandTypes.All);

    /// <summary>
    /// Masks out the "process" that are also on "dry run".
    /// </summary>
    /// <value></value>
    public readonly CommandProcessingConfig Normalized
    {
        get
        {
            int dryRunBits = DryRunMask & Bits;
            int doNotProcessMask = dryRunBits >> DryRunOffset;
            int doProcessMask = ~doNotProcessMask;
            int bits = (doProcessMask & Bits) | ((~ProcessMask) & Bits);
            return new()
            {
                Bits = bits,
            };
        }
    }

    public readonly bool HasProcess(LessonEquationCommandType type)
    {
        var mask = 1 << ((int) type + ProcessOffset);
        return (Bits & mask) != 0;
    }

    public readonly bool HasAnyProcess(LessonEquationCommandTypes types)
    {
        var mask = (int) types << ProcessOffset;
        return (Bits & mask) != 0;
    }

    public readonly bool HasDryRun(LessonEquationCommandType type)
    {
        var mask = 1 << ((int) type + DryRunOffset);
        return (Bits & mask) != 0;
    }

    public readonly bool HasAnyDryRun(LessonEquationCommandTypes types)
    {
        var mask = (int) types << DryRunOffset;
        return (Bits & mask) != 0;
    }
}

file struct SendUpdatedFormParams
{
    public required IDocument Document { get; init; }
    public required LessonInstance Lesson { get; init; }
    // public required HttpClient HttpClient { get; init; }
    // public required Uri Target { get; init; }
    public required Schedule Schedule { get; init; }
}
