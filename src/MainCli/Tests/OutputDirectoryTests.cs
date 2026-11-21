using ScheduleLib.Application.Core.Helper;

namespace MainCli.Tests;

public sealed class OutputDirectoryTests
{
    [Fact]
    public void BuildPathEnforcesBoundaryAndAllowsRoot()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "schedulelib-boundary-check");
        var output = new OutputDirectory(baseDir);

        string inside = output.BuildPath("nested/file.txt");
        string expectedInside = Path.GetFullPath(Path.Combine(baseDir, "nested/file.txt"));
        Assert.Equal(expected: expectedInside, actual: inside);

        Assert.Throws<FullPathOutsideTempOutputDirectoryException>(() => output.BuildPath("../escape.txt"));
        Assert.Throws<FullPathOutsideTempOutputDirectoryException>(() => output.BuildPath($"{baseDir}-evil/file.txt"));

        var rootPath = Path.GetPathRoot(baseDir)!;
        var rootOutput = new OutputDirectory(rootPath);
        string underRoot = rootOutput.BuildPath("file.txt");
        string expectedUnderRoot = Path.GetFullPath(Path.Combine(rootPath, "file.txt"));
        Assert.Equal(expected: expectedUnderRoot, actual: underRoot);
    }
}
