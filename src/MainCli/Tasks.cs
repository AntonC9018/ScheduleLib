using System.Diagnostics;
using System.Globalization;
using ConvertDocToDocx;
using DocumentFormat.OpenXml.Packaging;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Helper;
using ScheduleLib.Parsing.WordDoc;

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
                var outputPath = PathHelper.WithExtension(filePath, ".docx");
                var conversionSuccessful = await DocToDocxConversionHelper.TryConvertFile(
                    inputPath: filePath,
                    outputPath: outputPath,
                    cancellationToken: cancellationToken);
                if (!conversionSuccessful)
                {
                    throw new InvalidOperationException("Could not convert doc to docx");
                }
                File.Delete(filePath);
            }

            foreach (var filePath in Directory.EnumerateFiles(dirName, "*.docx", SearchOption.TopDirectoryOnly))
            {
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
}
