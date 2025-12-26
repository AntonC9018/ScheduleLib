using AutoConstructor.Attributes;
using CsvHelper;
using Anton.LayeredConfig.Retrieval;
using MainCli.Topics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;

namespace MainCli.BuilderNew.Impl;

[AutoConstructor]
public sealed partial class ManifestDirectoryTeacherSource : ILessonTopicSource
{
    private readonly List<string> _manifestDirectories;
    private readonly LookupFacade _lookup;
    private readonly Name _teacherName;
    private readonly ILogger _logger;

    public async ValueTask Configure(
        AllLessonTopicsDatabaseBuilder builder,
        CancellationToken cancellationToken)
    {
        foreach (var dir in _manifestDirectories)
        {
            await ProcessDirectory(dir, builder, cancellationToken);
        }
    }

    private async ValueTask ProcessDirectory(
        string manifestDirector,
        AllLessonTopicsDatabaseBuilder builder,
        CancellationToken cancellationToken)
    {
        var manifests = Directory.GetFiles(
            manifestDirector,
            searchOption: SearchOption.AllDirectories,
            searchPattern: "*.json");
        foreach (var manifestPath in manifests)
        {
            Manifest manifest;
            try
            {
                using var stream = new FileStream(manifestPath, FileMode.Open, FileAccess.Read);
                manifest = await ManifestSerializer.Deserialize(stream, cancellationToken);
            }
            catch (CsvHelperException e)
            {
                _logger.LogError(e, "Error while reading manifest {ManifestPath}", manifestPath);
                continue;
            }

            if (manifest.Teacher is null)
            {
                continue;
            }
            if (!manifest.Teacher.Equals(_teacherName))
            {
                continue;
            }

            var manifestDir = Path.GetDirectoryName(manifestPath);
            // Might want another layer here that just gathers the manifests.
            await builder.AddFromManifest(
                new(manifest, manifestDir),
                _lookup,
                cancellationToken);
        }
    }
}

public sealed class ManifestSource : ILessonTopicSource
{
    private readonly ManifestFileSource _fileSource;
    private readonly LookupFacade _lookup;

    public ManifestSource(
        ManifestFileSource fileSource,
        LookupFacade lookup)
    {
        _fileSource = fileSource;
        _lookup = lookup;
    }

    public async ValueTask Configure(
        AllLessonTopicsDatabaseBuilder builder,
        CancellationToken cancellationToken)
    {
        var manifest = await _fileSource.Read(cancellationToken);
        await builder.AddFromManifest(manifest, _lookup, cancellationToken);
    }
}

public sealed class ManifestSourceBuilder
{
    private readonly ManifestLessonTopicSourceDefinition Definition = new();

    public ManifestSource Create()
    {
        return null!;
    }
}

public static class ManifestFileSourceHelper
{
    extension(IServiceProvider sp)
    {
        public ManifestFileSource CreateManifestFileSource(
            string path,
            Name teacherName)
        {
            return ActivatorUtilities.CreateInstance<ManifestFileSource>(sp, [
                path,
                teacherName,
            ]);
        }

        public ManifestSource CreateManifestSource(IManifestFileSource s)
        {
            return ActivatorUtilities.CreateInstance<ManifestSource>(sp, [ s ]);
        }

        public ManifestDirectoryTeacherSource CreateManifestDirectoryTeacherSource(
            Name teacherName,
            List<string> directories)
        {
            return ActivatorUtilities.CreateInstance<ManifestDirectoryTeacherSource>(sp, [
                teacherName,
                directories,
            ]);
        }
    }
}

public interface IManifestFileSource
{
    Task<ManifestAtLocation> Read(CancellationToken cancellationToken);
}

[AutoConstructor]
public sealed partial class ManifestFileSource : IManifestFileSource
{
    private readonly ILogger _logger;
    private readonly string _path;
    private readonly Name? _teacherName;

    public async Task<ManifestAtLocation> Read(CancellationToken cancellationToken)
    {
        await using var inputFile = File.OpenRead(_path);
        var manifest = await ManifestSerializer.Deserialize(
            inputFile,
            cancellationToken);

        if (_teacherName != null)
        {
            if (manifest.Teacher is null)
            {
                manifest.Teacher = _teacherName;
            }
            else if (!manifest.Teacher.Equals(_teacherName))
            {
                TeacherNameMismatchLog(_teacherName, manifest.Teacher);
            }
        }
        return new(manifest, Path.GetDirectoryName(_path));
    }

    [LoggerMessage(LogLevel.Warning, "Teacher name in the specified file doesn't match the expected one: {Expected}, {Found}")]
    partial void TeacherNameMismatchLog(Name Expected, Name Found);
}

public readonly record struct ManifestAtLocation(
    Manifest Manifest,
    string? DirectoryPath);

// TODO: Separate this from the runtime factory.
public sealed class ManifestLessonTopicSourceDefinition : LessonTopicSourceDefinition
{
    public string? Path { get; set; }

    public ILessonTopicSource Create(IServiceProvider sp)
    {
        var configProvider = sp.GetRequiredService<ConfigProvider>();
        var teacherName = configProvider.Get(TeacherLayerConfig.Key).TeacherName;
        if (Path == null)
        {
            var manifestDirectoriesConfig = sp.GetRequiredService<IOptions<ManifestDirectoriesOptions>>().Value;
            var ret = sp.CreateManifestDirectoryTeacherSource(teacherName, manifestDirectoriesConfig.Directories);
            return ret;
        }
        else
        {
            var fileSource = sp.CreateManifestFileSource(Path, teacherName);
            var ret = sp.CreateManifestSource(fileSource);
            return ret;
        }
    }
}

public sealed class ManifestDirectoriesOptions
{
    public List<string> Directories { get; set; } = new();
}
