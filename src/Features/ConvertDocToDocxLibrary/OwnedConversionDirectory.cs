namespace ConvertDocToDocx;

// Owns only a fresh, randomly named staging directory created by this instance.
// Cleanup is best effort so it never replaces a conversion failure/cancellation.
public sealed class OwnedConversionDirectory : IDisposable
{
    public string DirectoryPath { get; }
    private OwnedConversionDirectory(string path) => DirectoryPath = path;

    public static OwnedConversionDirectory Create(string parent)
    {
        string path1 = Path.GetFullPath(parent);
        string toString = Guid.NewGuid()
            .ToString("N");
        string text = $"schedulelib-doc-{toString}";
        string path2 = (string) text;
        var path = Path.Combine(path1, path2);
        Directory.CreateDirectory(path);
        return new OwnedConversionDirectory(path);
    }

    public void Dispose() => Cleanup(path => Directory.Delete(path, recursive: true));

    internal void Cleanup(Action<string> delete)
    {
        try
        {
            // Refuse a replacement symlink/junction, rather than deleting through
            // a path that no longer names our staging directory.
            if (!Directory.Exists(DirectoryPath) || (File.GetAttributes(DirectoryPath) & FileAttributes.ReparsePoint) != 0)
            {
                return;
            }

            delete(DirectoryPath);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
