using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.WordDoc;

namespace MainCli;

public sealed class ScheduleLoader
{
    public string? CachedPath { get; set; }
    public List<IScheduleLoaderComponent> Components { get; set; } = new();

    public async ValueTask Load(
        DocParseContext context,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        string? hashHex = null;
        {
            bool shouldComputeHash = CachedPath != null;
            if (shouldComputeHash)
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
                foreach (var loader in Components)
                {
                    await loader.Hash(hasher, cancellationToken);
                }
                hashHex = hasher.ToHexString();
            }
        }

        await using var cachedFile = OpenCachedFile();
        MaybeDisposable<FileStream> OpenCachedFile()
        {
            if (CachedPath is { } cachedPath)
            {
#pragma warning disable CA2000 // file not disposed
                var ret = new FileStream(cachedPath, FileMode.OpenOrCreate, FileAccess.ReadWrite);
#pragma warning restore CA2000
                return new(ret);
            }
            return default;
        }

        if (!bypassCache && cachedFile.Value != null)
        {
            var serializedModel = await ScheduleSerializer.Deserialize(cachedFile.Value, cancellationToken);
            Debug.Assert(hashHex is not null);
            if (serializedModel.Hash == hashHex)
            {
                ScheduleSerializer.AddToBuilder(
                    context.Schedule,
                    serializedModel,
                    context.CourseNameUnifierModule);
                return;
            }
        }

        foreach (var loader in Components)
        {
            await loader.Apply(context, cancellationToken);
        }

        if (cachedFile.Value != null)
        {
            var schedule = context.Schedule.Build();
            Debug.Assert(hashHex != null);
            cachedFile.Value.Seek(0, SeekOrigin.Begin);
            await ScheduleSerializer.Serialize(schedule, cachedFile.Value, hashHex, cancellationToken);
        }
    }
}

public interface IScheduleLoaderComponent
{
    public ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken);
    public ValueTask Apply(DocParseContext context, CancellationToken cancellationToken);
}

public sealed class DirectoryScheduleLoaderComponent : IScheduleLoaderComponent
{
    public required string DirectoryPath { get; init; }

    public async ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        await hasher.AppendDirectory(DirectoryPath, cancellationToken);
    }

    public async ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        await TasksHelper.ParseDocumentDirIntoSchedule(
            context,
            DirectoryPath,
            cancellationToken: cancellationToken);
    }
}

public sealed class EnrichWithTeacherFullNamesScheduleLoaderComponent : IScheduleLoaderComponent
{
    public required string FilePath { get; init; }

    public async ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        await hasher.AppendFileContents(
            absolutePath: FilePath,
            cancellationToken: cancellationToken);
    }

    public ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        TasksHelper.OptionallyEnrichContextWithTeacherFullNames(context.Schedule, FilePath);
        return ValueTask.CompletedTask;
    }
}

public static class HashHelper
{
    extension(IncrementalHash hasher)
    {
        public string ToHexString()
        {
            var hashLen = hasher.HashLengthInBytes;
            using var hash = new RentedBuffer<byte>(hashLen);
            int len = hasher.GetCurrentHash(hash.Span);
            Debug.Assert(len == hashLen);
            return Convert.ToHexStringLower(hash.Span);
        }

        public async ValueTask AppendDirectory(
            string srcFullPath,
            CancellationToken cancellationToken,
            string searchPattern = "*",
            bool hashPaths = true,
            bool hashContents = true)
        {
            Debug.Assert(srcFullPath == Path.GetFullPath(srcFullPath));

            var filePaths = Directory.GetFiles(
                    srcFullPath,
                    searchPattern: searchPattern,
                    SearchOption.AllDirectories)
                .OrderBy(p => p)
                .ToArray();

            foreach (var filePath in filePaths)
            {
                if (hashPaths)
                {
                    var relativePath = filePath.AsMemory(srcFullPath.Length + 1);
                    hasher.AppendFileName(relativePath);
                }
                if (hashContents)
                {
                    await hasher.AppendFileContents(filePath, cancellationToken);
                }
            }
        }

        public void AppendFileName(
            ReadOnlyMemory<char> relativePath)
        {
            const int maxPathBytes = 4096;
            using var pathBuffer = new RentedBuffer<byte>(maxPathBytes);

            int byteCount = Encoding.UTF8.GetBytes(relativePath.Span, pathBuffer.Span);
            hasher.AppendData(pathBuffer.Span[.. byteCount]);
        }

        public async ValueTask AppendFileContents(
            string absolutePath,
            CancellationToken cancellationToken)
        {
            const int bufferSize = 8192;
            using var readBuffer = new RentedBuffer<byte>(bufferSize);

            await using var fs = File.OpenRead(absolutePath);
            int read;
            while ((read = await fs.ReadAsync(readBuffer.Memory, cancellationToken)) > 0)
            {
                hasher.AppendData(readBuffer.Span[.. read]);
            }
        }
    }
}

file readonly record struct MaybeDisposable<T> : IAsyncDisposable
    where T : IAsyncDisposable
{
    public T? Value { get; }

    public MaybeDisposable(T? value)
    {
        Value = value;
    }

    public async ValueTask DisposeAsync()
    {
        if (Value != null)
        {
            await Value.DisposeAsync();
        }
    }
}
