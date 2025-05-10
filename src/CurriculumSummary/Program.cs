using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Web;
using Azure.Core;
using Azure.Identity;
using Microsoft.Graph;
using ScheduleLib;
using ScheduleLib.Parsing;
using Process = System.Diagnostics.Process;

var cancellationToken = CancellationToken.None;
var config = Helper.MicrosoftAuthConfig();
// var credential = await Helper.CreateMaybeLoadCredential(new()
// {
// }, cancellationToken: cancellationToken);
var credential = await Helper.CreateMaybeLoadCredential(
    new InteractiveBrowserCredentialOptions
    {
        TenantId = config.TenantId,
        ClientId = config.ClientId,
        TokenCachePersistenceOptions = new()
        {
        },
    },
    () => new(scopes: [
        "User.Read",
        "Files.Read.All",
        "Files.Read",
        "User.ReadBasic.All",
    ]),
    cancellationToken);
await using var x_ = credential.AsDisposable();

using var httpProvider = CreateHttp();
HttpProvider CreateHttp()
{
    HttpClientHandler? defaultHandler = null;
    LoggingHandler? loggingHandler = null;
    try
    {
#pragma warning disable CA2000
        defaultHandler = new HttpClientHandler();
        loggingHandler = new LoggingHandler(defaultHandler);
        var handler = loggingHandler;
#pragma warning restore CA2000

        var ret = new HttpProvider(handler, disposeHandler: true, serializer: null);
        return ret;
    }
    catch
    {
        if (loggingHandler != null)
        {
            loggingHandler.Dispose();
        }
        else if (defaultHandler != null)
        {
            defaultHandler.Dispose();
        }
        throw;
    }
}

var graphClient = new GraphServiceClient(credential, httpProvider: httpProvider);
{
}
var rootFolderRef = await GetRootFolder();
var children = await graphClient
    .Drives[rootFolderRef.DriveId]
    .Items[rootFolderRef.Id]
    .Children
    .Request()
    .Select("name,id")
    .GetAsync(cancellationToken);
var childrenWithKeys = children
    .Select(child =>
    {
        var name = child.Name;
        var parser = new Parser(name);
        var qual = Qualification(ref parser);
        parser.SkipWhitespace();
        var code = Code(ref parser);
        parser.SkipWhitespace();

        ParserPosition lastSegmentPos = default;
        ParserPosition spacePos = default;
        bool hadSpaces = false;
        {
            var bparser = parser.BufferedView();
            while (true)
            {
                var r = bparser.SkipNotWhitespace();
                if (r.EndOfInput)
                {
                    break;
                }
                hadSpaces = true;
                spacePos = bparser.Position;
                bparser.SkipWhitespace();
                lastSegmentPos = bparser.Position;
            }
        }

        var attendanceModes = AttendanceModeFlags.None;

        // process last segment
        if (hadSpaces)
        {
            var lastSegmentParser = parser.BufferedView();
            lastSegmentParser.MoveTo(lastSegmentPos);
            var lastSegment = lastSegmentParser.SourceUntilEnd();
            attendanceModes = AttendanceModes(lastSegment.Span);
        }

        ReadOnlyMemory<char> nameSegment;
        {
            var nameParser = parser.BufferedView();
            if (attendanceModes == AttendanceModeFlags.None)
            {
                nameSegment = nameParser.SourceUntilEnd();
            }
            else
            {
                nameSegment = nameParser.SourceUntilExclusive(spacePos);
            }
        }

        return (
            Key: new CurriculumGroupKey
            {
                Code = code.ToString(),
                Name = nameSegment.ToString(),
                AttendanceModes = attendanceModes,
                QualificationType = qual,
            },
            Value: child);

        static AttendanceModeFlags AttendanceModes(ReadOnlySpan<char> lastSpan)
        {
            var ret = AttendanceModeFlags.None;
            int itemCount = 0;
            bool hasUnparsedItem = false;
            foreach (var x in lastSpan.Split('+'))
            {
                var segment = lastSpan[x];
                if (segment.Length == 0)
                {
                    continue;
                }

                itemCount++;

                var maybeMode = GetMode(segment);
                if (maybeMode is not { } mode)
                {
                    if (hasUnparsedItem)
                    {
                        break;
                    }
                    hasUnparsedItem = true;
                    continue;
                }


                var flag = (AttendanceModeFlags) (1 << (int) mode);
                if ((ret & flag) == flag)
                {
                    throw new NotSupportedException("The same attendance mode specified a second time");
                }
                ret |= flag;
                continue;

                static AttendanceMode? GetMode(ReadOnlySpan<char> s)
                {
                    if (s.SequenceEqual("zi"))
                    {
                        return AttendanceMode.Zi;
                    }
                    if (s.SequenceEqual("fr"))
                    {
                        return AttendanceMode.FrecventaRedusa;
                    }
                    return null;
                }
            }

            if (hasUnparsedItem && itemCount > 1)
            {
                throw new NotSupportedException("'+' in the last segment without it parsing");
            }

            return ret;
        }
    })
    .ToArray();

return;


static ReadOnlyMemory<char> Code(ref Parser parser)
{
    var bparser = parser.BufferedView();

    var skipResult = bparser.SkipNumbers();
    if (!skipResult.SkippedAny)
    {
        throw new NotSupportedException("Expected code");
    }
    if (skipResult.EndOfInput)
    {
        throw new NotSupportedException("Expected '.' after first part of code");
    }
    if (bparser.Current == '.')
    {
        bparser.Move();

        var secondPartSkipResult = bparser.SkipNumbers();
        if (!secondPartSkipResult.SkippedAny)
        {
            throw new NotSupportedException("Expected number after '.'");
        }
    }

    var ret = parser.SourceUntilExclusive(bparser);
    parser.MoveTo(bparser.Position);
    return ret;
}

static QualificationType Qualification(ref Parser parser)
{
    var bparser = parser.BufferedView();
    if (!bparser.SkipLetters().SkippedAny)
    {
        return QualificationType.Licenta;
    }

    var s = parser.PeekSpanUntilPosition(bparser.Position);
    parser.MoveTo(bparser.Position);

    if (IgnoreDiacriticsComparer.Instance.Equals(s, "master"))
    {
        return QualificationType.Master;
    }

    throw new NotSupportedException("Unknown qualification");
}

async Task<ItemRef> GetRootFolder()
{
    var result = await graphClient
        .Users["titu.capcelea@usm.md"]
        .Drive
        .Root
        .ItemWithPath("Curricula DI anul universitar 2024-2025/Curricula per program studii")
        .Request()
        .Select("folder,parentReference,id")
        .GetAsync(cancellationToken: cancellationToken);
    var folder = result.Folder;
    var itemId = result.Id;
    var driveId = result.ParentReference.DriveId;
    if (folder is null)
    {
        throw new InvalidOperationException("Folder null");
    }

    return new()
    {
        Id = itemId,
        DriveId = driveId,
    };
}

#pragma warning disable CS8321 // Local function is declared but never used
async Task<string> FindItemId()
#pragma warning restore CS8321 // Local function is declared but never used
{
    var sharedWithMeBuilder = graphClient.Me.Drive.SharedWithMe();
    var sharedItemsResponse = await sharedWithMeBuilder
        .Request()
        .Select("remoteItem")
        .GetAsync(cancellationToken: cancellationToken);
// sharedItems.Where(x => x.Name == "Curricula per program studii")
    var curriculaId = sharedItemsResponse
        .Select(x => x.RemoteItem)
        .Where(x =>
        {
            const string path = "/Curricula DI anul universitar 2024-2025/Curricula per program studii";
            _ = path;

            var url = HttpUtility.UrlDecode(x.WebUrl);

            if (!url.EndsWith(path))
            {
                return false;
            }
            // The filter odata thing seems bugged
            if (x.Shared.SharedBy.User.Id != "titu.capcelea@usm.md")
            {
                return false;
            }

            return true;
        })
        .Select(x =>
        {
            return x.Id;
        })
        .Single();
    return curriculaId;
}

public sealed class CurriculumGroupKey
{
    public required QualificationType QualificationType { get; init; }
    public required AttendanceModeFlags AttendanceModes { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
}

public sealed class ItemRef
{
    public required string Id { get; init; }
    public required string DriveId { get; init; }
}

public sealed class MicrosoftAuthConfig
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }
}

public static class Helper
{
    public static MicrosoftAuthConfig MicrosoftAuthConfig()
    {
        var config = new ConfigurationBuilder();
        config.AddUserSecrets<Program>();
        var c = config.Build();
        var microsoft = c.GetRequiredSection("Microsoft");
        var ret = microsoft.Get<MicrosoftAuthConfig>();
        if (ret is null)
        {
            throw new InvalidOperationException("Microsoft null");
        }
        if (ret.TenantId == null)
        {
            throw new InvalidOperationException("TenantId null");
        }
        if (ret.ClientId == null)
        {
            throw new InvalidOperationException("ClientId null");
        }
        if (ret.ClientSecret == null)
        {
            throw new InvalidOperationException("ClientSecret null");
        }
        return ret;
    }

    // https://stackoverflow.com/a/43232486/9731532
    public static void OpenInDefaultBrowser(string url)
    {
        try
        {
            Process.Start(url);
        }
        catch
        {
            // hack because of this: https://github.com/dotnet/corefx/issues/10361
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                url = url.Replace("&", "^&");
                Process.Start(new ProcessStartInfo(url)
                {
                    UseShellExecute = true,
                });
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                Process.Start("xdg-open", url);
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                Process.Start("open", url);
            }
            else
            {
                throw;
            }
        }
    }

    public static Task WhenCancelled(this CancellationToken token)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        token.Register(() => tcs.TrySetResult());
        return tcs.Task;
    }

    private const string _TokenPath = "tokencache.bin";

    public static async Task<InteractiveBrowserCredential> CreateMaybeLoadCredential(
        InteractiveBrowserCredentialOptions opts,
        Func<TokenRequestContext> contextFactory,
        CancellationToken cancellationToken)
    {
        Debug.Assert(opts.AuthenticationRecord == null);

        AuthenticationRecord? authRecord = null;
        try
        {
            await using var s = new FileStream(_TokenPath, FileMode.Open, FileAccess.Read);
            authRecord = await AuthenticationRecord.DeserializeAsync(s, cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        if (authRecord != null)
        {
            opts.AuthenticationRecord = authRecord;
        }

        var credential = new InteractiveBrowserCredential(opts);

        if (authRecord == null)
        {
            var context = contextFactory();
            authRecord = await credential.AuthenticateAsync(context, cancellationToken);
        }
        return credential;
    }

    private static readonly PropertyInfo RecordProperty = typeof(InteractiveBrowserCredential)
        .GetProperty("Record", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Property not found");

    public static async Task SaveCredential(InteractiveBrowserCredential credential)
    {
        await using var s = new FileStream(_TokenPath, FileMode.Create, FileAccess.Write);
        var token = (Azure.Identity.AuthenticationRecord?) RecordProperty.GetValue(credential);
        if (token != null)
        {
            await token.SerializeAsync(s, CancellationToken.None);
        }
    }

    public static CredentialsDisposable AsDisposable(this InteractiveBrowserCredential c)
    {
        return new(c);
    }
}

public readonly struct CredentialsDisposable : IAsyncDisposable
{
    public readonly InteractiveBrowserCredential Credential;

    public CredentialsDisposable(InteractiveBrowserCredential credential)
    {
        Credential = credential;
    }

    public async ValueTask DisposeAsync()
    {
        await Helper.SaveCredential(Credential);
    }
}

file class LoggingHandler : DelegatingHandler
{
    public LoggingHandler() : base()
    {
    }

    public LoggingHandler(HttpMessageHandler innerHandler)
        : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Console.WriteLine("Request:");
        Console.WriteLine(request.ToString());
        if (request.Content != null)
        {
            var r = await request.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine(r);
        }
        Console.WriteLine();

        var response = await base.SendAsync(request, cancellationToken);

        Console.WriteLine("Response:");
        Console.WriteLine(response.ToString());
        if (response.Content != null)
        {
            var r = await response.Content.ReadAsStringAsync(cancellationToken);
            Console.WriteLine(r);
        }
        Console.WriteLine();

        return response;
    }
}
