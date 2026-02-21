using AngleSharp;
using AngleSharp.Html.Dom;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib.Scraping.Common;

namespace QuizModels;

public sealed class MoodleScrapingContext : IDisposable
{
    private readonly ScrapingContext _context;
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
    internal static readonly PasswordLoginFormConfig TokenRetrieverConfig = new()
    {
        LoginName = new("username"),
        PasswordName = new("password"),
        RequireButtonClick = true,
    };

    public static async Task<MoodleScrapingContext> Create(
        IServiceProvider sp,
        Credentials credentials,
        CancellationToken cancellationToken)
    {
        var builder = new ScrapingContextBuilder();
        builder.Delay(TimeSpan.FromSeconds(0.4));
        builder.AddConfig(Names);
        builder.AddConfig(TokenRetrieverConfig);
        builder.TokenAuth(
            f => f.PasswordLoginForm(credentials));
        builder.AddLogging(sp.GetRequiredService<ILoggerFactory>());
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
    public const string CredentialsKey = "Moodle";

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
        await xml.SaveForDebug();
        return;
    }

}
