using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Xml;
using AngleSharp.Xml.Dom;
using ScheduleLib.Helper;
using ScheduleLib.Scraping.Common;

namespace QuizModels;

public sealed class MoodleScrapingContext : IDisposable
{
    private ScrapingContext _context;
    public HttpClient HttpClient => _context.HttpClient;
    public IBrowsingContext Browser => _context.Browser;

    public MoodleScrapingContext(ScrapingContext context)
    {
        _context = context;
    }

    private const string BaseUrl = "https://elearning.usm.md";
    internal static readonly TokenNamesConfig Names = new()
    {
        BaseUrl = new(BaseUrl),
        LoginUrl = new($"{BaseUrl}/login/index.php"),
        TokenCookieName = new("MoodleSessionusmmd"),
    };
    internal static readonly PasswordLoginFieldNames TokenRetrieverConfig = new()
    {
        UserName = new("username"),
        UserPassword = new("password"),
    };

    public static async Task<MoodleScrapingContext> Create(
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        var builder = new ScrapingContextBuilder();
        builder.Delay(TimeSpan.FromSeconds(0.4));
        builder.AddConfig(Names);
        builder.AddConfig(TokenRetrieverConfig);
        builder.TokenAuth(
            f => f.PasswordCredentials(credentials));
        var context = await builder.Build(cancellationToken);
        return new MoodleScrapingContext(context);
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}

public static class MoodleInterop
{
    #if false
    public static async Task Login(IBrowsingContext browser, Credentials credentials)
    {
        const string loginUrl = $"{BaseUrl}/login/index.php";
        var loginPage = await browser.OpenAsync(loginUrl);
        {
            var usernameInput = (IHtmlInputElement) loginPage.QuerySelector("#username")!;
            var username = credentials.Username;
            usernameInput.Value = username;
        }
        {
            var passwordInput = (IHtmlInputElement) loginPage.QuerySelector("#password")!;
            var password = credentials.Password;
            passwordInput.Value = password;
        }

        var form = ((IHtmlButtonElement) loginPage.QuerySelector("#loginbtn")!).Form!;
        var mainPage = await form.SubmitAsync();
        _ = mainPage;
    }
#endif

    public static async Task DownloadMoodleXml(
        this MoodleScrapingContext context,
        string quizId)
    {
        var url = $"{MoodleScrapingContext.Names.BaseUrl}/question/bank/exportquestions/export.php?cmid={quizId}";
        var questionList = await context.Browser.OpenAsync(url);
        var xmlFormatCheck = (IHtmlInputElement) questionList.QuerySelector("#id_format_xml")!;
        xmlFormatCheck.IsChecked = true;
        var downloadPage = await xmlFormatCheck.Form!.SubmitAsync();
        _ = downloadPage;

        var downloadAnchor = downloadPage.QuerySelector("div[role=main] > div.generalbox > a")!;
        var downloadLink = downloadAnchor.GetAttribute("href")!;
        // await using var stream = await httpClient.GetStreamAsync(downloadLink);

        // var requester = browser.GetService<ILoader>()!;
        // var downloads = requester.GetDownloads();
        //
        // var download = await downloads.First().Task;

        var xml = await context.Browser.OpenAsync(downloadLink);
        await SaveForDebug(xml);
        return;
    }

    public static async Task SaveForDebug(IDocument doc)
    {
        string FileName(string ext)
        {
            return $"temp.{ext}";
        }
        FileStream Open(string fileName)
        {
            return new FileStream(fileName, FileMode.Create, FileAccess.Write);
        }
        string Ext()
        {
            return doc switch
            {
                IXmlDocument => "xml",
                IHtmlDocument => "html",
                _ => throw new InvalidOperationException(),
            };
        }
        string ext = Ext();
        string fileName = FileName(ext);
        await using var outputStream = Open(fileName);

        switch (doc)
        {
            case IXmlDocument xml:
            {
                await using var textWriter = new StreamWriter(outputStream);
                xml.ToXml(textWriter);
                break;
            }
            case IHtmlDocument html:
            {
                await html.ToHtmlAsync(outputStream);
                break;
            }
        }
        ExplorerHelper.TryOpenExplorerAndSelectFile(fileName);
    }
}
