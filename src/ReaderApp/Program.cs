using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Web;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.Configuration;
using ReaderApp;
using ReaderApp.Helper;
using ScheduleLib;
using ScheduleLib.Builders;

Console.WriteLine("Start");

var dayNameProvider = new DayNameProvider();
var context = DocParseContext.Create(new()
{
    DayNameProvider = dayNameProvider,
    CourseNameParserConfig = new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#", "Python"],
        IgnoredFullWords = ["p/u", "pentru"],
        IgnoredShortenedWords = ["Opț"],
        MinUsefulWordLength = 3,
    }),
});

context.Schedule.ConfigureRemappings(remap =>
{
    var teach = remap.TeacherLastNameRemappings;
    teach.Add("Curmanschi", "Curmanschii");
    teach.Add("Băț", "Beț");
    teach.Add("Spincean", "Sprîncean");
});

{
    // Register the teachers from the list.
    const string fileName = @"data\Cadre didactice DI 2024-2025.xlsx";
    using var excel = SpreadsheetDocument.Open(fileName, isEditable: false, new()
    {
        AutoSave = false,
        CompatibilityLevel = CompatibilityLevel.Version_2_20,
    });

    ExcelTeacherListParser.AddTeachersFromExcel(new()
    {
        Excel = excel,
        Schedule = context.Schedule,
    });
}
{
    context.Schedule.SetStudyYear(2024);

    const string dirName = @"data\2024_sem2";
    foreach (var filePath in Directory.EnumerateFiles(dirName, "*.docx", SearchOption.TopDirectoryOnly))
    {
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        WordScheduleParser.ParseToSchedule(new()
        {
            Context = context,
            Document = document,
        });
    }
}

var schedule = context.BuildSchedule();
Console.WriteLine("Schedule built");

var option = Option.CreateLessonsInRegistry;

var cancellationToken = CancellationToken.None;
_ = cancellationToken;

switch (option)
{
    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.AllTeachersExcel:
    {
        const string outputFile = "all_teachers_orar.xlsx";
        string outputFileFullPath = Path.GetFullPath(outputFile);

        var timeConfig = new DefaultLessonTimeConfig(context.TimeConfig);

        Tasks.GenerateAllTeacherExcel(new()
        {
            DayNameProvider = new DayNameProvider(),
            StringBuilder = new(),
            LessonTypeDisplay = new(),
            ParityDisplay = new(),
            TimeSlotDisplay = new(),
            SeminarDate = (DayOfWeek.Wednesday, timeConfig.T15_00),
            OutputFilePath = outputFileFullPath,
            Schedule = schedule,
            TimeConfig = context.TimeConfig,
        });

        ExplorerHelper.OpenFolderAndSelectFile(outputFileFullPath);
        break;
    }

    // ReSharper disable once UnreachableSwitchCaseDueToIntegerAnalysis
    case Option.PerGroupAndPerTeacherPdfs:
    {
        await Tasks.GeneratePdfForGroupsAndTeachers(new()
        {
            LessonTextDisplayServices = new()
            {
                ParityDisplay = new(),
                LessonTypeDisplay = new(),
                SubGroupNumberDisplay = new(),
            },
            Schedule = schedule,
            LessonTimeConfig = context.TimeConfig,
            TimeSlotDisplay = new(),
            DayNameProvider = dayNameProvider,
            OutputPath = "output",
        });
        break;
    }

    case Option.CreateLessonsInRegistry:
    {
        var credentials = GetCredentials();

        var cookieContainer = new CookieContainer();

        using var handler = new HttpClientHandler();
        handler.CookieContainer = cookieContainer;
        handler.UseCookies = true;
        handler.AllowAutoRedirect = false;

        using var httpClient = new HttpClient(handler);
        const string tokensFile = "tokens.json";
        const string tokenCookieName = "ForDecanat";

        // const string registryUrl = "http://crd.usm.md/studregistry";
        const string loginUrl = "http://crd.usm.md/studregistry/Account/Login";
        var loginUri = new Uri(loginUrl);

        await InitializeToken();
        break;

        async ValueTask<Cookie?> LoadToken()
        {
            if (!File.Exists(tokensFile))
            {
                return null;
            }

            await using var stream = File.OpenRead(tokensFile);
            using var cookies = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (cookies == null)
            {
                return null;
            }
            var root = cookies.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (!root.TryGetProperty(credentials.Login, out var token))
            {
                return null;
            }

            try
            {
                var cookie = token.Deserialize<Cookie>();
                return cookie;
            }
            catch (JsonException)
            {
                return null;
            }
        }

        async Task<bool> MaybeSetCookieFromFile()
        {
            var token = await LoadToken();
            if (token is null)
            {
                return false;
            }
            if (token.Expired)
            {
                return false;
            }
            cookieContainer.Add(loginUri, token);
            return true;
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task InitializeToken()
        {
            if (await MaybeSetCookieFromFile())
            {
                return;
            }

            var success = await LogIn(
                httpClient,
                credentials,
                cancellationToken);
            if (!success)
            {
                throw new InvalidOperationException("Login failed.");
            }

            var cookies = cookieContainer.GetCookies(new(loginUrl));
            if (cookies[tokenCookieName] is not { } token)
            {
                throw new InvalidOperationException("Token cookie not found.");
            }

            await using var stream = File.Open(tokensFile, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (await TryUpdateExisting())
            {
                return;
            }
            await CreateNew();
            return;

            async ValueTask<bool> TryUpdateExisting()
            {
                if (!Path.Exists(tokensFile))
                {
                    return false;
                }
                if (stream.Length == 0)
                {
                    return await CreateNew();
                }

                var document = await JsonNode.ParseAsync(
                    stream,
                    cancellationToken: cancellationToken);
                if (document is not JsonObject)
                {
                    return false;
                }

                document[credentials.Login] = JsonSerializer.SerializeToNode(token);

                await JsonSerializer.SerializeAsync(
                    stream,
                    document,
                    cancellationToken: cancellationToken);
                return true;
            }

            async Task<bool> CreateNew()
            {
                var root = new JsonObject();
                return await Save(root);
            }

            async Task<bool> Save(JsonObject root)
            {
                stream.Seek(0, SeekOrigin.Begin);
                root[credentials.Login] = JsonSerializer.SerializeToNode(token);
                await JsonSerializer.SerializeAsync(
                    stream,
                    root,
                    cancellationToken: cancellationToken);
                return true;
            }
        }

        static async Task<bool> LogIn(
            HttpClient client,
            Credentials credentials,
            CancellationToken cancellationToken)
        {
            Uri uri;
            {
                var b = new UriBuilder(loginUrl);
                var parameters = HttpUtility.ParseQueryString("");
                parameters.Add("UserLogin", credentials.Login);
                parameters.Add("UserPassword", credentials.Password);
                b.Query = parameters.ToString();
                uri = b.Uri;
            }

            var response = await client.PostAsync(
                uri,
                content: null,
                cancellationToken: cancellationToken);
            bool success = response.StatusCode == HttpStatusCode.Redirect;
            return success;
        }
    }
}

static Credentials GetCredentials()
{
    var b = new ConfigurationBuilder();
    b.AddUserSecrets<Program>();
    var config = b.Build();
    var ret = config.GetRequiredSection("Registry").Get<Credentials>();
    if (ret == null)
    {
        throw new InvalidOperationException("Credentials not found.");
    }
    if (ret.Login == null)
    {
        throw new InvalidOperationException("Login not found.");
    }
    if (ret.Password == null)
    {
        throw new InvalidOperationException("Password not found.");
    }
    return ret;
}

public enum Option
{
    AllTeachersExcel,
    PerGroupAndPerTeacherPdfs,
    CreateLessonsInRegistry,
}


public sealed class Credentials
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}
