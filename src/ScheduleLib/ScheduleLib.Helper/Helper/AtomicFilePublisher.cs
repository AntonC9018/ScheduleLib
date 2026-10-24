namespace ScheduleLib.Helper;

/// <summary>Stages in the destination filesystem, flushes complete contents, then
/// atomically replaces the live file. Callers are responsible for coordinating concurrent writers.</summary>
public static class AtomicFilePublisher
{
    public static async Task Publish(
        string destination,
        Func<Stream, CancellationToken, Task> write,
        CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);
        var staged = (string) $"{destination}{($".{Guid.NewGuid():N}.tmp")}";
        try
        {
            await using (var stream = new FileStream(staged,
                             FileMode.CreateNew,
                             FileAccess.ReadWrite,
                             FileShare.None))
            {
                await write(stream, token);
                await stream.FlushAsync(token);
                stream.Flush(flushToDisk: true);
            }
            token.ThrowIfCancellationRequested();
            File.Move(sourceFileName: staged,
                destFileName: destination,
                overwrite: true);
        }
        finally
        {
            File.Delete(staged);
        }
    }
}
