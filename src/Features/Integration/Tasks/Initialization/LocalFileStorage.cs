namespace ScheduleLib.Application.Core;

/// <summary>Coordinates cooperating processes on this machine. Lock files persist;
/// ownership is the open exclusive handle, so termination releases the lock.</summary>
public sealed class LocalFileLock : IAsyncDisposable, IDisposable
{
    private readonly FileStream _stream;
    private LocalFileLock(FileStream stream) => _stream = stream;

    public static async Task<LocalFileLock> Acquire(string path, CancellationToken token, bool wait = false)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None)); }
            catch (IOException e) when (e.HResult == unchecked((int)0x80070020) || (e.HResult & 0xffff) == 11)
            {
                if (!wait) throw new LocalOperationBusyException($"Another operation owns {path}", e);
                await Task.Delay(100, token);
            }
        }
    }

    public ValueTask DisposeAsync() => _stream.DisposeAsync();
    public void Dispose() => _stream.Dispose();
}

public sealed class LocalOperationBusyException(string message, Exception? inner = null) : IOException(message, inner);

/// <summary>Publishes files atomically using the shared filesystem helper.
/// Callers coordinate writers with <see cref="LocalFileLock"/>.</summary>
public static class AtomicFile
{
    public static Task Publish(string destination, Func<Stream, CancellationToken, Task> write, CancellationToken token)
    {
        return ScheduleLib.Helper.AtomicFilePublisher.Publish(destination, write, token);
    }
}
