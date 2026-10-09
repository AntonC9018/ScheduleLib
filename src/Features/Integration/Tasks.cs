using System.Diagnostics;
using System.Globalization;
using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using AutoConstructor.Attributes;
using ConvertDocToDocx;
using DocumentFormat.OpenXml.Packaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using QuizModels;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Topics;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Helper;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Application.Core;

public struct ParseStudyWeekWordDocParams
{
    public required string InputPath;
    public required HolidayPeriod[] Holidays;
}

public static class TasksHelper
{
    // ReSharper disable once UnusedMember.Global
    public static ManualWeeklyScheduledDateProvider CreateDateProviderFromWeekParityExcel(
        ParseStudyWeekWordDocParams p)
    {
        using var stream = File.OpenRead(p.InputPath);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);
        var studyWeeks = ParityExcelParser.Parse(word).ToArray();
        var ret = new ManualWeeklyScheduledDateProvider(
            studyWeeks: studyWeeks,
            holidays: p.Holidays);
        return ret;
    }

    public static void OptionallyEnrichContextWithTeacherFullNames(
        ScheduleBuilder schedule,
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        using var excel = SpreadsheetDocument.Open(filePath, isEditable: false, new()
        {
            AutoSave = false,
            CompatibilityLevel = CompatibilityLevel.Version_2_20,
        });

        ExcelTeacherListParser.AddTeachersFromExcel(new()
        {
            Excel = excel,
            Schedule = schedule,
        });
    }

    public static async Task ParseDocumentDirIntoSchedule(
        DocParseContext context,
        string dirName,
        CancellationToken cancellationToken)
    {
        dirName = Path.GetFullPath(dirName);

        await ParseDirectoryToSchedule(
            context,
            dirName,
            cancellationToken: cancellationToken);

        var subdirs = Directory.EnumerateDirectories(dirName, "*", SearchOption.TopDirectoryOnly)
            .Select(x =>
            {
                var lastSegmentStart = x.LastIndexOf(Path.DirectorySeparatorChar);
                Debug.Assert(lastSegmentStart != -1);
                lastSegmentStart += 1;

                var lastSegment = x.AsSpan()[lastSegmentStart ..];

                if (!DateOnly.TryParseExact(
                        lastSegment,
                        format: "dd.MM.yy",
                        provider: null,
                        style: DateTimeStyles.None,
                        result: out var startDate))
                {
                    throw new InvalidOperationException($"The folders must be named in the format 'DD.MM.YYYY'. Found this: {x}");
                }
                return (SubDirPath: x, StartDate: startDate);
            })
            .OrderBy(x => x.StartDate);

        foreach (var t in subdirs)
        {
            await ParseDirectoryToSchedule(
                context,
                t.SubDirPath,
                cancellationToken: cancellationToken,
                period: new()
                {
                    StartDate = t.StartDate,
                });
        }
        return;

        static async Task ParseDirectoryToSchedule(
            DocParseContext context,
            string dirName,
            CancellationToken cancellationToken,
            PeriodBeginning? period = null)
        {
            foreach (var filePath in Directory.EnumerateFiles(dirName, "*.doc", SearchOption.TopDirectoryOnly))
            {
                // CA2000 misses using-declaration disposal inside this async local function.
#pragma warning disable CA2000
                using var stagingOwner = OwnedConversionDirectory.Create(Path.GetTempPath());
#pragma warning restore CA2000
                var stagingDirectory = stagingOwner.DirectoryPath;
                {
                    var stagedInput = Path.Combine(stagingDirectory, Path.GetFileName(filePath));
                    File.Copy(filePath, stagedInput);
                    var outputPath = Path.ChangeExtension(stagedInput, ".docx");
                    if (!await DocToDocxConversionHelper.TryConvertFile(stagedInput, outputPath, cancellationToken))
                        throw new IOException("Could not convert doc to docx. Windows with Microsoft Word is required.");
                    using var document = WordprocessingDocument.Open(outputPath, isEditable: false);
                    context.SetPeriod(period);
                    WordScheduleParser.ParseToSchedule(new() { Context = context, Document = document });
                }
            }

            foreach (var filePath in Directory.EnumerateFiles(dirName, "*.docx", SearchOption.TopDirectoryOnly))
            {
                // A paired legacy source was already parsed from its staged conversion.
                if (File.Exists(Path.ChangeExtension(filePath, ".doc"))) continue;
                cancellationToken.ThrowIfCancellationRequested();
                using var document = WordprocessingDocument.Open(filePath, isEditable: false);
                context.SetPeriod(period);

                WordScheduleParser.ParseToSchedule(new()
                {
                    Context = context,
                    Document = document,
                });
            }
        }
    }

    extension(IServiceProvider sp)
    {
        public async ValueTask<RegistryScrapingContext> MakeRegistryContext(CancellationToken cancellationToken)
        {
            var credentialsResolver = sp.GetRequiredService<CredentialsResolver<BuiltRegistryConfig>>();
            var credentials = credentialsResolver.Get();
            var registryContext = await RegistryScrapingContext.Create(
                sp,
                credentials: credentials,
                cancellationToken: cancellationToken);
            return registryContext;
        }

        public async ValueTask<MoodleScrapingContext> MakeMoodleContext(CancellationToken cancellationToken)
        {
            var credentialsResolver = sp.GetRequiredService<CredentialsResolver<MoodleConfig>>();
            var credentials = credentialsResolver.Get();
            var registryContext = await MoodleScrapingContext.Create(
                sp,
                credentials: credentials,
                cancellationToken: cancellationToken);
            return registryContext;
        }

        public Semester GetCurrentSemester()
        {
            return sp.GetRequiredService<IOptions<StudyYearOptions>>().Value.Semester;
        }
    }
}
