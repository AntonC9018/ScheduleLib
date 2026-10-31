public sealed class TestTempDirectory : IDisposable
{
    public string Path { get; }

    public TestTempDirectory(string namePrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(namePrefix);
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"{namePrefix}-{Guid.NewGuid():N}");
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
