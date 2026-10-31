using System.Collections.Immutable;
using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

namespace ScheduleLib.Cli;

public sealed class SettingsArguments : IArgumentModel
{
    [Option(
        "project",
        Description = "Existing project directory; otherwise discover the nearest schedulelib.json upward.")]
    public string? Project { get; set; }

    [Option("profile", Description = "Code-defined teacher identity; user/project settings overlay this teacher.")]
    public string? Profile { get; set; }
}

public sealed class ConfigOverrideArguments : IArgumentModel
{
    [Option(
        "calendar-name",
        Description = "Inspect a typed GoogleCalendarConfig calendar-name override for this invocation.")]
    public string? CalendarName { get; set; }
}

public sealed record SettingsSource(string Name, string? File, IReadOnlyList<string> Keys);

public sealed record SettingsValue(JsonNode? Value, IReadOnlyList<SettingsSource> Sources);

/// <summary>Owns the registered layer services and the actual path consumed by runtime DataProvider.</summary>
public sealed class ResolvedSettings : IDisposable, IMarkerDataHelper
{
    private readonly ServiceProvider _provider;
    private readonly JsonSerializerOptions _json;
    private readonly IReadOnlyDictionary<string, NodePath> _profilePaths;
    private readonly NodePath _globalPath;
    internal JsonSerializerOptions Json => _json;
    internal IServiceProvider Services => _provider;

    public TreeBuilder Tree { get; }

    public NodePath Path { get; }

    public string? ProjectDirectory { get; }

    public string? Profile { get; }

    public IReadOnlyList<string> Profiles { get; }

    public IReadOnlyList<SettingsSource> Sources { get; }

    internal ResolvedSettings(
        ServiceProvider provider,
        TreeBuilder tree,
        NodePath path,
        JsonSerializerOptions json,
        string? projectDirectory,
        string? profile,
        IReadOnlyList<string> profiles,
        IReadOnlyList<SettingsSource> sources,
        IReadOnlyDictionary<string, NodePath> profilePaths,
        NodePath globalPath)
    {
        _provider = provider;
        Tree = tree;
        Path = path;
        _json = json;
        ProjectDirectory = projectDirectory;
        Profile = profile;
        Profiles = profiles;
        Sources = sources;
        _profilePaths = profilePaths;
        _globalPath = globalPath;
    }

    public T? Get<T>(NodeDataKey<T> key) where T : class => Path.ConstructValue(key, _provider);
    public T? GetGlobal<T>(NodeDataKey<T> key) where T : class => _globalPath.ConstructValue(key, _provider);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Tree);
        services.AddSingleton<IMarkerDataHelper>(this);
        services.Configure<ScheduleDerivationOptions>(options =>
            options.AutoMergeLabSubGroups = GetGlobal(ScheduleDerivationConfig.Key)?.AutoMergeLabSubGroups ?? false);
        if (Profile is { } teacher)
        {
            services.AddScoped(_ => new TeacherLayerConfig
            {
                TeacherName = NameHelper.Parse(teacher),
            });
        }
    }

    MarkerData IMarkerDataHelper.GetMarkerData(IServiceProvider sp) => new(sp.GetRequiredService<TeacherLayerConfig>());

    NodePath? IMarkerDataHelper.GetCurrentPath(MarkerData data)
    {
        return data.Value is TeacherLayerConfig { TeacherName: { } name }
            ? _profilePaths.GetValueOrDefault(name.ToString())
            : Path;
    }

    public IReadOnlyDictionary<string, SettingsValue> Inspect()
    {
        var values = new SortedDictionary<string, SettingsValue>(StringComparer.Ordinal);
        foreach (var key in Path.Path.SelectMany(x => x.ConfigKeys)
                     .Distinct()
                     .Where(x => x != TeacherLayerConfig.Key))
        {
            var type = NodeDataKey.Registry.GetTypeFromKey(key);
            var value = typeof(ResolvedSettings).GetMethod(
                    nameof(GetNode),
                    BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(type)
                .Invoke(this, [key]);
            values.Add(key.Value, new((JsonNode?) value, ContributingSources(key.Value)));
        }
        return values;
    }

    public SettingsValue Inspect(string key)
    {
        var parts = key.Split('.');
        if (!Inspect()
                .TryGetValue(parts[0], out var block))
        {
            throw new UnknownSettingsKeyException(key);
        }

        var value = block.Value;
        foreach (var part in parts.Skip(1))
        {
            if (value is not JsonObject obj || !obj.TryGetPropertyValue(part, out value))
            {
                throw new UnknownSettingsKeyException(key);
            }
        }
        return new(value?.DeepClone(), ContributingSources(key));
    }

    private IReadOnlyList<SettingsSource> ContributingSources(string key)
    {
        return Sources
            .Select(source => source with
            {
                Keys = source.Keys.Where(x =>
                    {
                        if (x == key)
                        {
                            return true;
                        }

                        if (x.StartsWith($"{key}.", StringComparison.Ordinal))
                        {
                            return true;
                        }

                        return key.StartsWith($"{x}.", StringComparison.Ordinal);
                    })
                    .Order()
                    .ToArray(),
            })
            .Where(source => source.Keys.Count != 0)
            .ToArray();
    }

    private JsonNode? GetNode<T>(NodeDataKey key) where T : class
    {
        var value = Get(new NodeDataKey<T>(key));
        var node = JsonSerializer.SerializeToNode(value, _json);
        return CliSettings.Redact(node);
    }

    public void Validate()
    {
        foreach (var path in _profilePaths.Values.Prepend(Path))
        {
            foreach (var key in path.Path.SelectMany(x => x.ConfigKeys)
                         .Distinct()
                         .Where(x => x != TeacherLayerConfig.Key))
            {
                try
                {
                    typeof(ResolvedSettings).GetMethod(
                            nameof(ValidateBlock),
                            BindingFlags.NonPublic | BindingFlags.Instance)!
                        .MakeGenericMethod(NodeDataKey.Registry.GetTypeFromKey(key))
                        .Invoke(this, [path, key]);
                }
                catch (TargetInvocationException e) when (e.InnerException is { } error)
                {
                    throw new SettingsBlockResolutionException(blockKey: key.Value, resolutionMessage: error.Message);
                }
            }
        }
    }

    private void ValidateBlock<T>(NodePath path, NodeDataKey key) where T : class =>
        _ = path.ConstructValue(new NodeDataKey<T>(key), _provider);

    public void Dispose() => _provider.Dispose();

    internal sealed class UnknownSettingsKeyException : JsonException
    {
        internal UnknownSettingsKeyException(string key)
            : base($"Unknown setting key: {key}")
            => Key = key;

        public string Key { get; }
    }

    internal sealed class SettingsBlockResolutionException : JsonException
    {
        internal SettingsBlockResolutionException(string blockKey, string resolutionMessage)
            : base($"Cannot resolve settings block '{blockKey}': {resolutionMessage}")
            => (BlockKey, ResolutionMessage) = (blockKey, resolutionMessage);

        public string BlockKey { get; }

        public string ResolutionMessage { get; }
    }
}

public static partial class CliSettings
{
    public const string FileName = "schedulelib.json";

    private static readonly HashSet<string> SupportedBlocks = new(StringComparer.Ordinal)
    {
        "GoogleCalendarConfig",
        "GoogleDriveConfig",
        "MoodleConfig",
        "RegistryConfig",
        "RegistryLessonFilterConfig",
        "DeadlinesExcelConfig",
        "LessonAttendanceConfig",
        "LessonTopicsConfig",
        "LabTasksDatabaseConfig",
        "ScheduleDerivationConfig",
    };

    public static string UserFile
    {
        get
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ScheduleLib",
                FileName);
        }
    }

    public static async Task<ResolvedSettings> Load(
        SettingsArguments arguments,
        string? invocationDirectory = null,
        string? userFile = null,
        CancellationToken cancellationToken = default,
        ConfigOverrideArguments? overrides = null)
    {
        return await LoadCore(
            arguments,
            invocationDirectory: invocationDirectory,
            userFile: userFile,
            cancellationToken,
            overrides);
    }

    private static async Task<ResolvedSettings> LoadCore(
        SettingsArguments arguments,
        string? invocationDirectory,
        string? userFile,
        CancellationToken cancellationToken,
        ConfigOverrideArguments? overrides = null,
        (string Path, JsonObject Envelope)? replacement = null)
    {
        var cwd = Path.GetFullPath(invocationDirectory ?? Environment.CurrentDirectory);
        var project = DiscoverProject(explicitProject: arguments.Project, invocationDirectory: cwd);
        var services = new ServiceCollection();
        // Registration only: never initialize schedules, credential resolvers, provider clients, or user secrets.
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        var provider = services.BuildServiceProvider();
        try
        {
            var tree = provider.GetRequiredService<TreeBuilder>();
            tree.AddDefaultConfig();
            var codeProfiles = tree.GetMarkerNodes()
                .ToDictionary(
                    keySelector: x => x.Config.TeacherName.ToString(),
                    elementSelector: x => x.Builder.Node,
                    StringComparer.Ordinal);
            if (arguments.Profile is { } teacher && !codeProfiles.ContainsKey(teacher))
            {
                throw new UnknownCliTeacherProfileException(teacher);
            }

            var json =
                new JsonSerializerOptions(
                    provider.GetRequiredService<IOptionsMonitor<JsonSerializerOptions>>()
                        .Get(TreeSerializer.ServiceKey))
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                    NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                    WriteIndented = true,
                };
            json.Converters.Insert(0, new ConfigColorConverter());
            json.Converters.Insert(0, new JsonStringEnumConverter(allowIntegerValues: false));
            var user = await ReadFile(
                userFile ?? UserFile,
                json,
                codeProfiles.Keys,
                cancellationToken,
                replacement);
            var local = project is null
                ? null
                : await ReadFile(
                    Path.Combine(project, FileName),
                    json,
                    codeProfiles.Keys,
                    cancellationToken,
                    replacement);
            var defaultSources = new List<SettingsSource>
            {
                CodeSource(
                    "code defaults",
                    tree.BaseNode,
                    json),
            };
            var defaultsPath = ImmutableArray.CreateBuilder<MutableNode>();
            defaultsPath.Add(tree.BaseNode);
            AddOverlay(
                defaultsPath,
                defaultSources,
                user,
                "user defaults",
                user?.Defaults);
            AddOverlay(
                defaultsPath,
                defaultSources,
                local,
                "project settings",
                local?.Defaults);
            var profilePaths = new Dictionary<string, NodePath>(StringComparer.Ordinal);
            var selectedPath = defaultsPath.ToImmutable();
            var selectedSources = defaultSources;
            foreach (var coded in codeProfiles)
            {
                var path = defaultsPath.ToImmutable()
                    .ToBuilder();
                var sources = new List<SettingsSource>(defaultSources);
                path.Add(coded.Value);
                sources.Add(CodeSource($"code profile: {coded.Key}", coded.Value, json));
                AddOverlay(
                    path,
                    sources,
                    user,
                    name: $"user profile: {coded.Key}",
                    user?.Profiles.GetValueOrDefault(coded.Key),
                    profile: coded.Key);
                AddOverlay(
                    path,
                    sources,
                    local,
                    name: $"project profile: {coded.Key}",
                    local?.Profiles.GetValueOrDefault(coded.Key),
                    profile: coded.Key);
                AddCli(path, sources);
                profilePaths.Add(coded.Key, new(path.ToImmutable()));
                if (arguments.Profile == coded.Key)
                {
                    selectedPath = path.ToImmutable();
                    selectedSources = sources;
                }
            }
            if (arguments.Profile is null)
            {
                var path = selectedPath.ToBuilder();
                AddCli(path, selectedSources);
                selectedPath = path.ToImmutable();
            }
            var settingsPath = new NodePath(selectedPath);
            var globalPath = new NodePath(defaultsPath.ToImmutable());
            var resolved = new ResolvedSettings(
                provider,
                tree,
                path: settingsPath,
                json,
                projectDirectory: project,
                profile: arguments.Profile,
                codeProfiles.Keys.Order()
                    .ToArray(),
                selectedSources,
                profilePaths,
                globalPath: globalPath);
            resolved.Validate();
            return resolved;

            void AddOverlay(
                ImmutableArray<MutableNode>.Builder path,
                List<SettingsSource> sources,
                SettingsFile? file,
                string name,
                JsonObject? values,
                string? profile = null)
            {
                if (file is null)
                {
                    return;
                }

                var actions = GetOperations(file.Operations, profile);

                if (values is null && actions is null)
                {
                    return;
                }

                var node = new MutableNode();
                path.Add(node);
                foreach (var setting in values ?? new JsonObject())
                {
                    var key = new NodeDataKey(setting.Key);
                    var type = NodeDataKey.Registry.GetTypeFromKey(key);
                    var value = setting.Value?.Deserialize(type, json);
                    if (value is not null)
                    {
                        SetValue(
                            node,
                            key,
                            value);
                    }
                }
                if (actions is not null)
                {
                    foreach (var action in actions.Cast<JsonObject>())
                    {
                        AddOperation(
                            node,
                            action,
                            provider,
                            json);
                    }
                }

                var leafKeys = LeafKeys(values);
                var actionKeys = actions?.Cast<JsonObject>()
                        .Select(x => x["key"]!.GetValue<string>())
                    ?? [];
                sources.Add(new(
                    Name: name,
                    File: file.Path,
                    leafKeys.Concat(actionKeys)
                        .ToArray()));
            }

            void AddCli(ImmutableArray<MutableNode>.Builder path, List<SettingsSource> sources)
            {
                if (overrides?.CalendarName is not { } calendar)
                {
                    return;
                }

                var node = new MutableNode();
                new NodeBuilder(node, provider).GoogleCalendar()
                    .ConfigureValue(x => x.CalendarName = calendar);
                path.Add(node);
                sources.Add(new(
                    Name: "CLI arguments",
                    File: null,
                    ["GoogleCalendarConfig.calendarName"]));
            }
        }
        catch
        {
            provider.Dispose();
            throw;
        }
    }

    public static string? DiscoverProject(string? explicitProject, string invocationDirectory)
    {
        if (explicitProject is { } selected)
        {
            string full;
            try
            {
                full = Path.GetFullPath(path: selected, basePath: invocationDirectory);
            }
            catch (ArgumentException)
            {
                throw new InvalidProjectDirectoryPathException(selected);
            }
            if (!Directory.Exists(full))
            {
                throw new ProjectDirectoryNotFoundException(full);
            }

            return full;
        }
        for (var directory = new DirectoryInfo(invocationDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            string settingsPath = Path.Combine(directory.FullName, FileName);
            if (File.Exists(settingsPath))
            {
                return directory.FullName;
            }
        }

        return null;
    }

    private sealed record SettingsFile(
        string Path,
        JsonObject? Defaults,
        Dictionary<string, JsonObject?> Profiles,
        JsonObject? Operations);

    private static async Task<SettingsFile?> ReadFile(
        string path,
        JsonSerializerOptions json,
        IEnumerable<string> teachers,
        CancellationToken cancellationToken,
        (string Path, JsonObject Envelope)? replacement = null)
    {
        if (replacement is { } candidate && Path.GetFullPath(path) == candidate.Path)
        {
            return ReadEnvelope(
                (JsonObject) candidate.Envelope.DeepClone(),
                path,
                json,
                teachers);
        }

        if (!File.Exists(path))
        {
            return null;
        }

        await using var input = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read | FileShare.Delete,
            bufferSize: 4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        JsonNode? node;
        try
        {
            using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken);
            CheckDuplicates(document.RootElement, path);
            node = JsonNode.Parse(document.RootElement.GetRawText());
        }
        catch (JsonException e)
        {
            throw new InvalidSettingsJsonException(settingsPath: path, parserMessage: e.Message);
        }
        return ReadEnvelope(
            node,
            path,
            json,
            teachers);
    }

    private static SettingsFile ReadEnvelope(
        JsonNode? node,
        string path,
        JsonSerializerOptions json,
        IEnumerable<string> teachers)
    {
        if (node is not JsonObject envelope)
        {
            throw new ExpectedSettingsObjectException(path);
        }

        CheckKeys(
            envelope,
            ["schemaVersion", "defaults", "profiles", "operations"],
            path);
        if (envelope["schemaVersion"] is not JsonValue version
            || !version.TryGetValue<int>(out var schema)
            || schema != 1)
        {
            throw new UnsupportedSettingsSchemaVersionException(path);
        }

        var defaults = ReadBlock(envelope["defaults"], "defaults");
        var profiles = new Dictionary<string, JsonObject?>(StringComparer.Ordinal);
        if (envelope["profiles"] is { } profileNode)
        {
            if (profileNode is not JsonObject map)
            {
                throw new SettingsObjectRequiredException(location: "profiles", settingsPath: path);
            }

            foreach (var entry in map)
            {
                if (!teachers.Contains(entry.Key))
                {
                    throw new UnknownSettingsProfileException(profile: entry.Key, settingsPath: path);
                }

                profiles.Add(entry.Key, ReadBlock(entry.Value, $"profiles.{entry.Key}"));
            }
        }
        var operations = ReadOperations(
            envelope["operations"],
            path,
            json,
            teachers);
        return new(
            Path.GetFullPath(path),
            Defaults: defaults,
            profiles,
            Operations: operations);

        JsonObject? ReadBlock(JsonNode? block, string location)
        {
            if (block is null)
            {
                return null;
            }

            if (block is not JsonObject values)
            {
                throw new SettingsObjectRequiredException(location: location, settingsPath: path);
            }

            foreach (var setting in values)
            {
                if (location.StartsWith("profiles.", StringComparison.Ordinal)
                    && setting.Key == "ScheduleDerivationConfig")
                {
                    throw new JsonException("ScheduleDerivationConfig is global and cannot be configured per profile.");
                }

                var key = new NodeDataKey(setting.Key);
                var type = NodeDataKey.Registry.TryGetTypeFromKey(key);
                if (type is null || !SupportedBlocks.Contains(setting.Key))
                {
                    throw new UnsupportedSettingsBlockException(blockKey: setting.Key, settingsPath: path);
                }

                RejectSecrets(setting.Value, path);
                NormalizeInheritance(
                    setting.Value,
                    type,
                    json,
                    path);

                ResolvePaths(setting.Value, Path.GetDirectoryName(Path.GetFullPath(path))!);
                // Separate CLI options; the desktop serializer's format and options remain unchanged.
                try
                {
                    _ = setting.Value?.Deserialize(type, json);
                }
                catch (Exception e) when (e is JsonException
                                              or ArgumentException
                                              or NotSupportedException
                                              or InvalidOperationException)
                {
                    throw new InvalidSettingsBlockValueException(
                        location: location,
                        settingKey: setting.Key,
                        settingsPath: path,
                        errorMessage: e.Message);
                }
            }
            return values;
        }
    }

    private static void SetValue(
        MutableNode node,
        NodeDataKey key,
        object value)
    {
        typeof(CliSettings)
            .GetMethod(nameof(SetTypedValue), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(value.GetType())
            .Invoke(null, [node, key, value]);
    }

    private static void SetTypedValue<T>(
        MutableNode node,
        NodeDataKey key,
        object value) where T : class
    {
        var typed = node.GetOrAdd(new NodeDataKey<T>(key));
        typed.SetValue((T) value);
    }

    private static void CheckDuplicates(JsonElement value, string file)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name))
                {
                    throw new DuplicateSettingsKeyException(key: property.Name, settingsPath: file);
                }

                CheckDuplicates(property.Value, file);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                CheckDuplicates(item, file);
            }
        }
    }

    private static void CheckKeys(
        JsonObject value,
        string[] allowed,
        string path)
    {
        foreach (var key in value.Select(x => x.Key))
        {
            if (!allowed.Contains(key))
            {
                throw new UnsupportedSettingsKeyInFileException(key: key, settingsPath: path);
            }
        }
    }

    private static void RejectSecrets(JsonNode? node, string file)
    {
        if (node is JsonObject obj)
        {
            foreach (var entry in obj)
            {
                if (IsSecret(entry.Key)
                    || (entry.Key == "$type"
                        && entry.Value?.ToString() is "ValueCredentialsSource" or "ManualGoogleApiKeysSource"))
                {
                    throw new InlineSettingsSecretsUnsupportedException(file);
                }

                RejectSecrets(entry.Value, file);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                RejectSecrets(item, file);
            }
        }
    }

    private static void NormalizeInheritance(
        JsonNode? node,
        Type type,
        JsonSerializerOptions json,
        string file)
    {
        if (node is JsonArray array && type.IsGenericType)
        {
            foreach (var item in array)
            {
                if (item is null)
                {
                    throw new NullSettingsCollectionEntryException(file);
                }

                NormalizeInheritance(item, type.GetGenericArguments()[0], json, file);
            }
            return;
        }
        if (node is not JsonObject obj)
        {
            return;
        }

        var info = json.GetTypeInfo(type);
        if (info.PolymorphismOptions is { } polymorphism && obj["$type"] is { } discriminator)
        {
            var derived =
                polymorphism.DerivedTypes.FirstOrDefault(x =>
                    x.TypeDiscriminator?.ToString() == discriminator.ToString());
            if (derived.DerivedType is not null)
            {
                info = json.GetTypeInfo(derived.DerivedType);
            }
        }
        if (info.Kind != JsonTypeInfoKind.Object)
        {
            return; // Custom converters validate their own representation.
        }

        foreach (var entry in obj.ToArray())
        {
            if (entry.Key == "$type")
            {
                continue;
            }

            var property = info.Properties.FirstOrDefault(x => x.Name == entry.Key);
            if (property is null)
            {
                throw new UnsupportedSettingsMemberException(memberName: entry.Key, settingsPath: file);
            }

            if (entry.Value is null)
            {
                obj.Remove(entry.Key);
            }
            else
            {
                NormalizeInheritance(
                    entry.Value,
                    property.PropertyType,
                    json,
                    file);
            }
        }
    }

    private static void ResolvePaths(JsonNode? node, string directory)
    {
        if (node is JsonObject obj)
        {
            foreach (var entry in obj.ToArray())
            {
                if (entry.Key is "credentialsPath" or "filePath" or "path"
                    && entry.Value is JsonValue value
                    && value.TryGetValue<string>(out var path))
                {
                    try
                    {
                        obj[entry.Key] = Path.GetFullPath(path: path, basePath: directory);
                    }
                    catch (ArgumentException)
                    {
                        throw new InvalidSettingsPathMemberException(entry.Key);
                    }
                }
                else
                {
                    ResolvePaths(entry.Value, directory);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                ResolvePaths(item, directory);
            }
        }
    }

    private static SettingsSource CodeSource(
        string name,
        MutableNode node,
        JsonSerializerOptions json)
    {
        var keys = new List<string>();
        foreach (var config in node.Configs.Where(x => x.Key != TeacherLayerConfig.Key))
        {
            var value = config.Container.GetValue();
            if (value is null)
            {
                keys.Add(config.Key.Value); // Includes code-defined block removals.
            }
            else
            {
                var serialized = JsonSerializer.SerializeToNode(
                    value,
                    value.GetType(),
                    json);
                var leaves = LeafKeys(serialized, config.Key.Value).ToArray();
                keys.AddRange(leaves.Length == 0 ? [config.Key.Value] : leaves);
            }
        }
        return new(
            Name: name,
            File: null,
            keys);
    }

    private static IEnumerable<string> LeafKeys(JsonNode? node, string prefix = "")
    {
        if (node is null)
        {
            yield break;
        }

        if (node is JsonObject obj)
        {
            foreach (var entry in obj)
            {
                var childPrefix = prefix.Length == 0 ? entry.Key : $"{prefix}.{entry.Key}";
                foreach (var key in LeafKeys(entry.Value, childPrefix))
                {
                    yield return key;
                }
            }
        }
        else
        {
            yield return prefix;
        }
    }

    private static bool IsSecret(string key)
    {
        if (key.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (key.Contains("secret", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (key.Contains("token", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (key.Equals("apiKey", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (key.Equals("accessKey", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    internal static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            foreach (var entry in obj.ToArray())
            {
                if (IsSecret(entry.Key))
                {
                    obj[entry.Key] = "[redacted]";
                }
                else
                {
                    Redact(entry.Value);
                }
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                Redact(item);
            }
        }

        return node;
    }

    internal sealed class UnknownCliTeacherProfileException : JsonException
    {
        internal UnknownCliTeacherProfileException(string profile)
            : base($"Unknown teacher profile '{profile}'. Use config profiles to list supported teachers.")
            => Profile = profile;

        public string Profile { get; }
    }

    internal sealed class InvalidProjectDirectoryPathException : JsonException
    {
        internal InvalidProjectDirectoryPathException(string projectPath)
            : base("Invalid project directory path.")
            => ProjectPath = projectPath;

        public string ProjectPath { get; }
    }

    internal sealed class ProjectDirectoryNotFoundException : DirectoryNotFoundException
    {
        internal ProjectDirectoryNotFoundException(string projectPath)
            : base($"Project directory does not exist: {projectPath}")
            => ProjectPath = projectPath;

        public string ProjectPath { get; }
    }

    internal sealed class InvalidSettingsJsonException : JsonException
    {
        internal InvalidSettingsJsonException(string settingsPath, string parserMessage)
            : base($"Invalid settings JSON in {settingsPath}: {parserMessage}")
            => (SettingsPath, ParserMessage) = (settingsPath, parserMessage);

        public string SettingsPath { get; }

        public string ParserMessage { get; }
    }

    internal sealed class ExpectedSettingsObjectException : JsonException
    {
        internal ExpectedSettingsObjectException(string settingsPath)
            : base($"Expected settings object in {settingsPath}.")
            => SettingsPath = settingsPath;

        public string SettingsPath { get; }
    }

    internal sealed class UnsupportedSettingsSchemaVersionException : JsonException
    {
        internal UnsupportedSettingsSchemaVersionException(string settingsPath)
            : base($"Unsupported or missing schemaVersion in {settingsPath}; expected 1.")
            => SettingsPath = settingsPath;

        public string SettingsPath { get; }
    }

    internal sealed class SettingsObjectRequiredException : JsonException
    {
        internal SettingsObjectRequiredException(string location, string settingsPath)
            : base($"{location} must be an object in {settingsPath}.")
            => (Location, SettingsPath) = (location, settingsPath);

        public string Location { get; }

        public string SettingsPath { get; }
    }

    internal sealed class UnknownSettingsProfileException : JsonException
    {
        internal UnknownSettingsProfileException(string profile, string settingsPath)
            : base($"Unknown teacher profile '{profile}' in {settingsPath}.")
            => (Profile, SettingsPath) = (profile, settingsPath);

        public string Profile { get; }

        public string SettingsPath { get; }
    }


    internal sealed class UnsupportedSettingsBlockException : JsonException
    {
        internal UnsupportedSettingsBlockException(string blockKey, string settingsPath)
            : base($"Unsupported settings block '{blockKey}' in {settingsPath}.")
            => (BlockKey, SettingsPath) = (blockKey, settingsPath);

        public string BlockKey { get; }

        public string SettingsPath { get; }
    }

    internal sealed class InvalidSettingsBlockValueException : JsonException
    {
        internal InvalidSettingsBlockValueException(
            string location,
            string settingKey,
            string settingsPath,
            string errorMessage)
            : base($"Invalid {location}.{settingKey} in {settingsPath}: {errorMessage}")
        {
            (Location, SettingKey, SettingsPath, ErrorMessage) =
                (location, settingKey, settingsPath, errorMessage);
        }

        public string Location { get; }

        public string SettingKey { get; }

        public string SettingsPath { get; }

        public string ErrorMessage { get; }
    }

    internal sealed class DuplicateSettingsKeyException : JsonException
    {
        internal DuplicateSettingsKeyException(string key, string settingsPath)
            : base($"Duplicate settings key '{key}' in {settingsPath}.")
            => (Key, SettingsPath) = (key, settingsPath);

        public string Key { get; }

        public string SettingsPath { get; }
    }

    internal sealed class UnsupportedSettingsKeyInFileException : JsonException
    {
        internal UnsupportedSettingsKeyInFileException(string key, string settingsPath)
            : base($"Unsupported settings key '{key}' in {settingsPath}.")
            => (Key, SettingsPath) = (key, settingsPath);

        public string Key { get; }

        public string SettingsPath { get; }
    }

    internal sealed class InlineSettingsSecretsUnsupportedException : JsonException
    {
        internal InlineSettingsSecretsUnsupportedException(string sourcePath)
            : base(
                $"Inline secrets are unsupported in settings file {sourcePath}; reference the existing credential configuration instead.")
            => SourcePath = sourcePath;

        public string SourcePath { get; }
    }

    internal sealed class NullSettingsCollectionEntryException : JsonException
    {
        internal NullSettingsCollectionEntryException(string settingsPath)
            : base($"Null collection entries are unsupported in {settingsPath}.")
            => SettingsPath = settingsPath;

        public string SettingsPath { get; }
    }

    internal sealed class UnsupportedSettingsMemberException : JsonException
    {
        internal UnsupportedSettingsMemberException(string memberName, string settingsPath)
            : base($"Unsupported settings member '{memberName}' in {settingsPath}.")
            => (MemberName, SettingsPath) = (memberName, settingsPath);

        public string MemberName { get; }

        public string SettingsPath { get; }
    }

    internal sealed class InvalidSettingsPathMemberException : JsonException
    {
        internal InvalidSettingsPathMemberException(string memberName)
            : base($"Invalid settings path for '{memberName}'.")
            => MemberName = memberName;

        public string MemberName { get; }
    }

    internal sealed class SettingsOperationsArrayRequiredException : JsonException
    {
        internal SettingsOperationsArrayRequiredException(string settingsPath)
            : base($"Operations must be an array in {settingsPath}.")
            => SettingsPath = settingsPath;

        public string SettingsPath { get; }
    }

    internal sealed class SettingsOperationObjectRequiredException : JsonException
    {
        internal SettingsOperationObjectRequiredException(string settingsPath)
            : base($"Expected operation object in {settingsPath}.")
            => SettingsPath = settingsPath;

        public string SettingsPath { get; }
    }

    internal sealed class UnsupportedSettingKeyException : JsonException
    {
        internal UnsupportedSettingKeyException(string key)
            : base($"Unsupported setting key: {key}")
            => Key = key;

        public string Key { get; }
    }

    internal sealed class UnsupportedOperationKeyException : JsonException
    {
        internal UnsupportedOperationKeyException(string key)
            : base($"Unsupported operation key: {key}")
            => Key = key;

        public string Key { get; }
    }

    internal sealed class SettingsOperationMembersRequiredException : JsonException
    {
        internal SettingsOperationMembersRequiredException(string memberName)
            : base("Operation requires string key and operation members.")
            => MemberName = memberName;

        public string MemberName { get; }
    }

    internal sealed class UnsupportedSettingsOperationException : JsonException
    {
        internal UnsupportedSettingsOperationException(string operation, string settingKey)
            : base(
                $"Unsupported {operation} operation for {settingKey}; remove accepts a block or collection --item, clear accepts a collection.")
            => (Operation, SettingKey) = (operation, settingKey);

        public string Operation { get; }

        public string SettingKey { get; }
    }


    internal sealed class InvalidTypedSettingsOperationItemException : JsonException
    {
        internal InvalidTypedSettingsOperationItemException(string settingKey, Exception innerException)
            : base(
                $"Invalid typed item for {settingKey}; include its registered identity and any required $type discriminator.",
                innerException)
            => SettingKey = settingKey;

        public string SettingKey { get; }
    }


    internal sealed class InvalidSettingsScopeException : ArgumentException
    {
        internal InvalidSettingsScopeException(SettingsScope scope)
            : base("Scope must be user or project.")
            => Scope = scope;

        public SettingsScope Scope { get; }
    }

    internal sealed class SettingsMemberIsNotObjectException : JsonException
    {
        internal SettingsMemberIsNotObjectException(string settingKey)
            : base($"Cannot edit member beneath a non-object setting: {settingKey}")
            => SettingKey = settingKey;

        public string SettingKey { get; }
    }
}

/// <summary>CLI-only color representation; desktop serialization is intentionally unchanged.</summary>
internal sealed class ConfigColorConverter : JsonConverter<Color>
{
    public override Color Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
        {
            throw new JsonException("Color must be a known color name or #AARRGGBB.");
        }

        var value = reader.GetString()!;
        if (value.Length == 9
            && value[0] == '#'
            && uint.TryParse(
                value.AsSpan(1),
                System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture,
                out var argb))
        {
            return Color.FromArgb(unchecked((int) argb));
        }

        var color = Color.FromName(value);
        if (!color.IsKnownColor)
        {
            throw new JsonException("Color must be a known color name or #AARRGGBB.");
        }

        return color;
    }

    public override void Write(
        Utf8JsonWriter writer,
        Color value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.IsKnownColor ? value.Name : $"#{unchecked((uint) value.ToArgb()):X8}");
}
