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

        var loader = new ScheduleLoader();

        // TODO: Do this config in a more adequate way
        var studyYear = _studyYearOptions.Value;

        loader.CachedPath = @$"data\schedule_{studyYear.StudyYear}_{studyYear.Semester.AsOrdinal()}.json";
        loader.Components.Add(new DirectoryScheduleLoaderComponent
        {
            DirectoryPath = @$"data\{studyYear.StudyYear}_sem{studyYear.Semester.AsOrdinal()}",
        });
        loader.Components.Add(new FRScheduleLoaderComponent
        {
            FilePath = @"data\2025_sem1\fr.xlsx",
        });
        loader.Components.Add(new EnrichWithTeacherFullNamesScheduleLoaderComponent
        {
            FilePath = @"data\Cadre didactice DI 2024-2025.xlsx",
        });
        await loader.Load(
            context,
            cancellationToken,
            bypassCache: false);

        _logger.LogInformation("Schedule built");
    }
}
