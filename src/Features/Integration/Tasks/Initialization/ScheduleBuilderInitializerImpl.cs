using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Application.Core;

public sealed class ScheduleBuilderInitializerOptions
{
    public bool BypassCache { get; set; } = false;
    public bool UseCache { get; set; } = true;
    public bool EnrichWithFullNames { get; set; } = true;
    public bool LoadConsultations { get; set; } = true;
}

[AutoConstructor]
public sealed partial class ScheduleBuilderInitializer : IScheduleInitializer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<StudyYearOptions> _studyYearOptions;
    private readonly ILogger _logger;
    private readonly ConfigureRemappingsDelegate _configureRemappings;
    private readonly IOptions<ScheduleBuilderInitializerOptions> _opts;

    public async Task Initialize(
        ScheduleBuilder builder,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        builder.ConfigureRemappings(_configureRemappings);
        builder.EnableLookupModule();

        var loader = new ScheduleLoader();

        var studyYear = _studyYearOptions.Value;
        var opts = _opts.Value;
        if (opts.UseCache)
        {
            loader.CachedPath = @$"data\schedule_{studyYear.StudyYear}_{studyYear.Semester.AsOrdinal()}.json";
        }

        {
            var scheduleDirs = ScheduleDirectoryDiscovery.DiscoverDirectories(path: new("data"))
                .OrderBy(x => x.StudyYear)
                .ThenBy(x => x.Semester)
                .ThenBy(x => x.AttendanceMode);
            var matchingDirs = scheduleDirs.MatchingStudyYear(studyYear);
            var loaders = matchingDirs.Select(x => x.GetLoader());
            loader.Components.AddRange(loaders);
        }

        if (opts.EnrichWithFullNames)
        {
            // loader.Components.Add(new EnrichWithTeacherFullNamesFromWordScheduleLoaderComponent
            // {
            //     FilePath = @"data\Cadre didactice DI 2024-2025.xlsx",
            // });

            var websiteLoader = sp.GetRequiredService<EnrichWithTeacherFullNamesFromWebsite>();
            loader.Components.Add(websiteLoader);
        }

        if (opts.LoadConsultations)
        {
            var l = sp.GetRequiredService<ConsultationsLoaderComponent>();
            loader.Components.Add(l);
        }

        var context = ActivatorUtilities.CreateInstance<DocParseContext>(sp, builder);
        await loader.Load(
            context,
            cancellationToken,
            bypassCache: opts.BypassCache);

        _logger.LogInformation("Schedule built");
    }
}

