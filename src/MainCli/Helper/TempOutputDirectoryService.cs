using ScheduleLib.Helper;

namespace MainCli.Helper;

public sealed class TempOutputDirectoryService
{
    private readonly string _directory;

    public TempOutputDirectoryService(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public void Initialize(bool clear = false)
    {
        if (clear)
        {
            Directory.Delete(_directory, recursive: true);
        }
        else if (Directory.Exists(_directory))
        {
            return;
        }
        Directory.CreateDirectory(_directory);
    }

    public Stream File(string path, FileMode mode, FileAccess access)
    {
        var fullPath = NormalizePath(path);
        var ret = new FileStream(fullPath, mode, access);
        return ret;
    }

    private string NormalizePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            var normalized = Path.GetFullPath(path);
            if (!normalized.StartsWith(_directory))
            {
                throw new InvalidOperationException(
                    $"Full path '{path}' not in the expected directory '{_directory}'!");
            }
            return path;
        }
        else
        {
            var ret = Path.Combine(_directory, path);
            return ret;
        }
    }

    public bool TryOpenFileInExplorer(string file)
    {
        var path = NormalizePath(file);
        return ExplorerHelper.TryOpenExplorerAndSelectFile(path);
    }
}
