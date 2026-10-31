using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistryActionGuardTests
{
    [Fact]
    public async Task MapsCancellationAndPreservesSubmissionState()
    {
        var cancellation = new OperationCanceledException();

        var error = await Assert.ThrowsAsync<RegistryActionCancelledException>(
            () => RegistryActionGuard.Run(onSubmissionStarted =>
            {
                onSubmissionStarted();
                throw cancellation;
            }));

        Assert.True(error.SubmissionStarted);
        Assert.Same(cancellation, error.InnerException);
    }

    [Fact]
    public async Task MapsOtherFailuresBeforeSubmission()
    {
        var failure = new InvalidOperationException("failed before submission");

        var error = await Assert.ThrowsAsync<RegistryActionExecutionException>(
            () => RegistryActionGuard.Run(_ => Task.FromException(failure)));

        Assert.False(error.SubmissionStarted);
        Assert.Same(failure, error.InnerException);
    }
}
