using ScheduleLib.Cli;

public sealed class CliExceptionContextTests
{
    [Fact]
    public void ZipMismatchKeepsTheFrameworkTypeAndFailureTimeContext()
    {
        string[] missing = ["first.json"];
        string[] extra = ["unexpected.json"];
        var error = ExportCommands.WebsiteZipContentMismatch.CreateException(
            "site.zip",
            missing: missing,
            extra: extra);

        missing[0] = "changed.json";
        extra[0] = "changed.json";

        Assert.IsType<InvalidDataException>(error);
        var context = Assert.IsType<ExportCommands.WebsiteZipContentMismatch>(
            error.Data[typeof(ExportCommands.WebsiteZipContentMismatch)]);
        Assert.Equal(expected: "site.zip", actual: context.ZipPath);
        Assert.Equal(expected: ["first.json"], actual: context.Missing);
        Assert.Equal(expected: ["unexpected.json"], actual: context.Extra);
        Assert.Equal(
            expected: "ZIP content mismatch in site.zip: missing [first.json], extra [unexpected.json].",
            actual: error.Message);
    }
}
