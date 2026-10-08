using Microsoft.Extensions.DependencyInjection;
using QuizModels;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Cli;

/// <summary>Uses the already authenticated registry session; planning never submits grade forms.</summary>
public interface IRegistryGradePlanner
{
    Task<RegistryGradePlan> Plan(IRegistrySession session, string quizId, CancellationToken token);
}

public sealed class ConfiguredRegistryGradePlanner : IRegistryGradePlanner
{
    private readonly Func<IServiceProvider, ScheduleLib.Scraping.Common.Credentials, CancellationToken, Task<MoodleScrapingContext>> _openMoodle;

    public ConfiguredRegistryGradePlanner() : this((services, credentials, token) => MoodleScrapingContext.Create(services, credentials, token)) { }

    public ConfiguredRegistryGradePlanner(Func<IServiceProvider, ScheduleLib.Scraping.Common.Credentials, CancellationToken, Task<MoodleScrapingContext>> openMoodle) => _openMoodle = openMoodle;

    public async Task<RegistryGradePlan> Plan(IRegistrySession session, string quizId, CancellationToken token)
    {
        if (session is not IConfiguredRegistrySession configured)
            throw new PlatformNotSupportedException("Grade import requires a configured registry session.");
        var services = configured.Services;
        ScheduleLib.Scraping.Common.Credentials credentials;
        try
        {
            credentials = services.GetRequiredService<CredentialsResolver<MoodleConfig>>().Get();
            if (credentials is null || string.IsNullOrWhiteSpace(credentials.Login) || string.IsNullOrWhiteSpace(credentials.Password))
                throw new InvalidOperationException();
        }
        catch (InvalidOperationException)
        {
            throw new RegistryAuthenticationException("Moodle credentials are missing; configure the selected teacher's existing credential source.");
        }
        MoodleScrapingContext context;
        try { context = await _openMoodle(services, credentials, token); }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new RegistryAuthenticationException("Moodle authentication failed using configured credentials."); }
        using (context)
        {
            var quiz = await context.ScrapeQuizAttempts(quizId, cancellationToken: token);
            return await services.GetRequiredService<CopyGradesFromMoodleForTestTaskHandler>().Plan(new()
            {
                Quiz = quiz, RegistryNavigator = configured.CreateNavigator(token),
                Semester = services.GetCurrentSemester(), CancellationToken = token,
            });
        }
    }
}
