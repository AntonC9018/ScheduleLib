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

    [Fact]
    public void SubclassAndGradeRulesPreserveSpecificPrecedenceAndPartialExit()
    {
        var fileMissing = CliRegistryFailure.Classify(
            new FileNotFoundException("missing schedule"),
            attempted: true,
            grades: true);
        Assert.Equal(expected: CliExit.InvalidConfiguration, actual: fileMissing.ExitCode);
        Assert.Equal(expected: ["missing schedule"], actual: fileMissing.Errors);

        var argumentSubclass = CliRegistryFailure.Classify(
            new ArgumentNullException("param"),
            attempted: true,
            grades: false);
        Assert.Equal(expected: CliExit.InvalidArguments, actual: argumentSubclass.ExitCode);

        var gradeIo = CliRegistryFailure.Classify(
            new IOException("disk"),
            attempted: false,
            grades: true);
        Assert.Equal(expected: CliExit.IoFailure, actual: gradeIo.ExitCode);
        var gradeIoMessage = Assert.Single(gradeIo.Errors);
        Assert.StartsWith(
            expectedStartString: "Registry input/output operation failed.",
            actualString: gradeIoMessage);

        var partialGradeIo = CliRegistryFailure.Classify(
            new IOException("disk"),
            attempted: true,
            grades: true);
        Assert.Equal(expected: CliExit.Partial, actual: partialGradeIo.ExitCode);

        var syncFallback = CliRegistryFailure.Classify(
            new Exception("boom"),
            attempted: false,
            grades: false);
        Assert.Equal(expected: CliExit.IoFailure, actual: syncFallback.ExitCode);

        var gradeFallback = CliRegistryFailure.Classify(
            new Exception("boom"),
            attempted: false,
            grades: true);
        Assert.Equal(expected: CliExit.Unexpected, actual: gradeFallback.ExitCode);
    }
}
