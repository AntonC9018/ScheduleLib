using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Anton.LayeredData;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public enum SettingsScope { User, Project }
public sealed record SettingsEdit(string File, string Scope, string? Profile, string Key, bool Changed);

public static partial class CliSettings
{
    // The candidate is resolved through the same engine as a fresh invocation before publication.
    internal static async Task<SettingsEdit> Edit(SettingsArguments arguments, SettingsScope scope, string key,
        string? value, bool unset, CancellationToken cancellationToken, string? invocationDirectory = null, string? userFile = null,
        string? operation = null, string? item = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var cwd = Path.GetFullPath(invocationDirectory ?? Environment.CurrentDirectory);
        using var initial = await Load(arguments, cwd, userFile, cancellationToken);
        ValidateEditKey(key, initial);
        JsonNode? parsed = null;
        if (!unset && operation is null)
        {
            try
            {
                using var document = JsonDocument.Parse(value!);
                CheckDuplicates(document.RootElement, "VALUE");
                parsed = JsonNode.Parse(document.RootElement.GetRawText());
            }
            catch (JsonException) { throw new JsonException("VALUE must be valid typed JSON without duplicate properties."); }
            RejectSecrets(parsed, "VALUE");
        }
        var action = operation is null ? null : CreateOperation(operation, key, item, initial);
        var file = scope switch
        {
            SettingsScope.User => Path.GetFullPath(userFile ?? UserFile),
            SettingsScope.Project when initial.ProjectDirectory is { } project => Path.Combine(project, FileName),
            SettingsScope.Project => throw new JsonException("Project scope requires a discovered settings file or --project DIRECTORY."),
            _ => throw new ArgumentException("Scope must be user or project."),
        };
        if (action is not null) ValidateRemoval(action, initial, file);
        // Validate a synthetic document before creating even the coordination file.
        var probe = new JsonObject { ["schemaVersion"] = 1 };
        if (action is null) Apply(probe, arguments.Profile, key, parsed, unset: false);
        else ApplyOperation(probe, arguments.Profile, action);
        ReadEnvelope(probe, file, initial.Json, initial.Profiles);
        await using var lease = await LocalFileLock.Acquire(file + ".lock", cancellationToken, wait: true);
        // Re-read under the lease so concurrent edits cannot overwrite one another.
        using var current = await Load(arguments, cwd, userFile, cancellationToken);
        var exists = File.Exists(file);
        var envelope = exists
            ? JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))!.AsObject()
            : new JsonObject { ["schemaVersion"] = 1 };
        var before = envelope.ToJsonString();
        if (action is not null)
        {
            ValidateRemoval(action, current, file);
            ApplyOperation(envelope, arguments.Profile, action);
        }
        else
        {
            RemoveOperations(envelope, arguments.Profile, key, includeAncestors: !unset);
            Apply(envelope, arguments.Profile, key, parsed, unset);
        }
        using var candidate = await LoadCore(arguments, cwd, userFile, cancellationToken, replacement: (file, envelope));
        var changed = before != envelope.ToJsonString();
        if (changed)
            await AtomicFile.Publish(file, (stream, token) => JsonSerializer.SerializeAsync(stream, envelope,
                new JsonSerializerOptions { WriteIndented = true }, token), cancellationToken);
        return new(file, scope.ToString().ToLowerInvariant(), arguments.Profile, key, changed);
    }

    private static void ValidateEditKey(string key, ResolvedSettings settings)
    {
        var parts = key.Split('.');
        var type = NodeDataKey.Registry.TryGetTypeFromKey(new(parts[0]));
        if (type is null || !SupportedBlocks.Contains(parts[0]) || parts.Any(string.IsNullOrEmpty))
            throw new JsonException($"Unsupported setting key: {key}");
        foreach (var part in parts.Skip(1))
        {
            var info = settings.Json.GetTypeInfo(type);
            if (info.Kind != JsonTypeInfoKind.Object)
            {
                // Custom converter member representations are checked by their converter and inspection.
                _ = settings.Inspect(key);
                return;
            }
            var property = info.Properties.FirstOrDefault(x => x.Name == part);
            if (property is null) throw new JsonException($"Unsupported setting key: {key}");
            type = property.PropertyType;
        }
    }

    private static void Apply(JsonObject envelope, string? profile, string key, JsonNode? value, bool unset)
    {
        var parts = key.Split('.');
        var path = profile is null ? new[] { "defaults" }.Concat(parts).ToArray()
            : new[] { "profiles", profile }.Concat(parts).ToArray();
        var current = envelope;
        var parents = new List<(JsonObject Parent, string Key)>();
        foreach (var part in path.SkipLast(1))
        {
            if (current[part] is not JsonObject next)
            {
                if (unset) return;
                if (current[part] is not null) throw new JsonException($"Cannot edit member beneath a non-object setting: {key}");
                current[part] = next = new JsonObject();
            }
            parents.Add((current, part));
            current = next;
        }
        if (!unset) { current[path[^1]] = value?.DeepClone(); return; }
        current.Remove(path[^1]);
        foreach (var parent in parents.AsEnumerable().Reverse())
        {
            if (parent.Parent[parent.Key] is JsonObject { Count: 0 }) parent.Parent.Remove(parent.Key);
            else break;
        }
    }
}
