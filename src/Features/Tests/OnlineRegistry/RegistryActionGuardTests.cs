using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistryActionGuardTests
{
    [Fact]
    public async Task MapsCancellationAndPreservesSubmissionState()
    {
        var cancellation = new OperationCanceledException();

        var error = await Assert.ThrowsAsync<RegistryActionCancelledException>(() =>
        {
            var action = RegistryActionGuard.Run(onSubmissionStarted =>
            {
                onSubmissionStarted();
                throw cancellation;
            });
            return action;
        });

        Assert.True(error.SubmissionStarted);
        Assert.Same(expected: cancellation, actual: error.InnerException);
    }

    [Fact]
    public async Task MapsOtherFailuresBeforeSubmission()
    {
        var failure = new InvalidOperationException("failed before submission");

        var error = await Assert.ThrowsAsync<RegistryActionExecutionException>(() =>
        {
            var action = RegistryActionGuard.Run(_ => Task.FromException(failure));
            return action;
        });

        Assert.False(error.SubmissionStarted);
        Assert.Same(expected: failure, actual: error.InnerException);
    }
}
