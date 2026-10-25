using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

/// <summary>Validated private output location for registry-history topic candidates.</summary>
public sealed class RegistryHistoryTopicDirectory
{
    private readonly string _path;

    public RegistryHistoryTopicDirectory(string path)
    {
        _path = Path.GetFullPath(path);
        Validate();
    }

    public Task<RegistryHistoryTopicFilesResult> Save(RegistryHistoryTopicsAnalysis analysis, CancellationToken token)
    {
        return RegistryHistoryTopicFiles.Save(this, analysis, token);
    }

    internal void EnsureCreated()
    {
        Validate();
        CreatePrivateDirectory(_path);
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path);
            return;
        }
        const UnixFileMode permissions = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
        Directory.CreateDirectory(path, permissions);
        File.SetUnixFileMode(path, permissions);
    }

    internal FileStream NewFile(string name)
    {
        var path = Path.Combine(_path, name);
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        return new(path, options);
    }

    private void Validate()
    {
        var path = Path.TrimEndingDirectorySeparator(_path);
        var name = Path.GetFileName(path);
        if (name != ".registry-history")
        {
            throw new IOException("Private history output requires a directory named .registry-history.");
        }
        for (var current = new DirectoryInfo(_path); current is not null; current = current.Parent)
        {
            if (IsSymbolicDirectory(current))
            {
                throw new IOException("Private history output requires regular directories.");
            }
        }
    }

    private static bool IsSymbolicDirectory(DirectoryInfo directory)
    {
        if (directory.LinkTarget is not null)
        {
            return true;
        }
        if (!directory.Exists)
        {
            return false;
        }
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            return true;
        }
        return false;
    }
}
