using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using FmiWebsiteInterop.Teachers;
using Microsoft.Extensions.Logging;
using ScheduleLib.Application.Core.FR;
using ScheduleLib.Builders;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.WordDoc;
using TruePath;

namespace ScheduleLib.Application.Core;

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
        MaybeAsyncDisposable<FileStream> OpenCachedFile()
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

        if (!bypassCache
            && cachedFile.Value != null
            && cachedFile.Value.Length != 0)
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
            var f = cachedFile.Value;
            f.Seek(0, SeekOrigin.Begin);
            await ScheduleSerializer.Serialize(schedule, f, hashHex, cancellationToken);
            f.SetLength(f.Position);
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
    public required AbsolutePath DirectoryPath
    {
        get;
        init;
    }

    public async ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        await hasher.AppendDirectory(DirectoryPath.Value, cancellationToken);
    }

    public async ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        await TasksHelper.ParseDocumentDirIntoSchedule(
            context,
            DirectoryPath.Value,
            cancellationToken: cancellationToken);
    }
}

public sealed class EnrichWithTeacherFullNamesFromWordScheduleLoaderComponent : IScheduleLoaderComponent
{
    public required string FilePath
    {
        get;
        init => field = Path.GetFullPath(value);
    }

    public async ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        await hasher.TryAppendFileContents(
            absolutePath: FilePath,
            cancellationToken: cancellationToken);
    }

    public ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        TasksHelper.OptionallyEnrichContextWithTeacherFullNames(context.Schedule, FilePath);
        return ValueTask.CompletedTask;
    }
}

public sealed class EnrichWithTeacherFullNamesFromWebsite(
    ItUsmWebsiteTeacherDataProvider _provider,
    ILogger<EnrichWithTeacherFullNamesFromWebsite> _logger) : IScheduleLoaderComponent
{
    public ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        _ = hasher;
        _ = cancellationToken;
        // Can't determine this here.
        // Might just do this once in like a week.
        // But not from this method.
        return ValueTask.CompletedTask;
    }

    public async ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        var teachers = await _provider.Get(cancellationToken);
        foreach (var t in teachers)
        {
            var addingName = t.Name.ToNameModel();

            // Feels like a hack
            int TeacherCount() => context.Schedule.Teachers.Count;
            int prevCount = TeacherCount();

            context.Schedule.Teacher(addingName);

            if (prevCount != TeacherCount())
            {
                _logger.LogWarning("Teacher {TeacherName} not in schedule but on website", t.Name);
            }
        }
    }
}

// ReSharper disable once InconsistentNaming
public sealed class FRScheduleDirectoryLoaderComponent : IScheduleLoaderComponent
{
    public required AbsolutePath DirectoryPath
    {
        get;
        init;
    }

    public async ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        await hasher.AppendDirectory(DirectoryPath.Value, cancellationToken);
    }

    public async ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        foreach (var filePath in Directory.EnumerateFiles(DirectoryPath.Value, "*", SearchOption.AllDirectories))
        {
            await using var inputFile = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            await FrExcelParser.ParseIntoSchedule(new()
            {
                Context = context,
                InputFile = inputFile,
                StringBuilder = new(),
            });
        }
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
                    var relativePath = filePath.AsSpan(srcFullPath.Length + 1);
                    hasher.AppendFileName(relativePath);
                }
                if (hashContents)
                {
                    await hasher.TryAppendFileContents(filePath, cancellationToken);
                }
            }
        }

        public void AppendFileName(
            ReadOnlySpan<char> relativePath)
        {
            const int maxPathBytes = 4096;
            using var pathBuffer = new RentedBuffer<byte>(maxPathBytes);

            int byteCount = Encoding.UTF8.GetBytes(relativePath, pathBuffer.Span);
            hasher.AppendData(pathBuffer.Span[.. byteCount]);
        }

        public async ValueTask TryAppendFileContents(
            string absolutePath,
            CancellationToken cancellationToken)
        {
            const int bufferSize = 8192;
            using var readBuffer = new RentedBuffer<byte>(bufferSize);

            try
            {
                await using var fs = File.OpenRead(absolutePath);
                int read;
                while ((read = await fs.ReadAsync(readBuffer.Memory, cancellationToken)) > 0)
                {
                    hasher.AppendData(readBuffer.Span[.. read]);
                }
            }
            catch (FileNotFoundException)
            {
            }
        }
    }
}
