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
            var
                sortedDictionary = values;
            string key2 = key.Value;
            var value2 = (JsonNode?) value;
            var sources =
                ContributingSources(key.Value);
            SettingsValue value3 = new(value2, sources);
            sortedDictionary.Add(key2, value3);
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
            bool conditionResult;
            if (value is not JsonObject obj)
            {
                conditionResult = true;
            }
            else
            {
                conditionResult = obj.TryGetPropertyValue(part, out value);
                conditionResult = !conditionResult;
            }

            if (conditionResult)
            {
                throw new UnknownSettingsKeyException(key);
            }
        }
        var value2 = value?.DeepClone();
        var sources =
            ContributingSources(key);
        SettingsValue settingsValue = new(value2, sources);
        return settingsValue;
    }

    private IReadOnlyList<SettingsSource> ContributingSources(string key)
    {
        return Sources
            .Select(source => source with
            {
                Keys = source.Keys.Where(x =>
                    {
                        bool CheckCondition()
                        {
                            if (x == key)
                            {
                                return true;
                            }

                            string text = x;
                            string text2 = $"{key}.";
                            string value = text2;
                            var comparisonType = StringComparison.Ordinal;
                            if (text.StartsWith(value, comparisonType))
                            {
                                return true;
                            }

                            string text3 = key;
                            string text4 = $"{x}.";
                            string value2 = text4;
                            var comparisonType2 = StringComparison.Ordinal;
                            if (text3.StartsWith(value2, comparisonType2))
                            {
                                return true;
                            }

                            return false;
                        }

                        return CheckCondition();
                    })
                    .Order()
                    .ToArray(),
            })
            .Where(source => source.Keys.Count != 0)
            .ToArray();
    }

    private JsonNode? GetNode<T>(NodeDataKey key) where T : class
    {
        var key2 = new NodeDataKey<T>(key);
        var value = Get(key2);
        var node = JsonSerializer.SerializeToNode(value, _json);
        var redact = CliSettings.Redact(node);
        return redact;
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
                    Type CalculateTypeArguments()
                    {
                        var typeArguments = NodeDataKey.Registry.GetTypeFromKey(key);
                        return typeArguments;
                    }

                    typeof(ResolvedSettings).GetMethod(
                            nameof(ValidateBlock),
                            BindingFlags.NonPublic | BindingFlags.Instance)!
                        .MakeGenericMethod(CalculateTypeArguments())
                        .Invoke(this, [path, key]);
                }
                catch (TargetInvocationException e) when (e.InnerException is { } error)
                {
                    throw new SettingsBlockResolutionException(blockKey: key.Value, resolutionMessage: error.Message);
                }
            }
        }
    }

    private void ValidateBlock<T>(NodePath path, NodeDataKey key) where T : class
    {
        NodeDataKey<T> CalculateKey()
        {
            var key2 = new NodeDataKey<T>(key);
            return key2;
        }

        _ = path.ConstructValue(CalculateKey(), _provider);
    }

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
            string path1 = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string path2 = "ScheduleLib";
            string path3 = FileName;
            string combine = Path.Combine(
                path1,
                path2,
                path3);
            return combine;
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
            var iList =
                json.Converters;
            int index = 0;
            var item =
                new JsonStringEnumConverter(allowIntegerValues: false);
            JsonConverter item2 = item;
            iList.Insert(index, item2);
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
                var list = sources;
                string text = $"code profile: {coded.Key}";
                string name2 = text;
                var node2 = coded.Value;
                var json2 = json;
                var item3 = CodeSource(
                    name2,
                    node2,
                    json2);
                list.Add(
                    item3);

                string CalculateName()
                {
                    string text2 = $"user profile: {coded.Key}";
                    string name3 = text2;
                    return name3;
                }

                AddOverlay(
                    path,
                    sources,
                    user,
                    name: CalculateName(),
                    user?.Profiles.GetValueOrDefault(coded.Key),
                    profile: coded.Key);

                string CalculateName2()
                {
                    string text3 = $"project profile: {coded.Key}";
                    string name4 = text3;
                    return name4;
                }

                AddOverlay(
                    path,
                    sources,
                    local,
                    name: CalculateName2(),
                    local?.Profiles.GetValueOrDefault(coded.Key),
                    profile: coded.Key);
                AddCli(path, sources);
                var dictionary = profilePaths;
                string key2 = coded.Key;
                NodePath value2 = new(path.ToImmutable());
                dictionary.Add(key2, value2);
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
            var provider2 = provider;
            var tree2 = tree;
            NodePath path2 = new(selectedPath);
            var json3 = json;
            string? projectDirectory2 = project;
            string? profile2 = arguments.Profile;
            IReadOnlyList<string> profiles = codeProfiles.Keys.Order()
                .ToArray();
            IReadOnlyList<SettingsSource> sources2 =
                selectedSources;
            IReadOnlyDictionary<string,
                NodePath> profilePaths2 = profilePaths;
            NodePath globalPath2 = new(defaultsPath.ToImmutable());
            var resolved = new ResolvedSettings(
                provider2,
                tree2,
                path: path2,
                json3,
                projectDirectory: projectDirectory2,
                profile: profile2,
                profiles,
                sources2,
                profilePaths2,
                globalPath: globalPath2);
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

                bool CalculateConditionResult()
                {
                    if (!(values is null))
                    {
                        return false;
                    }

                    if (actions is null)
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                if (conditionResult)
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

                var list = sources;
                string name2 = name;
                string file2 = file.Path;
                var leafKeys = LeafKeys(values);
                var second = actions?.Cast<JsonObject>()
                        .Select(x => x["key"]!.GetValue<string>())
                    ?? [];
                var concat = leafKeys.Concat(
                    second);
                IReadOnlyList<string> keys = concat.ToArray();
                SettingsSource item = new(
                    Name: name2,
                    File: file2,
                    keys);
                list.Add(item);
            }

            void AddCli(ImmutableArray<MutableNode>.Builder path, List<SettingsSource> sources)
            {
                if (overrides?.CalendarName is not { } calendar)
                {
                    return;
                }

                var node = new MutableNode();
                new NodeBuilder(node, provider).GoogleCalendar()
                    .ConfigureValue(x =>
                    {
                        var googleCalendarConfig = x;
                        googleCalendarConfig.CalendarName = calendar;
                    });
                path.Add(node);
                var list = sources;
                SettingsSource item = new(
                    Name: "CLI arguments",
                    File: null,
                    ["GoogleCalendarConfig.calendarName"]);
                list.Add(item);
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
            string path2 = Path.Combine(directory.FullName, FileName);
            if (File.Exists(path2))
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
        bool conditionResult2;
        if (envelope["schemaVersion"] is not JsonValue version)
        {
            conditionResult2 = true;
        }
        else
        {
            bool conditionValue = version.TryGetValue<int>(out var schema);
            if (!conditionValue)
            {
                conditionResult2 = true;
            }
            else
            {
                conditionResult2 = schema != 1;
            }
        }

        if (conditionResult2)
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

                var
                    dictionary = profiles;
                string key2 = entry.Key;
                var block2 = entry.Value;
                string text = $"profiles.{entry.Key}";
                string location2 = text;
                var value = ReadBlock(block2, location2);
                dictionary.Add(key2, value);
            }
        }
        var operations = ReadOperations(
            envelope["operations"],
            path,
            json,
            teachers);
        string path2 = Path.GetFullPath(path);
        var defaults2 = defaults;
        var profiles2 =
            profiles;
        var operations2 = operations;
        SettingsFile settingsFile = new(
            path2,
            Defaults: defaults2,
            profiles2,
            Operations: operations2);
        return settingsFile;

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
                bool CalculateConditionResult()
                {
                    if (!location.StartsWith("profiles.", StringComparison.Ordinal))
                    {
                        return false;
                    }

                    if (setting.Key == "ScheduleDerivationConfig")
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                if (conditionResult)
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

                string CalculateDirectory()
                {
                    string path2 = Path.GetFullPath(path);
                    string directory = Path.GetDirectoryName(path2)!;
                    return directory;
                }

                ResolvePaths(setting.Value, CalculateDirectory());
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
                    throw new InvalidSettingsBlockValueException(location: location,
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
        var mutableNode = node;
        var key2 = new NodeDataKey<T>(key);
        var typed = mutableNode.GetOrAdd(key2);
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
                bool CalculateConditionResult()
                {
                    if (IsSecret(entry.Key))
                    {
                        return true;
                    }
                    if (entry.Key != "$type")
                    {
                        return false;
                    }

                    if (entry.Value?.ToString() is "ValueCredentialsSource" or "ManualGoogleApiKeysSource")
                    {
                        return true;
                    }

                    return false;
                }

                bool conditionResult = CalculateConditionResult();


                if (conditionResult)
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

                var node2 = item;
                var genericArguments = type.GetGenericArguments();
                int int32 = 0;
                var type2 = genericArguments[int32];
                var json2 = json;
                string file2 = file;
                NormalizeInheritance(
                    node2,
                    type2,
                    json2,
                    file2);
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
                var node2 = JsonSerializer.SerializeToNode(
                    value,
                    value.GetType(),
                    json);
                string prefix = config.Key.Value;
                var leafKeys = LeafKeys(
                    node2,
                    prefix);
                var leaves = leafKeys.ToArray();
                var list = keys;
                string[] collection;
                if (leaves.Length == 0)
                {
                    collection = [config.Key.Value];
                }
                else
                {
                    collection = leaves;
                }

                IEnumerable<string> collection2 = collection;
                list.AddRange(collection2);
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
                string CalculatePrefix()
                {
                    string prefix2;
                    if (prefix.Length == 0)
                    {
                        prefix2 = entry.Key;
                    }
                    else
                    {
                        string text = $"{prefix}.{entry.Key}";
                        prefix2 = text;
                    }

                    return prefix2;
                }

                foreach (var key in LeafKeys(entry.Value, CalculatePrefix()))
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
        if (value.Length == 9)
        {
            if (value[0] == '#')
            {
                if (uint.TryParse(
                        value.AsSpan(1),
                        System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var argb))
                {
                    return Color.FromArgb(unchecked((int) argb));
                }
            }
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
        JsonSerializerOptions options)
    {
        var utf8JsonWriter = writer;
        string value2;
        if (value.IsKnownColor)
        {
            value2 = value.Name;
        }
        else
        {
            string text = $"#{unchecked((uint) value.ToArgb()):X8}";
            value2 = text;
        }

        utf8JsonWriter.WriteStringValue(value2);
    }
}
