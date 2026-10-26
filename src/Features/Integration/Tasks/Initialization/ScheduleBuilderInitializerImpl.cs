using ScheduleLib.Parsing;
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
    public string DataDirectory { get; set; } = "data";
    public string? CacheDirectory { get; set; }
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
            var encoding = System.Text.Encoding.UTF8;
            string s = Path.GetFullPath(opts.DataDirectory);
            byte[] source = encoding.GetBytes(s);
            byte[] inArray = System.Security.Cryptography.SHA256.HashData(source);
            var rootIdentity = Convert.ToHexStringLower(inArray);
            var scheduleLoader = loader;
            string path1 = opts.CacheDirectory ?? opts.DataDirectory;
            string path2 =
                $"{rootIdentity}_schedule_{studyYear.StudyYear!.Value.Value}_{studyYear.Semester.AsOrdinal()}.json";
            string combine = Path.Combine(path1, path2);
            scheduleLoader.CachedPath = combine;
        }

        {
            TruePath.LocalPath path3 = new(opts.DataDirectory);
            var discoverDirectories =
                ScheduleDirectoryDiscovery.DiscoverDirectories(path: path3);
            Func<ScheduleDirectoryDescriptor,
                StudyYear> keySelector = x => x.StudyYear;
            var
                orderBy = discoverDirectories.OrderBy(keySelector);
            Func<ScheduleDirectoryDescriptor,
                Semester> keySelector2 = x => x.Semester;
            var
                thenBy = orderBy.ThenBy(keySelector2);
            Func<ScheduleDirectoryDescriptor,
                AttendanceMode> keySelector3 = x => x.AttendanceMode;
            var scheduleDirs = thenBy.ThenBy(keySelector3);
            var matchingDirs = scheduleDirs.MatchingStudyYear(studyYear)
                .ToArray();
            if (matchingDirs.Length == 0)
            {
                throw new ScheduleSourcesNotFoundException(studyYear.StudyYear,
                    studyYear.Semester,
                    opts.DataDirectory);
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

        var context = ActivatorUtilities.CreateInstance<ScheduleImportContext>(sp, builder);

        // Code-defined parser/remapping changes invalidate the cache too. Runtime
        // initialization options are included independently of teacher/profile identity.
        string CalculateSerialize()
        {
            string serialize = System.Text.Json.JsonSerializer.Serialize(builder.Remappings.SubGroupNameRemappings);
            return serialize;
        }

        string CalculateSerialize2()
        {
            string serialize2 = System.Text.Json.JsonSerializer.Serialize(builder.Remappings.GroupNameRemappings);
            return serialize2;
        }

        loader.ConfigurationIdentity = string.Join("|",
            "cache-v2",
            typeof(ScheduleBuilderInitializer).Module.ModuleVersionId,
            typeof(ScheduleBuilder).Module.ModuleVersionId,
            _configureRemappings.Method.Module.ModuleVersionId,
            studyYear.StudyYear,
            studyYear.Semester,
            opts.EnrichWithFullNames,
            opts.LoadConsultations,
            System.Text.Json.JsonSerializer.Serialize(context.TimeConfig,
                new System.Text.Json.JsonSerializerOptions
                {
                    IncludeFields = true,
                }),
            CalculateSerialize(),
            CalculateSerialize2(),
            string.Join(";",
                builder.Remappings.TeacherLastNameRemappings.OrderBy(x => x.Key.ToString())
                    .Select(x => $"{x.Key}={x.Value}")));
        await loader.Load(
            context,
            cancellationToken,
            bypassCache: opts.BypassCache);

        _logger.LogInformation("Schedule built");
    }
}
