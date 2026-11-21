using System.Globalization;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace QuizModels;

public static class MoodleQuizScraper
{
    public static async Task<QuizAttemptsPage> ScrapeQuizAttempts(
        this MoodleScrapingContext context,
        string quizId,
        int pageSize = 2000,
        CancellationToken cancellationToken = default)
    {
        string url = $"{MoodleScrapingContext.Names.BaseUrl}/mod/quiz/report.php?id={quizId}&mode=overview";
        var page = await context.Browser.OpenAsync(url, cancellationToken);

        var path = MoodleQuizHtmlParser.ParseBreadcrumbs(page);

        if (page.QuerySelector("#id_pagesize") is not IHtmlInputElement pageSizeInput)
        {
            throw new MoodleQuizPageLayoutException("#id_pagesize");
        }
        pageSizeInput.Value = pageSize.ToString(CultureInfo.InvariantCulture);

        if (page.QuerySelector("#id_submitbutton") is not IHtmlInputElement submitButton
            || submitButton.Form is not { } form)
        {
            throw new MoodleQuizPageLayoutException("#id_submitbutton");
        }

        cancellationToken.ThrowIfCancellationRequested();
        // AngleSharp's SubmitAsync has no cancellation parameter; WaitAsync bounds the await.
        page = await form.SubmitAsync()
            .WaitAsync(cancellationToken);

        if (page.QuerySelector("table#attempts") is not IHtmlTableElement attemptsTable)
        {
            throw new MoodleAttemptsTableNotFoundException();
        }

        var attempts = MoodleQuizHtmlParser.ParseAttemptsTable(attemptsTable, cancellationToken);
        return new QuizAttemptsPage
        {
            Attempts = attempts,
            Path = path,
        };
    }
}
