public sealed class TestTempDirectory : IDisposable
{
    public string Path { get; }

    public TestTempDirectory(string namePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);
        var temporaryDirectory = System.IO.Path.GetTempPath();
        var uniqueId = Guid.NewGuid();
        var directoryName = $"{namePrefix}-{uniqueId:N}";
        Path = System.IO.Path.Combine(temporaryDirectory, directoryName);
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
