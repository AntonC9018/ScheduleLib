using ScheduleLib.Parsing;
using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Builders;
using ScheduleLib.DependencyInjection.ScopePlans;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Application.Config;
using ScheduleLib.Dates;
using ScheduleLib.Parsing.WordDoc;

namespace ScheduleLib.Application.Core;

/// <summary>
/// Where a scope's schedule is read from and the cache it may use. A seed supplied when the scope
/// opens; the cache defaults to the data directory.
/// </summary>
public sealed record ScheduleSource(string DataDirectory, string? CacheDirectory = null, bool UseCache = true);

/// <summary>How the loader builds a schedule, independently of where it reads it from.</summary>
public sealed class ScheduleSourceLoaderOptions
{
    public bool BypassCache { get; set; } = false;
    public bool EnrichWithFullNames { get; set; } = true;
    public bool LoadConsultations { get; set; } = true;
    public SubGroupValidationMode SubGroupValidation { get; set; } = SubGroupValidationMode.Strict;
}

/// <summary>
/// Builds the schedule of a source in a local builder, or reads it from the source's cache.
/// The builder is never shared: the result is an immutable <see cref="Schedule"/>.
/// </summary>
[AutoConstructor]
public sealed partial class ScheduleSourceLoader
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<StudyYearOptions> _studyYearOptions;
    private readonly ILogger<ScheduleSourceLoader> _logger;
    private readonly ConfigureRemappingsDelegate _configureRemappings;
    private readonly Remappings _remappings;
    private readonly GroupParseContext _groupParseContext;
    private readonly CourseNameUnifierConfig _courseNameUnifierConfig;
    private readonly IOptions<ScheduleSourceLoaderOptions> _opts;

    public async Task<Schedule> Load(ScheduleSource source, CancellationToken cancellationToken)
    {
        // The loader components are transient and some are disposable, so they live in a
        // scope of their own; none of them depends on a workflow's context.
        await using var scope = _scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var builder = new ScheduleBuilder
        {
            GroupParseContext = _groupParseContext,
            ImplicitSplitConfig = ScheduleDefaults.Config.ImplicitSplitConfig,
            OverlapValidationConfig = ScheduleDefaults.Config.OverlapValidationConfig,
            ValidationSettings = new()
            {
                SubGroup = _opts.Value.SubGroupValidation,
            },
        };
        builder.UseRemappings(_remappings);
        builder.EnableLookupModule();

        var loader = new ScheduleLoader();

        var studyYear = _studyYearOptions.Value;
        var opts = _opts.Value;
        if (source.UseCache)
        {
            var encoding = System.Text.Encoding.UTF8;
            string dataPath = Path.GetFullPath(source.DataDirectory);
            byte[] dataPathBytes = encoding.GetBytes(dataPath);
            byte[] inArray = System.Security.Cryptography.SHA256.HashData(dataPathBytes);
            var rootIdentity = Convert.ToHexStringLower(inArray);
            string cacheDir = source.CacheDirectory ?? source.DataDirectory;
            string cacheFileName =
                $"{rootIdentity}_schedule_{studyYear.StudyYear!.Value.Value}_{studyYear.Semester.AsOrdinal()}.json";
            loader.CachedPath = Path.Combine(cacheDir, cacheFileName);
        }

        {
            var rootPath = new TruePath.LocalPath(source.DataDirectory);
            var scheduleDirs = ScheduleDirectoryDiscovery.DiscoverDirectories(path: rootPath)
                .OrderBy(x => x.StudyYear)
                .ThenBy(x => x.Semester)
                .ThenBy(x => x.AttendanceMode);
            var matchingDirs = scheduleDirs.MatchingStudyYear(studyYear)
                .ToArray();
            if (matchingDirs.Length == 0)
            {
                throw new ScheduleSourcesNotFoundException(
                    studyYear.StudyYear,
                    studyYear.Semester,
                    source.DataDirectory);
            }

            var loaders = matchingDirs.SelectMany(x => x.GetLoaders());
            loader.Components.AddRange(loaders);
        }

        if (opts.EnrichWithFullNames)
        {
            // loader.Components.Add(new EnrichWithTeacherFullNamesFromWordScheduleLoaderComponent
            // {
            //     FilePath = Path.Combine(opts.DataDirectory, "Cadre didactice DI 2024-2025.xlsx"),
            // });

            var websiteLoader = sp.GetRequiredService<EnrichWithTeacherFullNamesFromWebsite>();
            loader.Components.Add(websiteLoader);
        }

        if (opts.LoadConsultations)
        {
            var l = sp.GetRequiredService<ConsultationsLoaderComponent>();
            loader.Components.Add(l);
        }

        var unifier = new CourseNameUnifierModule(_courseNameUnifierConfig);
        var context = ActivatorUtilities.CreateInstance<ScheduleImportContext>(sp, builder, unifier);

        // Code-defined parser/remapping changes invalidate the cache too. Runtime
        // initialization options are included independently of teacher/profile identity.
        {
            // Snapshot each input in the original argument order, before serialization runs.
            var initializerVersion = typeof(ScheduleSourceLoader).Module.ModuleVersionId;
            var builderVersion = typeof(ScheduleBuilder).Module.ModuleVersionId;
            var remappingVersion = _configureRemappings.Method.Module.ModuleVersionId;
            var identityStudyYear = studyYear.StudyYear;
            var identitySemester = studyYear.Semester;
            var enrichWithFullNames = opts.EnrichWithFullNames;
            var loadConsultations = opts.LoadConsultations;
            var subGroupValidation = opts.SubGroupValidation;
            var timeConfiguration = System.Text.Json.JsonSerializer.Serialize(
                context.TimeConfig,
                new System.Text.Json.JsonSerializerOptions
                {
                    IncludeFields = true,
                });
            var subGroupRemappings =
                System.Text.Json.JsonSerializer.Serialize(_remappings.SubGroupNameRemappings);
            var groupRemappings =
                System.Text.Json.JsonSerializer.Serialize(_remappings.GroupNameRemappings);
            var teacherRemappings = string.Join(
                ";",
                _remappings.TeacherLastNameRemappings.OrderBy(x => x.Key.ToString())
                    .Select(x => $"{x.Key}={x.Value}"));
            loader.ConfigurationIdentity = string.Join(
                "|",
                "cache-v4",
                initializerVersion,
                builderVersion,
                remappingVersion,
                identityStudyYear,
                identitySemester,
                enrichWithFullNames,
                loadConsultations,
                subGroupValidation,
                timeConfiguration,
                subGroupRemappings,
                groupRemappings,
                teacherRemappings);
        }
        var schedule = await loader.Load(
            context,
            cancellationToken,
            bypassCache: opts.BypassCache);

        _logger.LogInformation("Schedule built");
        return schedule;
    }
}

/// <summary>Loads the schedule of a scope and derives what workflows read from it.</summary>
[AutoConstructor]
public sealed partial class LoadedScheduleInitializer : IInitializer<LoadedSchedule>
{
    private readonly ScheduleSourceLoader _loader;
    private readonly ScheduleSource _source;
    private readonly ScheduleDerivationOptions _options;
    private readonly CourseNameUnifierConfig _courseNameUnifierConfig;
    private readonly ILogger<LoadedScheduleInitializer> _logger;

    public async ValueTask<LoadedSchedule> InitializeAsync(CancellationToken cancellationToken)
    {
        var built = await _loader.Load(_source, cancellationToken);
        var schedule = LabSubGroupMerges.Project(built, _options.AutoMergeLabSubGroups);
        LogLabSubGroupMerges(schedule);
        return LoadedSchedule.Create(schedule, _courseNameUnifierConfig);
    }

    private void LogLabSubGroupMerges(Schedule schedule)
    {
        foreach (var diagnostic in schedule.LabSubGroupMergeDiagnostics)
        {
            var course = schedule.Get(diagnostic.Course);
            var group = schedule.Get(diagnostic.Group);
            _logger.LogWarning(
                "Cannot infer a lab subgroup merge for {Course}, {Group}, {Specialization}, {Alternative}: {Reason}",
                course.FullName,
                group.Name,
                diagnostic.Specialization.Value,
                diagnostic.Alternative.Value,
                diagnostic.Reason);
        }

        if (!schedule.AutomaticLabSubGroupMerges.IsEmpty)
        {
            _logger.LogInformation(
                "Derived {Count} automatic lab subgroup merges for the semester",
                schedule.AutomaticLabSubGroupMerges.Length);
        }
    }
}

public sealed class ScheduleFactory(LoadedSchedule loaded) : IFactory<Schedule>
{
    public Schedule Create() => loaded.Schedule;
}

public sealed class ScheduleLookupFactory(LoadedSchedule loaded) : IFactory<ScheduleLookup>
{
    public ScheduleLookup Create() => loaded.Lookup;
}
