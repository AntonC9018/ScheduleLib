using ScheduleLib.Helper;

namespace ScheduleLib.Application.Core.Helper;

public readonly record struct RelativeFilePath(string Path);

public sealed class OutputDirectory
{
    private readonly string _directory;

    public OutputDirectory(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public IEnumerable<RelativeFilePath> FilePaths(string pattern, EnumerationOptions options)
    {
        var files = Directory.EnumerateFiles(
            path: _directory,
            searchPattern: pattern,
            enumerationOptions: options);
        var ret = files.Select(x =>
        {
            RelativeFilePath ProjectValue()
            {
                var separatorLen = 1;
                var ret = x[(_directory.Length + separatorLen)..];
                return new RelativeFilePath(ret);
            }

            return ProjectValue();
        });
        return ret;
    }

    public void Initialize(bool clear = false)
    {
        if (clear)
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        else if (Directory.Exists(_directory))
        {
            return;
        }
        Directory.CreateDirectory(_directory);
    }

    public Stream OpenFile(
        string path,
        FileMode mode,
        FileAccess access,
        FileShare? share = null)
    {
        var fullPath = NormalizePath(path);
        var ret = new FileStream(
            fullPath,
            mode,
            access,
            share ?? FileShare.Read);
        return ret;
    }

    public string BuildPath(string path)
    {
        return NormalizePath(path);
    }

    private string NormalizePath(string path)
    {
        if (Path.IsPathFullyQualified(path))
        {
            var normalized = Path.GetFullPath(path);
            if (!normalized.StartsWith(_directory))
            {
                throw new FullPathOutsideTempOutputDirectoryException(path: path, expectedDirectory: _directory);
            }
            return path;
        }
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
