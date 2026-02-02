using ScheduleLib.Helper;

namespace ScheduleLib.Application.Core.Helper;

public readonly record struct FilePath(string Path);

public readonly record struct FileInDirectory
{
    public OutputDirectory Directory { get; }
    public string Path { get; }

    public FileInDirectory(OutputDirectory directory, string path)
    {
        Path = path;
        Directory = directory;
    }

    public Stream Open(FileMode mode, FileAccess access, FileShare? share = null)
    {
        return Directory.OpenFile(Path, mode, access, share);
    }
    public bool TryOpenInExplorer()
    {
        return Directory.TryOpenFileInExplorer(Path);
    }
}


public sealed class OutputDirectory
{
    private readonly string _directory;

    public OutputDirectory(string directory)
    {
        _directory = Path.GetFullPath(directory);
    }

    public void Clear()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
            Directory.CreateDirectory(_directory);
        }
    }

    public IEnumerable<FileInDirectory> Files(string pattern, EnumerationOptions options)
    {
        return FilePaths(pattern, options)
            .Select(x => File(x.Path));
    }

    public IEnumerable<FilePath> FilePaths(string pattern, EnumerationOptions options)
    {
        var files = Directory.EnumerateFiles(
            _directory,
            searchPattern: pattern,
            enumerationOptions: options);
        var ret = files.Select(x =>
        {
            var separatorLen = 1;
            var ret = x[(_directory.Length + separatorLen) ..];
            return new FilePath(ret);
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

    public Stream OpenFile(string path, FileMode mode, FileAccess access, FileShare? share = null)
    {
        var fullPath = NormalizePath(path);
        var ret = new FileStream(fullPath, mode, access, share ?? FileShare.Read);
        return ret;
    }
    public FileInDirectory File(string path)
    {
        return new FileInDirectory(this, path);
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

    public bool TryOpenInExplorer()
    {
        return ExplorerHelper.TryOpenExplorerAndSelectFile(_directory);
    }

    public bool TryOpenFileInExplorer(string file)
    {
        var path = NormalizePath(file);
        return ExplorerHelper.TryOpenExplorerAndSelectFile(path);
    }
}
