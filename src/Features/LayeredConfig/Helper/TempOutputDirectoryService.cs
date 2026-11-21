using ScheduleLib.Helper;

namespace ScheduleLib.Application.Core.Helper;

public readonly record struct RelativeFilePath(string Path);

public sealed class OutputDirectory
{
    private readonly string _directory;

    public OutputDirectory(string directory)
    {
        _directory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
    }

    public IEnumerable<RelativeFilePath> FilePaths(string pattern, EnumerationOptions options)
    {
        var files = Directory.EnumerateFiles(
            path: _directory,
            searchPattern: pattern,
            enumerationOptions: options);
        var ret = files.Select(x => new RelativeFilePath(Path.GetRelativePath(relativeTo: _directory, path: x)));
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
        string candidate;
        if (Path.IsPathFullyQualified(path))
        {
            candidate = path;
        }
        else
        {
            candidate = Path.Combine(_directory, path);
        }

        var normalized = Path.GetFullPath(candidate);
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!IsWithinDirectory(normalized, comparison))
        {
            throw new FullPathOutsideTempOutputDirectoryException(path: path, expectedDirectory: _directory);
        }
        return normalized;
    }

    private bool IsWithinDirectory(string normalizedPath, StringComparison comparison)
    {
        if (normalizedPath.Equals(_directory, comparison))
        {
            return true;
        }
        if (!normalizedPath.StartsWith(_directory, comparison))
        {
            return false;
        }
        if (Path.EndsInDirectorySeparator(_directory))
        {
            return true;
        }
        char separator = normalizedPath[_directory.Length];
        if (separator == Path.DirectorySeparatorChar)
        {
            return true;
        }
        if (separator == Path.AltDirectorySeparatorChar)
        {
            return true;
        }
        return false;
    }

    public bool TryOpenFileInExplorer(string file)
    {
        var path = NormalizePath(file);
        return ExplorerHelper.TryOpenExplorerAndSelectFile(path);
    }
}
