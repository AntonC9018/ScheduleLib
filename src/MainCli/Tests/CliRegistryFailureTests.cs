using ScheduleLib.Cli;

public sealed class CliRegistryFailureTests
{
    [Fact]
    public void UnclassifiedFailureIncludesExceptionChain()
    {
        var error = new InvalidOperationException(
            "outer",
            new FormatException("inner"));

        var failure = CliRegistryFailure.Classify(
            error,
            attempted: false,
            grades: false);

        var message = Assert.Single(failure.Errors);
        Assert.StartsWith(
            expectedStartString: "Registry operation failed. No automatic retry was performed. Cause: ",
            actualString: message);
        Assert.Contains(
            expectedSubstring: "System.InvalidOperationException: outer ---> System.FormatException: inner",
            actualString: message);
    }
}
