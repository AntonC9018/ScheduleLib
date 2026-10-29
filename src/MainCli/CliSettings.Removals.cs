using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Cli;

public static partial class CliSettings
{
    /// <summary>A setting path resolved to the configuration block and value it addresses.</summary>
    private readonly record struct OperationTarget(Type Block, PropertyInfo[] Members, Type Value);

    // Operations belong to the CLI envelope; desktop TreeSerializer remains values-only.
    private static JsonObject? ReadOperations(
        JsonNode? node,
        string file,
        JsonSerializerOptions json,
        IEnumerable<string> teachers)
    {
        if (node is null)
        {
            return null;
        }

        if (node is not JsonObject operations)
        {
            throw new SettingsObjectRequiredException(location: "operations", settingsPath: file);
        }

        CheckKeys(
            operations,
            ["defaults", "profiles"],
            file);
        if (operations.ContainsKey("defaults"))
        {
            ReadActions(operations["defaults"]);
        }

        if (operations["profiles"] is { } profiles)
        {
            if (profiles is not JsonObject map)
            {
                throw new SettingsObjectRequiredException(location: "operations.profiles", settingsPath: file);
            }

            foreach (var entry in map)
            {
                if (!teachers.Contains(entry.Key))
                {
                    throw new UnknownSettingsProfileException(profile: entry.Key, settingsPath: file);
                }

                ReadActions(entry.Value, isProfile: true);
            }
        }
        return operations;

        void ReadActions(JsonNode? actions, bool isProfile = false)
        {
            if (actions is not JsonArray array)
            {
                throw new SettingsOperationsArrayRequiredException(file);
            }

            foreach (var entry in array)
            {
                if (entry is not JsonObject action)
                {
                    throw new SettingsOperationObjectRequiredException(file);
                }

                CheckKeys(
                    action,
                    ["operation", "key", "item"],
                    file);
                ValidateOperation(action, json);
                if (isProfile
                    && action["key"]!.GetValue<string>()
                        .Split('.')[0]
                    == "ScheduleDerivationConfig")
                {
                    throw new JsonException("ScheduleDerivationConfig is global and cannot be configured per profile.");
                }

                RejectSecrets(action["item"], file);

                string CalculateDirectory()
                {
                    string path = Path.GetFullPath(file);
                    string directory = Path.GetDirectoryName(path)!;
                    return directory;
                }

                ResolvePaths(action["item"], CalculateDirectory());
            }
        }
    }

    private static JsonArray? GetOperations(JsonObject? operations, string? profile) =>
        (profile is null ? operations?["defaults"] : operations?["profiles"]?[profile]) as JsonArray;

    private static OperationTarget OperationPath(
        string key,
        JsonSerializerOptions json)
    {
        var parts = key.Split('.');
        var nodeDataKeyRegistry = NodeDataKey.Registry;
        NodeDataKey key2 = new(parts[0]);
        var block = nodeDataKeyRegistry.TryGetTypeFromKey(key2);
        if (block is null || !SupportedBlocks.Contains(parts[0]))
        {
            throw new UnsupportedSettingKeyException(key);
        }

        var type = block;
        var members = new List<PropertyInfo>();
        foreach (var part in parts.Skip(1))
        {
            var property = json.GetTypeInfo(type).Properties
                .FirstOrDefault(x => x.Name == part);
            if (property?.AttributeProvider is not PropertyInfo member)
            {
                throw new UnsupportedOperationKeyException(key);
            }

            members.Add(member);
            type = member.PropertyType;
        }
        return new(
            Block: block,
            members.ToArray(),
            Value: type);
    }

    private static void ValidateOperation(JsonObject action, JsonSerializerOptions json)
    {
        if (action["key"] is not JsonValue keyValue)
        {
            throw new SettingsOperationMembersRequiredException("key");
        }
        if (!keyValue.TryGetValue<string>(out var key))
        {
            throw new SettingsOperationMembersRequiredException("key");
        }
        if (action["operation"] is not JsonValue opValue)
        {
            throw new SettingsOperationMembersRequiredException("operation");
        }
        if (!opValue.TryGetValue<string>(out var operation))
        {
            throw new SettingsOperationMembersRequiredException("operation");
        }

        var path = OperationPath(key, json);
        var list = path.Value.IsGenericType && path.Value.GetGenericTypeDefinition() == typeof(List<>);
        var hasItem = action.ContainsKey("item");

        bool CheckCondition()
        {
            if (!(operation is "remove" or "reset"))
            {
                return false;
            }

            if (path.Members.Length != 0)
            {
                return false;
            }

            if (!hasItem)
            {
                return true;
            }

            return false;
        }

        if (CheckCondition())
        {
            return;
        }

        if (!list)
        {
            throw new UnsupportedSettingsOperationException(operation: operation, settingKey: key);
        }

        if (operation is not ("remove" or "clear"))
        {
            throw new UnsupportedSettingsOperationException(operation: operation, settingKey: key);
        }
        if (operation == "remove" != hasItem)
        {
            throw new UnsupportedSettingsOperationException(operation: operation, settingKey: key);
        }

        if (hasItem)
        {
            if (action["item"] is not JsonObject item)
            {
                throw new JsonException("item must be a typed JSON object containing the registered key.");
            }

            try
            {
                JsonNode node = item;
                var genericArguments = path.Value.GetGenericArguments();
                int int32 = 0;
                var type = genericArguments[int32];
                var json2 = json;
                string file = "operation item";
                NormalizeInheritance(
                    node,
                    type,
                    json2,
                    file);

                Type CalculateReturnType()
                {
                    var genericArguments2 = path.Value.GetGenericArguments();
                    int int322 = 0;
                    var returnType = genericArguments2[int322];
                    return returnType;
                }

                _ = item.Deserialize(CalculateReturnType(), json)
                    ?? throw new JsonException("item cannot be null.");
            }
            catch (Exception error) when (error is JsonException
                                              or ArgumentException
                                              or NotSupportedException
                                              or InvalidOperationException)
            {
                throw new InvalidTypedSettingsOperationItemException(key, error);
            }
        }
    }

    private static JsonObject CreateOperation(
        string operation,
        string key,
        string? item,
        ResolvedSettings settings)
    {
        var action = new JsonObject
        {
            ["operation"] = operation,
            ["key"] = key,
        };
        if (item is not null)
        {
            using var document = JsonDocument.Parse(item);
            CheckDuplicates(document.RootElement, "item");
            action["item"] = JsonNode.Parse(document.RootElement.GetRawText());
        }
        ValidateOperation(action, settings.Json);
        RejectSecrets(action["item"], "item");
        return action;
    }

    private static void ValidateRemoval(
        JsonObject action,
        ResolvedSettings settings,
        string file)
    {
        var path = OperationPath(action["key"]!.GetValue<string>(), settings.Json);
        if (path.Members.Length == 0)
        {
            return;
        }

        var element = path.Value.GetGenericArguments()[0];
        var iServiceProvider = settings.Services;
        var serviceType = typeof(IKeyEqualityComparer<>).MakeGenericType(element);
        var comparer = iServiceProvider.GetService(serviceType);
        if (comparer is null)
        {
            throw new JsonException("Collection has no registered item key.");
        }

        if (action["operation"]!.GetValue<string>() != "remove")
        {
            return;
        }

        var descriptor = action["item"]!.DeepClone();

        string CalculateDirectory()
        {
            string directory = Path.GetDirectoryName(file)!;
            return directory;
        }

        ResolvePaths(descriptor, CalculateDirectory());
        var item = descriptor.Deserialize(element, settings.Json)!;
        var values = settings.Inspect(action["key"]!.GetValue<string>()).Value as JsonArray;
        if (values is null
            || !values.Any(x =>
            {
                var resolvedValue = x!.Deserialize(element, settings.Json)!;
                return KeyMatches(
                    element,
                    comparer: comparer,
                    item: item,
                    value: resolvedValue);
            }))
        {
            throw new JsonException("Unknown collection item key; no resolved entry matches --item.");
        }
    }

    private static bool KeyMatches(
        Type element,
        object comparer,
        object item,
        object value)
    {
        return (bool) typeof(IEqualityComparer<>).MakeGenericType(element)
            .GetMethod("Equals", [element, element])!
            .Invoke(
                comparer,
                [item, value])!;
    }

    private static void AddOperation(
        MutableNode node,
        JsonObject action,
        IServiceProvider provider,
        JsonSerializerOptions json)
    {
        var path = OperationPath(action["key"]!.GetValue<string>(), json);
        if (path.Members.Length != 0)
        {
            var elementType = path.Value.GetGenericArguments()[0];
            var comparerType = typeof(IKeyEqualityComparer<>).MakeGenericType(elementType);
            var comparer = provider.GetService(comparerType);
            if (comparer is null)
            {
                throw new JsonException("Collection has no registered item key.");
            }
        }

        typeof(CliSettings).GetMethod(nameof(AddTypedOperation), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(path.Block)
            .Invoke(null, [node, action, path.Members, provider, json]);
    }

    private static void AddTypedOperation<T>(
        MutableNode node,
        JsonObject action,
        PropertyInfo[] members,
        IServiceProvider provider,
        JsonSerializerOptions json) where T : class
    {
        var keyPath = action["key"]!.GetValue<string>().Split('.');
        var keyName = keyPath[0];
        var key = new NodeDataKey<T>(new NodeDataKey(keyName));
        var updates = node.GetOrAdd(key)
            .UpdateActions;
        var operation = action["operation"]!.GetValue<string>();
        if (members.Length == 0)
        {
            updates.Add(operation == "remove" ? RemoveValueUpdater<T>.Instance : ResetValueUpdater<T>.Instance);
            return;
        }
        updates.Add(new DelegateUpdater<T>((sp, value) =>
        {
            object? current = value;
            foreach (var member in members)
            {
                if (current is not null)
                {
                    current = member.GetValue(current);
                }
            }

            if (current is not IList list)
            {
                return value;
            }

            if (operation == "clear")
            {
                // The registered basic operation defines collection reset semantics.
                var serviceType = typeof(IBasicOperations<>).MakeGenericType(members[^1].PropertyType);
                var basic = sp.GetRequiredService(
                    serviceType);
                basic.GetType()
                    .GetMethod("Reset")!
                    .Invoke(basic, [list]);
            }
            else
            {
                var element = members[^1].PropertyType.GetGenericArguments()[0];
                var serviceType2 = typeof(IKeyEqualityComparer<>).MakeGenericType(element);
                var comparer = sp.GetRequiredService(serviceType2);
                var item = action["item"]!.Deserialize(element, json)!;
                for (var i = list.Count - 1; i >= 0; i--)
                {
                    if (KeyMatches(
                            element,
                            comparer,
                            item,
                            list[i]!))
                    {
                        list.RemoveAt(i);
                    }
                }
            }
            return value;
        }));
    }

    private static void ApplyOperation(
        JsonObject envelope,
        string? profile,
        JsonObject action)
    {
        var operations = envelope["operations"] as JsonObject;
        if (operations is null)
        {
            envelope["operations"] = operations = new();
        }

        var target = operations;
        var name = "defaults";
        if (profile is not null)
        {
            if (operations["profiles"] is not JsonObject profiles)
            {
                operations["profiles"] = profiles = new();
            }

            target = profiles;
            name = profile;
        }
        if (target[name] is not JsonArray actions)
        {
            target[name] = actions = new();
        }

        if (!actions.Any(x => JsonNode.DeepEquals(node1: x, node2: action)))
        {
            actions.Add(action.DeepClone());
        }
    }

    private static void RemoveOperations(
        JsonObject envelope,
        string? profile,
        string key,
        bool includeAncestors)
    {
        if (envelope["operations"] is not JsonObject operations
            || GetOperations(operations, profile) is not { } actions)
        {
            return;
        }

        foreach (var action in actions.Cast<JsonObject>()
                     .ToArray())
        {
            var actionKey = action["key"]!.GetValue<string>();
            bool conditionResult;
            if (actionKey == key)
            {
                conditionResult = true;
            }
            else
            {
                string text = actionKey;
                string text2 = $"{key}.";
                string value = text2;
                var comparisonType = StringComparison.Ordinal;
                if (text.StartsWith(value, comparisonType))
                {
                    conditionResult = true;
                }
                else
                {
                    if (includeAncestors)
                    {
                        string text3 = key;
                        string text4 = $"{actionKey}.";
                        string value2 = text4;
                        var comparisonType2 = StringComparison.Ordinal;
                        bool startsWith = text3.StartsWith(value2, comparisonType2);
                        conditionResult = startsWith;
                    }
                    else
                    {
                        conditionResult = false;
                    }
                }
            }

            if (conditionResult)
            {
                actions.Remove(action);
            }
        }
        if (actions.Count == 0)
        {
            if (profile is null)
            {
                operations.Remove("defaults");
            }
            else if (operations["profiles"] is JsonObject profiles)
            {
                profiles.Remove(profile);
                if (profiles.Count == 0)
                {
                    operations.Remove("profiles");
                }
            }
        }
        if (operations.Count == 0)
        {
            envelope.Remove("operations");
        }
    }
}
