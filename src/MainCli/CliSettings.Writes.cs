using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using Anton.LayeredData;
using ScheduleLib.Application.Core;

namespace ScheduleLib.Cli;

public enum SettingsScope
{
    User,
    Project,
}

public sealed record SettingsEdit(
    string File,
    string Scope,
    string? Profile,
    string Key,
    bool Changed);

internal readonly record struct SettingsEditRequest(
    SettingsArguments Arguments,
    SettingsScope Scope,
    string Key,
    string? Value,
    bool Unset,
    CancellationToken CancellationToken,
    string? InvocationDirectory = null,
    string? UserFile = null,
    string? Operation = null,
    string? Item = null);

public static partial class CliSettings
{
    /// <summary>A parent object on a settings path, and the member name under it.</summary>
    private readonly record struct SettingsParent(JsonObject Parent, string Key);

    // The candidate is resolved through the same engine as a fresh invocation before publication.
    internal static async Task<SettingsEdit> Edit(SettingsEditRequest request)
    {
        var arguments = request.Arguments;
        var scope = request.Scope;
        var key = request.Key;
        var value = request.Value;
        var unset = request.Unset;
        var cancellationToken = request.CancellationToken;
        var invocationDirectory = request.InvocationDirectory;
        var userFile = request.UserFile;
        var operation = request.Operation;
        var item = request.Item;

        cancellationToken.ThrowIfCancellationRequested();
        var cwd = Path.GetFullPath(invocationDirectory ?? Environment.CurrentDirectory);
        var initial = await Load(
            arguments,
            invocationDirectory: cwd,
            userFile: userFile,
            cancellationToken);
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
            catch (JsonException)
            {
                throw new JsonException("VALUE must be valid typed JSON without duplicate properties.");
            }
            RejectSecrets(parsed, "VALUE");
        }
        var action = operation is null
            ? null
            : CreateOperation(
                operation: operation,
                key: key,
                item: item,
                initial);
        var file = scope switch
        {
            SettingsScope.User => Path.GetFullPath(userFile ?? UserFile),
            SettingsScope.Project when initial.ProjectDirectory is { } project => Path.Combine(project, FileName),
            SettingsScope.Project => throw new JsonException(
                "Project scope requires a discovered settings file or --project DIRECTORY."),
            _ => throw new InvalidSettingsScopeException(scope),
        };
        if (action is not null)
        {
            ValidateRemoval(
                action,
                initial,
                file);
        }
        // Validate a synthetic document before creating even the coordination file.
        var probe = new JsonObject
        {
            ["schemaVersion"] = 1,
        };
        if (action is null)
        {
            Apply(
                probe,
                profile: arguments.Profile,
                key: key,
                parsed,
                unset: false);
        }
        else
        {
            ApplyOperation(
                envelope: probe,
                arguments.Profile,
                action: action);
        }

        ReadEnvelope(
            probe,
            file,
            initial.Json,
            initial.Profiles);
        await using var lease = await LocalFileLock.Acquire(
            $"{file}.lock",
            cancellationToken,
            wait: true);
        // Re-read under the lease so concurrent edits cannot overwrite one another.
        var current = await Load(
            arguments,
            invocationDirectory: cwd,
            userFile: userFile,
            cancellationToken);
        var exists = File.Exists(file);
        var envelope = exists
            ? JsonNode.Parse(await File.ReadAllTextAsync(file, cancellationToken))!
                .AsObject()
            : new JsonObject
            {
                ["schemaVersion"] = 1,
            };
        var before = envelope.ToJsonString();
        if (action is not null)
        {
            ValidateRemoval(
                action,
                current,
                file);
            ApplyOperation(
                envelope: envelope,
                arguments.Profile,
                action: action);
        }
        else
        {
            RemoveOperations(
                envelope,
                profile: arguments.Profile,
                key: key,
                includeAncestors: !unset);
            Apply(
                envelope,
                profile: arguments.Profile,
                key: key,
                parsed,
                unset);
        }
        var candidate = await LoadCore(
            CreateStandaloneTree(),
            SettingsSelection.From(arguments),
            invocationDirectory: cwd,
            userFile: userFile,
            cancellationToken,
            replacement: (file, envelope));
        var changed = before != envelope.ToJsonString();
        if (changed)
        {
            await AtomicFile.Publish(
                file,
                (stream, token) => JsonSerializer.SerializeAsync(
                    stream,
                    envelope,
                    new JsonSerializerOptions
                    {
                        WriteIndented = true,
                    },
                    token),
                cancellationToken);
        }

        var scopeName = scope.ToString()
            .ToLowerInvariant();
        return new(
            File: file,
            Scope: scopeName,
            Profile: arguments.Profile,
            Key: key,
            Changed: changed);
    }

    private static void ValidateEditKey(string key, ResolvedSettings settings)
    {
        var parts = key.Split('.');
        NodeDataKey blockKey = new(parts[0]);
        var type = NodeDataKey.Registry.TryGetTypeFromKey(blockKey);
        if (type is null)
        {
            throw new UnsupportedSettingKeyException(key);
        }
        if (!SupportedBlocks.Contains(parts[0]))
        {
            throw new UnsupportedSettingKeyException(key);
        }
        if (parts.Any(string.IsNullOrEmpty))
        {
            throw new UnsupportedSettingKeyException(key);
        }

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
            if (property is null)
            {
                throw new UnsupportedSettingKeyException(key);
            }

            type = property.PropertyType;
        }
    }

    private static void Apply(
        JsonObject envelope,
        string? profile,
        string key,
        JsonNode? value,
        bool unset)
    {
        var parts = key.Split('.');
        string[] path = profile is null
            ? ["defaults", .. parts]
            : ["profiles", profile, .. parts];
        var current = envelope;
        var parents = new List<SettingsParent>();
        foreach (var part in path.SkipLast(1))
        {
            if (current[part] is not JsonObject next)
            {
                if (unset)
                {
                    return;
                }

                if (current[part] is not null)
                {
                    throw new SettingsMemberIsNotObjectException(key);
                }

                current[part] = next = new JsonObject();
            }
            parents.Add(new(current, part));
            current = next;
        }
        if (!unset)
        {
            current[path[^1]] = value?.DeepClone();
            return;
        }
        current.Remove(path[^1]);
        foreach (var parent in parents.AsEnumerable()
                     .Reverse())
        {
            if (parent.Parent[parent.Key] is JsonObject { Count: 0 })
            {
                parent.Parent.Remove(parent.Key);
            }
            else
            {
                break;
            }
        }
    }
}
