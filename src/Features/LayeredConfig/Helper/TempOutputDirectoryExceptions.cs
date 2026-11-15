namespace ScheduleLib.Application.Core.Helper;

public sealed class FullPathOutsideTempOutputDirectoryException(string path, string expectedDirectory)
    : InvalidOperationException(
        $"Full path '{path}' not in the expected directory '{expectedDirectory}'!")
{
    public string Path { get; } = path;
    public string ExpectedDirectory { get; } = expectedDirectory;
}
