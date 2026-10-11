using ScheduleLib.Builders;
using System.Text.Json;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Generation;
using ScheduleLib.Import.Pdf;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;

namespace FmiScheduleImport;

public static class FmiPdfImport
{
    public static async Task GenerateAsync(ScheduleUpdate update, string outputPath, bool strict = false,
        CancellationToken cancellationToken = default)
    {
        if (Directory.Exists(outputPath) && Directory.EnumerateFileSystemEntries(outputPath).Any())
            throw new IOException("Output directory must be absent or empty.");
        var start = update.PeriodStart;
        var cells = new List<PdfScheduleCell>();
        foreach (var source in update.Downloads)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var pdf = new MemoryStream(source.Content, writable: false);
            cells.AddRange(PdfScheduleImporter.Read(pdf, $"orar{source.Link.Year}.pdf"));
        }
        if (cells.Count == 0) throw new FormatException("The PDFs contain no schedule cells.");
        var context = ScheduleImportContext.Create(new()
        {
            DayNameProvider = new(),
            CourseNameUnifierConfig = Config.CourseNameUnifier,
            ParserFactory = new(new() { ProcessSpacesCourseName = Config.WhiteSpaceActionCourseName }),
            SpecializationRegistry = Config.SpecializationRegistry,
        });
        context.Schedule.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(start.Month >= 8 ? start.Year : start.Year - 1),
            GroupLabelsThatAreMaster = Config.GroupLabelsThatAreMaster,
        });
        context.Schedule.ConfigureRemappings(Config.ConfigureRemappings);
        var cohorts = new ElectiveCohorts(context, cells);
        context.Schedule.ImplicitSplitConfig = cohorts.SplitConfig;
        context.Schedule.OverlapValidationConfig = new()
        {
            StudyYear = context.Schedule.GroupParseContext.CurrentStudyYear,
        };
        context.SetPeriod(new() { StartDate = start });
        PdfScheduleImporter.Import(context, cells, cohorts.Add);
        Schedule schedule;
        string? sourceConflicts = null;
        try
        {
            schedule = context.Schedule.Build();
        }
        catch (OverlappingLessonsException e) when (!strict)
        {
            // A source import must preserve conflicts rather than silently change lesson
            // times or membership. All other model validation still runs on the retry.
            sourceConflicts = e.Message;
            Console.Error.WriteLine("Source conflicts retained in generated documents:\n" + sourceConflicts);
            context.Schedule.OverlapValidationConfig = null;
            schedule = context.Schedule.Build();
        }
        var output = new OutputDirectory(Path.GetFullPath(outputPath));
        output.Initialize();
        await File.WriteAllTextAsync(Path.Combine(outputPath, "import-report.json"),
            JsonSerializer.Serialize(new
            {
                PeriodStart = start,
                Cells = cells.Count,
                Groups = schedule.Groups.Length,
                Teachers = schedule.Teachers.Length,
                Lessons = schedule.EnumerateAllLessons().Count(),
                SourceConflicts = sourceConflicts,
                CohortProjection = "Teaching-section Gr.1/Gr.2 are temporarily projected onto elective alternatives, independently of academic-group I/II. Shared lectures belong to both. See README for model limitations.",
            }, new JsonSerializerOptions { WriteIndented = true }));
        await using (var json = File.Create(Path.Combine(outputPath, "schedule.json")))
        {
            await ScheduleSerializer.Serialize(schedule, json, "", cancellationToken);
        }
        var handler = new GeneratePdfsForGroupsAndTeachersTaskHandler(
            new(new SubGroupNumberDisplayHandler(), new ParityDisplayHandler(), new LessonTypeDisplayHandler()),
            context.TimeConfig, new TimeSlotDisplayHandler(), new DayNameProvider(),
            Config.SpecializationRegistry, schedule);
        await handler.Run(new() { CancellationToken = cancellationToken, OutputDirectory = output });
        var sourceDirectory = Path.Combine(outputPath, "sources");
        Directory.CreateDirectory(sourceDirectory);
        foreach (var source in update.Downloads)
            await File.WriteAllBytesAsync(Path.Combine(sourceDirectory, $"orar{source.Link.Year}.pdf"), source.Content, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "cells.json"), JsonSerializer.Serialize(cells, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "sources.json"), JsonSerializer.Serialize(update.Sources, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
    }
}
