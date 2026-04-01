using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using ClosedXML.Excel;
using FmiWebsiteInterop.Teachers;
using Microsoft.Extensions.Logging;
using ScheduleLib.Application.Core.FR;
using ScheduleLib.Builders;
using ScheduleLib.Excel.Helper;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Excel;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.Theses.Parsing;
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
public sealed class ScheduleDirectoryExcelLoaderComponent : IScheduleLoaderComponent
{
    public required ExcelScheduleParser.Config Config { get; init; }
    public required AbsolutePath DirectoryPath { get; init; }

    public async ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        await hasher.AppendDirectory(DirectoryPath.Value, cancellationToken);
    }

    public async ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        foreach (var filePath in Directory.EnumerateFiles(DirectoryPath.Value, "*.xlsx", SearchOption.AllDirectories))
        {
            await using var inputFile = new FileStream(
                filePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite);
            await ExcelScheduleParser.ParseIntoSchedule(Config, new()
            {
                Context = context,
                InputFile = inputFile,
                StringBuilder = new(),
            });
        }
    }
}

public sealed class ConsultationsLoaderComponent(
    DriveFileLoader _driveFileLoader)
    : IScheduleLoaderComponent, IDisposable
{
    const string fileId = "1SKqhQjIesic_23haSKSwjDZc8-lLYcIA";

    private MemoryStream? _docStream;
    private async ValueTask<MemoryStream> LazyFile(CancellationToken cancellationToken)
    {
        if (_docStream is null)
        {
            _docStream = new();
            var progress = await _driveFileLoader.Load(
                _docStream,
                fileId,
                DriveFileType.Excel,
                cancellationToken);
            progress.ThrowIfNotComplete();
        }
        _docStream.Seek(0, SeekOrigin.Begin);
        return _docStream;
    }

    public async ValueTask Hash(IncrementalHash hasher, CancellationToken cancellationToken)
    {
        hasher.AppendFileName(fileId);

        var file = await LazyFile(cancellationToken);
        hasher.AppendMemoryStream(file);
    }

    private enum Column
    {
        Name,
        Day,
        Time,
        Room,
    }

    private static readonly ColumnSearchRules<Column> ColumnNames = ColumnSearchRules.Create<Column>(b =>
    {
        b.Column(Column.Name).ExactMatch("Numele/Prenumele");
        b.Column(Column.Day).ExactMatch("Ziua");
        b.Column(Column.Time).ExactMatch("Ora");
        b.Column(Column.Room).ExactMatch("Sala");
    });

    private struct RowData()
    {
        public Name? Name = null;
        public DayOfWeek Day;
        public List<TimeSlot> Time = new();
        public string? Room = null!;
    }

    private struct Processor : ICellProcessor<Column>
    {
        private readonly LessonTimeConfig _timeConfig;
        private readonly DayNameParser _dayParser;

        public Processor(
            LessonTimeConfig timeConfig,
            DayNameParser dayParser)
        {
            _timeConfig = timeConfig;
            _dayParser = dayParser;
            Data = new();
        }

        public RowData Data;

        public void Clear()
        {
            Data.Time.Clear();
            Data.Name = null;
            Data.Room = null;
        }

        public bool Process(Column column, IXLCell cell)
        {
            if (!cell.TryGetValue(out string t))
            {
                return false;
            }
            switch (column)
            {
                case Column.Name:
                {
                    var parser = new Parser(t);
                    var name = NameHelper.TryParseName(ref parser);
                    if (!parser.IsEmpty || name is null)
                    {
                        throw cell.Exception($"'{t}' did not parser as a name");
                    }
                    Data.Name = name;
                    return true;
                }
                case Column.Time:
                {
                    TimeInterval interval;
                    if (cell.TryGetValue(out TimeSpan time))
                    {
                        var startTime = new TimeOnly(hour: time.Hours, minute: time.Minutes);
                        var consultationDuration = _timeConfig.LessonDuration;
                        interval = new(startTime, startTime.Add(consultationDuration));
                    }
                    else
                    {
                        var parser = new Parser(t);
                        interval = parser.ParseTimeInterval(allowOpenInterval: true);
                        if (!parser.IsEmpty)
                        {
                            throw cell.Exception("Not consumed the interval fully.");
                        }
                    }

                    _timeConfig.GetTimeSlotsThatInclude(interval, Data.Time);
                    if (Data.Time.Count == 0)
                    {
                        throw cell.Exception("The given time doesn't match a single time slot");
                    }
                    return true;
                }
                case Column.Day:
                {
                    if (_dayParser.Map(t) is not { } day)
                    {
                        throw cell.Exception($"Unknown day of week '{t}'");
                    }
                    Data.Day = day;
                    return true;
                }
                case Column.Room:
                {
                    Data.Room = t;
                    return true;
                }
                default:
                {
                    throw Unreachable();
                }
            }
        }
    }

    public async ValueTask Apply(DocParseContext context, CancellationToken cancellationToken)
    {
        var file = await LazyFile(cancellationToken);
        ProcessExcel(context, file);
    }

    public void Dispose()
    {
        _docStream?.Dispose();
    }

    private static void ProcessExcel(DocParseContext context, MemoryStream file)
    {
        var consultationCourseId = context.Schedule.Course("Consultație");

        using var workbook = new XLWorkbook(file);
        var worksheet = workbook.Worksheets.First();
        const int uselessRowCount = 3;

        const int headerRowNumber = uselessRowCount + 1;
        var headerRow = worksheet.Row(headerRowNumber);
        using var mappings = headerRow.FindMappings(ColumnNames);

        const int dataRowStart = headerRowNumber + 1;
        int potentialDataRowEnd = worksheet.LastRowUsed()!.RowNumber();
        var dataRows = worksheet.Rows(dataRowStart, potentialDataRowEnd);
        var processor = new Processor(context.TimeConfig, context.DayNameParser);
        foreach (var dataRow in dataRows)
        {
            processor.Clear();
            var setRows = dataRow.Cells().ProcessRow(ref processor, mappings);
            if (setRows.AreNoneSet)
            {
                break;
            }

            var notSetRows = setRows.Flipped;
            if (!notSetRows.IsEmpty)
            {
                throw dataRow.Exception($"The following rows were not set: '{notSetRows}'");
            }

            ref var data = ref processor.Data;
            var teacher = context.Schedule.Teacher(data.Name!.ToNameModel());
            var room = context.GetOrAddRoom(data.Room!);

            foreach (var timeSlot in data.Time)
            {
                var lesson = context.Schedule.RegularLesson();
                lesson.Teacher(teacher);
                lesson.Room(room);
                lesson.TimeSlot(timeSlot);
                lesson.DayOfWeek(data.Day);
                lesson.Type(LessonType.Consultation);
                lesson.Course(consultationCourseId);
            }
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

        public void AppendMemoryStream(MemoryStream stream)
        {
            stream.TryGetBuffer(out var buffer);
            var span = buffer.AsSpan();
            hasher.AppendData(span);
        }
    }
}
