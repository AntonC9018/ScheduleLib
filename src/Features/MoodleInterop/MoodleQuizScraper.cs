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
        var escapedQuizId = Uri.EscapeDataString(quizId);
        var relativeUrl = $"mod/quiz/report.php?id={escapedQuizId}&mode=overview";
        var url = new Uri(MoodleScrapingContext.Names.BaseUrl, relativeUrl);
        var page = await context.Browser.OpenAsync(url.AbsoluteUri, cancellationToken);

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
        // AngleSharp's SubmitAsync has no cancellation parameter; WaitAsync bounds the await
        // and observes any late transport fault if the caller cancels and disposes the browser.
        var submission = form.SubmitAsync();
        try
        {
            page = await submission.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _ = submission.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            throw;
        }

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
