using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using AutoConstructor.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;

namespace MainCli;

[AutoConstructor]
public sealed partial class ScheduleBuilderInitializer : IScheduleInitializer
{
    private readonly LessonTimeConfig _timeConfig;
    private readonly DayNameParser _dayNameParser;
    private readonly CourseNameUnifierModule _unifier;
    private readonly LessonParserFactory _lessonParserFactory;
    private readonly IOptions<StudyYearOptions> _studyYearOptions;
    private readonly ILogger _logger;
    private readonly ConfigureRemappingsDelegate _configureRemappings;

    public async Task Initialize(
        ScheduleBuilder builder,
        CancellationToken cancellationToken)
    {
        builder.ConfigureRemappings(_configureRemappings);
        builder.EnableLookupModule();

        var context = new DocParseContext
        {
            CourseNameUnifierModule = _unifier,
            DayNameParser = _dayNameParser,
            ParserFactory = _lessonParserFactory,
            Schedule = builder,
            TimeConfig = _timeConfig,
        };

        // TODO: Do this in a more adequate way
        var studyYear = _studyYearOptions.Value;
        string scheduleSourcesDir = @$"data\{studyYear.StudyYear}_sem{studyYear.Semester.AsOrdinal()}";
        string serializedSchedulePath = @$"data\schedule_{studyYear.StudyYear}_{studyYear.Semester.AsOrdinal()}.json";
        await LoadSchedule(
            context: context,
            scheduleSourcesDir: scheduleSourcesDir,
            serializedSchedulePath: serializedSchedulePath,
            bypassCache: false,
            beforeEndAction: static context =>
            {
                // TODO: This is not included in the hash
                const string fileName = @"data\Cadre didactice DI 2024-2025.xlsx";
                TasksHelper.OptionallyEnrichContextWithTeacherFullNames(context.Schedule, fileName);
            },
            cancellationToken: cancellationToken);

        _logger.LogInformation("Schedule built");
    }

    public static async Task LoadSchedule(
        DocParseContext context,
        string scheduleSourcesDir,
        string serializedSchedulePath,
        Action<DocParseContext> beforeEndAction,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        var scheduleSourcesDirFullPath = Path.GetFullPath(scheduleSourcesDir);

        async ValueTask<SerializationModels.ScheduleModel?> GetValidModel()
        {
            if (bypassCache)
            {
                return null;
            }
            if (!Path.Exists(serializedSchedulePath))
            {
                return null;
            }

            var filesHash = GetDirectoryHash(scheduleSourcesDirFullPath);

            await using var inputFile = File.OpenRead(serializedSchedulePath);
            var serializedModel = await ScheduleSerializer.Deserialize(inputFile, cancellationToken);
            if (serializedModel.Hash != filesHash)
            {
                return null;
            }

            return serializedModel;
        }

        if (await GetValidModel() is { } scheduleSerializedModel)
        {
            ScheduleSerializer.AddToBuilder(
                context.Schedule,
                scheduleSerializedModel,
                context.CourseNameUnifierModule);

            beforeEndAction(context);
            return;
        }

        {
            await TasksHelper.ParseDocumentDirIntoSchedule(
                context,
                scheduleSourcesDirFullPath,
                cancellationToken: cancellationToken);

            beforeEndAction(context);

            var schedule = context.Schedule.Build();

            // I think word resaves them in some way.
            var newFilesHash = GetDirectoryHash(scheduleSourcesDirFullPath);
            await using var outputFile = new FileStream(serializedSchedulePath, FileMode.Create);
            await ScheduleSerializer.Serialize(schedule, outputFile, newFilesHash, cancellationToken);
            return;
        }
    }

    public static string GetDirectoryHash(
        string srcFullPath,
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

        const int maxPathBytes = 4096;
        const int bufferSize = 8192;
        using var pathBuffer = new RentedBuffer<byte>(maxPathBytes);
        using var readBuffer = new RentedBuffer<byte>(bufferSize);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        foreach (var filePath in filePaths)
        {
            if (hashPaths)
            {
                var relativePath = filePath.AsSpan(srcFullPath.Length + 1);
                int byteCount = Encoding.UTF8.GetBytes(relativePath, pathBuffer.Span);
                hasher.AppendData(pathBuffer.Span[.. byteCount]);
            }

            if (hashContents)
            {
                using var fs = File.OpenRead(filePath);
                int read;
                while ((read = fs.Read(readBuffer.Span)) > 0)
                {
                    hasher.AppendData(readBuffer.Span[.. read]);
                }
            }
        }

        var hashLen = hasher.HashLengthInBytes;
        using var hash = new RentedBuffer<byte>(hashLen);
        int len = hasher.GetCurrentHash(hash.Span);
        Debug.Assert(len == hashLen);
        return Convert.ToHexStringLower(hash.Span);
    }

}
