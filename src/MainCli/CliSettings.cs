using System.Collections.Immutable;
using System.Reflection;
using System.Drawing;
using System.Text.Json.Serialization.Metadata;
using ScheduleLib.Parsing;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Anton.LayeredData;
using Anton.LayeredData.Retrieval;
using Anton.LayeredData.TreeEnumeration;
using CommandDotNet;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core;
using ScheduleLib.OnlineRegistry;

namespace ScheduleLib.Cli;

public sealed class SettingsArguments : IArgumentModel
{
    [Option("project", Description = "Existing project directory; otherwise discover the nearest schedulelib.json upward.")]
    public string? Project { get; set; }
    [Option("profile", Description = "Code-defined teacher identity; user/project settings overlay this teacher.")]
    public string? Profile { get; set; }
}

public sealed class ConfigOverrideArguments : IArgumentModel
{
    [Option("calendar-name", Description = "Inspect a typed GoogleCalendarConfig calendar-name override for this invocation.")]
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
    internal JsonSerializerOptions Json => _json;
    public TreeBuilder Tree { get; }
    public NodePath Path { get; }
    public string? ProjectDirectory { get; }
    public string? Profile { get; }
    public IReadOnlyList<string> Profiles { get; }
    public IReadOnlyList<SettingsSource> Sources { get; }

    internal ResolvedSettings(ServiceProvider provider, TreeBuilder tree, NodePath path, JsonSerializerOptions json,
        string? projectDirectory, string? profile, IReadOnlyList<string> profiles, IReadOnlyList<SettingsSource> sources,
        IReadOnlyDictionary<string, NodePath> profilePaths)
    {
        _provider = provider; Tree = tree; Path = path; _json = json;
        ProjectDirectory = projectDirectory; Profile = profile; Profiles = profiles; Sources = sources; _profilePaths = profilePaths;
    }

    public T? Get<T>(NodeDataKey<T> key) where T : class => Path.ConstructValue(key, _provider);

    public void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton(Tree);
        services.AddSingleton<IMarkerDataHelper>(this);
        if (Profile is { } teacher)
            services.AddScoped(_ => new TeacherLayerConfig { TeacherName = NameHelper.Parse(teacher) });
    }

    MarkerData IMarkerDataHelper.GetMarkerData(IServiceProvider sp) => new(sp.GetRequiredService<TeacherLayerConfig>());
    NodePath? IMarkerDataHelper.GetCurrentPath(MarkerData data) => data.Value is TeacherLayerConfig { TeacherName: { } name }
        ? _profilePaths.GetValueOrDefault(name.ToString()) : Path;

    public IReadOnlyDictionary<string, SettingsValue> Inspect()
    {
        var values = new SortedDictionary<string, SettingsValue>(StringComparer.Ordinal);
        foreach (var key in Path.Path.SelectMany(x => x.ConfigKeys).Distinct().Where(x => x != TeacherLayerConfig.Key))
        {
            var type = NodeDataKey.Registry.GetTypeFromKey(key);
            var value = typeof(ResolvedSettings).GetMethod(nameof(GetNode), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(type).Invoke(this, [key]);
            values.Add(key.Value, new((JsonNode?)value, ContributingSources(key.Value)));
        }
        return values;
    }

    public SettingsValue Inspect(string key)
    {
        var parts = key.Split('.');
        if (!Inspect().TryGetValue(parts[0], out var block)) throw new JsonException($"Unknown setting key: {key}");
        var value = block.Value;
        foreach (var part in parts.Skip(1))
        {
            if (value is not JsonObject obj || !obj.TryGetPropertyValue(part, out value))
                throw new JsonException($"Unknown setting key: {key}");
        }
        return new(value?.DeepClone(), ContributingSources(key));
    }

    private IReadOnlyList<SettingsSource> ContributingSources(string key) => Sources
        .Select(source => source with { Keys = source.Keys.Where(x => x == key || x.StartsWith(key + ".", StringComparison.Ordinal)
            || key.StartsWith(x + ".", StringComparison.Ordinal)).Order().ToArray() })
        .Where(source => source.Keys.Count != 0).ToArray();

    private JsonNode? GetNode<T>(NodeDataKey key) where T : class
    {
        var value = Get(new NodeDataKey<T>(key));
        return CliSettings.Redact(JsonSerializer.SerializeToNode(value, _json));
    }

    public void Validate()
    {
        foreach (var path in _profilePaths.Values.Prepend(Path))
            foreach (var key in path.Path.SelectMany(x => x.ConfigKeys).Distinct().Where(x => x != TeacherLayerConfig.Key))
            {
                try
                {
                    typeof(ResolvedSettings).GetMethod(nameof(ValidateBlock), BindingFlags.NonPublic | BindingFlags.Instance)!
                        .MakeGenericMethod(NodeDataKey.Registry.GetTypeFromKey(key)).Invoke(this, [path, key]);
                }
                catch (TargetInvocationException e) when (e.InnerException is { } error)
                {
                    throw new JsonException($"Cannot resolve settings block '{key.Value}': {error.Message}");
                }
            }
    }

    private void ValidateBlock<T>(NodePath path, NodeDataKey key) where T : class =>
        _ = path.ConstructValue(new NodeDataKey<T>(key), _provider);

    public void Dispose() => _provider.Dispose();
}

public static partial class CliSettings
{
    public const string FileName = "schedulelib.json";
    private static readonly HashSet<string> SupportedBlocks = new(StringComparer.Ordinal)
    {
        "GoogleCalendarConfig", "GoogleDriveConfig", "MoodleConfig", "RegistryConfig", "RegistryLessonFilterConfig",
        "DeadlinesExcelConfig", "LessonAttendanceConfig", "LessonTopicsConfig", "LabTasksDatabaseConfig",
    };
    public static string UserFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ScheduleLib", FileName);

    public static async Task<ResolvedSettings> Load(SettingsArguments arguments, string? invocationDirectory = null,
        string? userFile = null, CancellationToken cancellationToken = default, ConfigOverrideArguments? overrides = null)
        => await LoadCore(arguments, invocationDirectory, userFile, cancellationToken, overrides);

    private static async Task<ResolvedSettings> LoadCore(SettingsArguments arguments, string? invocationDirectory,
        string? userFile, CancellationToken cancellationToken, ConfigOverrideArguments? overrides = null,
        (string Path, JsonObject Envelope)? replacement = null)
    {
        var cwd = Path.GetFullPath(invocationDirectory ?? Environment.CurrentDirectory);
        var project = DiscoverProject(arguments.Project, cwd);
        var services = new ServiceCollection();
        // Registration only: never initialize schedules, credential resolvers, provider clients, or user secrets.
        services.AddConfigsServices();
        services.AddOnlineRegistry();
        var provider = services.BuildServiceProvider();
        try
        {
            var tree = provider.GetRequiredService<TreeBuilder>();
            tree.AddDefaultConfig();
            var codeProfiles = tree.GetMarkerNodes().ToDictionary(x => x.Config.TeacherName.ToString(), x => x.Builder.Node, StringComparer.Ordinal);
            if (arguments.Profile is { } teacher && !codeProfiles.ContainsKey(teacher))
                throw new JsonException($"Unknown teacher profile '{teacher}'. Use config profiles to list supported teachers.");
            var json = new JsonSerializerOptions(provider.GetRequiredService<IOptionsMonitor<JsonSerializerOptions>>().Get(TreeSerializer.ServiceKey))
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
                WriteIndented = true,
            };
            json.Converters.Insert(0, new ConfigColorConverter());
            json.Converters.Insert(0, new JsonStringEnumConverter(allowIntegerValues: false));
            var user = await ReadFile(userFile ?? UserFile, json, codeProfiles.Keys, cancellationToken, replacement);
            var local = project is null ? null : await ReadFile(Path.Combine(project, FileName), json, codeProfiles.Keys, cancellationToken, replacement);
            var defaultSources = new List<SettingsSource> { CodeSource("code defaults", tree.BaseNode, json) };
            var defaultsPath = ImmutableArray.CreateBuilder<MutableNode>();
            defaultsPath.Add(tree.BaseNode);
            AddOverlay(defaultsPath, defaultSources, user, "user defaults", user?.Defaults);
            AddOverlay(defaultsPath, defaultSources, local, "project settings", local?.Defaults);
            var profilePaths = new Dictionary<string, NodePath>(StringComparer.Ordinal);
            var selectedPath = defaultsPath.ToImmutable();
            var selectedSources = defaultSources;
            foreach (var coded in codeProfiles)
            {
                var path = defaultsPath.ToImmutable().ToBuilder();
                var sources = new List<SettingsSource>(defaultSources);
                path.Add(coded.Value);
                sources.Add(CodeSource("code profile: " + coded.Key, coded.Value, json));
                AddOverlay(path, sources, user, "user profile: " + coded.Key, user?.Profiles.GetValueOrDefault(coded.Key));
                AddOverlay(path, sources, local, "project profile: " + coded.Key, local?.Profiles.GetValueOrDefault(coded.Key));
                AddCli(path, sources);
                profilePaths.Add(coded.Key, new(path.ToImmutable()));
                if (arguments.Profile == coded.Key) { selectedPath = path.ToImmutable(); selectedSources = sources; }
            }
            if (arguments.Profile is null)
            {
                var path = selectedPath.ToBuilder();
                AddCli(path, selectedSources);
                selectedPath = path.ToImmutable();
            }
            var resolved = new ResolvedSettings(provider, tree, new(selectedPath), json, project, arguments.Profile,
                codeProfiles.Keys.Order().ToArray(), selectedSources, profilePaths);
            resolved.Validate();
            return resolved;

            void AddOverlay(ImmutableArray<MutableNode>.Builder path, List<SettingsSource> sources,
                SettingsFile? file, string name, JsonObject? values)
            {
                if (file is null || values is null) return;
                var node = new MutableNode();
                path.Add(node);
                foreach (var setting in values)
                {
                    var key = new NodeDataKey(setting.Key);
                    var type = NodeDataKey.Registry.GetTypeFromKey(key);
                    var value = setting.Value?.Deserialize(type, json);
                    if (value is not null) SetValue(node, key, value);
                }
                sources.Add(new(name, file.Path, LeafKeys(values).ToArray()));
            }

            void AddCli(ImmutableArray<MutableNode>.Builder path, List<SettingsSource> sources)
            {
                if (overrides?.CalendarName is not { } calendar) return;
                var node = new MutableNode();
                new NodeBuilder(node, provider).GoogleCalendar().ConfigureValue(x => x.CalendarName = calendar);
                path.Add(node);
                sources.Add(new("CLI arguments", null, ["GoogleCalendarConfig.calendarName"]));
            }
        }
        catch { provider.Dispose(); throw; }
    }

    public static string? DiscoverProject(string? explicitProject, string invocationDirectory)
    {
        if (explicitProject is { } selected)
        {
            string full;
            try { full = Path.GetFullPath(selected, invocationDirectory); }
            catch (ArgumentException) { throw new JsonException("Invalid project directory path."); }
            if (!Directory.Exists(full)) throw new DirectoryNotFoundException($"Project directory does not exist: {full}");
            return full;
        }
        for (var directory = new DirectoryInfo(invocationDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, FileName))) return directory.FullName;
        return null;
    }

    private sealed record SettingsFile(string Path, JsonObject? Defaults, Dictionary<string, JsonObject?> Profiles);

    private static async Task<SettingsFile?> ReadFile(string path, JsonSerializerOptions json, IEnumerable<string> teachers, CancellationToken cancellationToken, (string Path, JsonObject Envelope)? replacement = null)
    {
        if (replacement is { } candidate && Path.GetFullPath(path) == candidate.Path)
            return ReadEnvelope((JsonObject)candidate.Envelope.DeepClone(), path, json, teachers);
        if (!File.Exists(path)) return null;
        await using var input = File.OpenRead(path);
        JsonNode? node;
        try
        {
            using var document = await JsonDocument.ParseAsync(input, cancellationToken: cancellationToken);
            CheckDuplicates(document.RootElement, path);
            node = JsonNode.Parse(document.RootElement.GetRawText());
        }
        catch (JsonException e) { throw new JsonException($"Invalid settings JSON in {path}: {e.Message}"); }
        return ReadEnvelope(node, path, json, teachers);
    }

    private static SettingsFile ReadEnvelope(JsonNode? node, string path, JsonSerializerOptions json, IEnumerable<string> teachers)
    {
        if (node is not JsonObject envelope) throw new JsonException($"Expected settings object in {path}.");
        CheckKeys(envelope, ["schemaVersion", "defaults", "profiles"], path);
        if (envelope["schemaVersion"] is not JsonValue version || !version.TryGetValue<int>(out var schema) || schema != 1)
            throw new JsonException($"Unsupported or missing schemaVersion in {path}; expected 1.");
        var defaults = ReadBlock(envelope["defaults"], "defaults");
        var profiles = new Dictionary<string, JsonObject?>(StringComparer.Ordinal);
        if (envelope["profiles"] is { } profileNode)
        {
            if (profileNode is not JsonObject map) throw new JsonException($"profiles must be an object in {path}.");
            foreach (var entry in map)
            {
                if (!teachers.Contains(entry.Key)) throw new JsonException($"Unknown teacher profile '{entry.Key}' in {path}.");
                profiles.Add(entry.Key, ReadBlock(entry.Value, "profiles." + entry.Key));
            }
        }
        return new(Path.GetFullPath(path), defaults, profiles);

        JsonObject? ReadBlock(JsonNode? block, string location)
        {
            if (block is null) return null;
            if (block is not JsonObject values) throw new JsonException($"{location} must be an object in {path}.");
            foreach (var setting in values)
            {
                var key = new NodeDataKey(setting.Key);
                var type = NodeDataKey.Registry.TryGetTypeFromKey(key);
                if (type is null || !SupportedBlocks.Contains(setting.Key))
                    throw new JsonException($"Unsupported settings block '{setting.Key}' in {path}.");
                RejectSecrets(setting.Value, path);
                NormalizeInheritance(setting.Value, type, json, path);
                ResolvePaths(setting.Value, Path.GetDirectoryName(Path.GetFullPath(path))!);
                // Separate CLI options; the desktop serializer's format and options remain unchanged.
                try { _ = setting.Value?.Deserialize(type, json); }
                catch (Exception e) when (e is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
                {
                    throw new JsonException($"Invalid {location}.{setting.Key} in {path}: {e.Message}");
                }
            }
            return values;
        }
    }

    private static void SetValue(MutableNode node, NodeDataKey key, object value) => typeof(CliSettings)
        .GetMethod(nameof(SetTypedValue), BindingFlags.NonPublic | BindingFlags.Static)!
        .MakeGenericMethod(value.GetType()).Invoke(null, [node, key, value]);

    private static void SetTypedValue<T>(MutableNode node, NodeDataKey key, object value) where T : class =>
        node.GetOrAdd(new NodeDataKey<T>(key)).SetValue((T)value);

    private static void CheckDuplicates(JsonElement value, string file)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException($"Duplicate settings key '{property.Name}' in {file}.");
                CheckDuplicates(property.Value, file);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray()) CheckDuplicates(item, file);
    }

    private static void CheckKeys(JsonObject value, string[] allowed, string path)
    {
        foreach (var key in value.Select(x => x.Key))
            if (!allowed.Contains(key)) throw new JsonException($"Unsupported settings key '{key}' in {path}.");
    }

    private static void RejectSecrets(JsonNode? node, string file)
    {
        if (node is JsonObject obj)
        {
            foreach (var entry in obj)
            {
                if (IsSecret(entry.Key) || entry.Key == "$type" && entry.Value?.ToString() is "ValueCredentialsSource" or "ManualGoogleApiKeysSource")
                    throw new JsonException($"Inline secrets are unsupported in settings file {file}; reference the existing credential configuration instead.");
                RejectSecrets(entry.Value, file);
            }
        }
        else if (node is JsonArray array) foreach (var item in array) RejectSecrets(item, file);
    }

    private static void NormalizeInheritance(JsonNode? node, Type type, JsonSerializerOptions json, string file)
    {
        if (node is JsonArray array && type.IsGenericType)
        {
            foreach (var item in array)
            {
                if (item is null) throw new JsonException($"Null collection entries are unsupported in {file}.");
                NormalizeInheritance(item, type.GetGenericArguments()[0], json, file);
            }
            return;
        }
        if (node is not JsonObject obj) return;
        var info = json.GetTypeInfo(type);
        if (info.PolymorphismOptions is { } polymorphism && obj["$type"] is { } discriminator)
        {
            var derived = polymorphism.DerivedTypes.FirstOrDefault(x => x.TypeDiscriminator?.ToString() == discriminator.ToString());
            if (derived.DerivedType is not null) info = json.GetTypeInfo(derived.DerivedType);
        }
        if (info.Kind != JsonTypeInfoKind.Object) return; // Custom converters validate their own representation.
        foreach (var entry in obj.ToArray())
        {
            if (entry.Key == "$type") continue;
            var property = info.Properties.FirstOrDefault(x => x.Name == entry.Key);
            if (property is null) throw new JsonException($"Unsupported settings member '{entry.Key}' in {file}.");
            if (entry.Value is null) obj.Remove(entry.Key);
            else NormalizeInheritance(entry.Value, property.PropertyType, json, file);
        }
    }

    private static void ResolvePaths(JsonNode? node, string directory)
    {
        if (node is JsonObject obj)
        {
            foreach (var entry in obj.ToArray())
            {
                if (entry.Key is "credentialsPath" or "filePath" or "path" && entry.Value is JsonValue value && value.TryGetValue<string>(out var path))
                {
                    try { obj[entry.Key] = Path.GetFullPath(path, directory); }
                    catch (ArgumentException) { throw new JsonException($"Invalid settings path for '{entry.Key}'."); }
                }
                else ResolvePaths(entry.Value, directory);
            }
        }
        else if (node is JsonArray array) foreach (var item in array) ResolvePaths(item, directory);
    }

    private static SettingsSource CodeSource(string name, MutableNode node, JsonSerializerOptions json)
    {
        var keys = new List<string>();
        foreach (var config in node.Configs.Where(x => x.Key != TeacherLayerConfig.Key))
        {
            var value = config.Container.GetValue();
            if (value is null) keys.Add(config.Key.Value); // Includes code-defined block removals.
            else
            {
                var leaves = LeafKeys(JsonSerializer.SerializeToNode(value, value.GetType(), json), config.Key.Value).ToArray();
                keys.AddRange(leaves.Length == 0 ? [config.Key.Value] : leaves);
            }
        }
        return new(name, null, keys);
    }

    private static IEnumerable<string> LeafKeys(JsonNode? node, string prefix = "")
    {
        if (node is null) yield break;
        if (node is JsonObject obj)
        {
            foreach (var entry in obj)
                foreach (var key in LeafKeys(entry.Value, prefix.Length == 0 ? entry.Key : prefix + "." + entry.Key)) yield return key;
        }
        else yield return prefix;
    }

    private static bool IsSecret(string key) => key.Contains("password", StringComparison.OrdinalIgnoreCase)
        || key.Contains("secret", StringComparison.OrdinalIgnoreCase) || key.Contains("token", StringComparison.OrdinalIgnoreCase)
        || key.Equals("apiKey", StringComparison.OrdinalIgnoreCase) || key.Equals("accessKey", StringComparison.OrdinalIgnoreCase);

    internal static JsonNode? Redact(JsonNode? node)
    {
        if (node is JsonObject obj)
            foreach (var entry in obj.ToArray())
                if (IsSecret(entry.Key)) obj[entry.Key] = "[redacted]";
                else Redact(entry.Value);
        else if (node is JsonArray array) foreach (var item in array) Redact(item);
        return node;
    }
}

/// <summary>CLI-only color representation; desktop serialization is intentionally unchanged.</summary>
internal sealed class ConfigColorConverter : JsonConverter<Color>
{
    public override Color Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("Color must be a known color name or #AARRGGBB.");
        var value = reader.GetString()!;
        if (value.Length == 9 && value[0] == '#' && uint.TryParse(value.AsSpan(1), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out var argb)) return Color.FromArgb(unchecked((int)argb));
        var color = Color.FromName(value);
        if (!color.IsKnownColor) throw new JsonException("Color must be a known color name or #AARRGGBB.");
        return color;
    }

    public override void Write(Utf8JsonWriter writer, Color value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.IsKnownColor ? value.Name : $"#{unchecked((uint)value.ToArgb()):X8}");
}
