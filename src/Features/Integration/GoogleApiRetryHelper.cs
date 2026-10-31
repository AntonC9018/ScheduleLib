using Google;

namespace ScheduleLib.Application.Core;

public static class GoogleApiRetryHelper
{
    private const int MaxRetries = 5;
    private const double MaxJitterSeconds = 1.0;

    // The SDK exposes no single interception point, so each call has to be wrapped.
    public static async Task ExecuteWithRetryAsync(Func<Task> action)
    {
        await ExecuteWithRetryAsync(async () =>
        {
            await action();
            return true;
        });
    }

    public static async Task<T> ExecuteWithRetryAsync<T>(Func<Task<T>> action)
    {
        var random = new Random();

        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (GoogleApiException ex) when (IsRetryable(ex) && attempt < MaxRetries)
            {
                // Exponential backoff with jitter.
                double jitterSeconds = random.NextDouble() * MaxJitterSeconds;
                double backoffSeconds = Math.Pow(x: 2, y: attempt);
                double delaySeconds = jitterSeconds + backoffSeconds;
                var delay = TimeSpan.FromSeconds(delaySeconds);
                await Task.Delay(delay);
            }
        }
    }

    private static bool IsRetryable(GoogleApiException ex)
    {
        if (ex.Error?.Errors == null)
        {
            return false;
        }

        foreach (var error in ex.Error.Errors)
        {
            if (error.Reason is "rateLimitExceeded" or "userRateLimitExceeded" or "backendError")
            {
                return true;
            }
        }

        return false;
    }
}
