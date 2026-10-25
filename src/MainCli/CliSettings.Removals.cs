using System.Collections;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;

namespace ScheduleLib.Cli;

public static partial class CliSettings
{
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
            throw new JsonException($"operations must be an object in {file}.");
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
                throw new JsonException($"operations.profiles must be an object in {file}.");
            }

            foreach (var entry in map)
            {
                if (!teachers.Contains(entry.Key))
                {
                    throw new JsonException($"Unknown teacher profile '{entry.Key}' in {file}.");
                }

                ReadActions(entry.Value, isProfile: true);
            }
        }
        return operations;

        void ReadActions(JsonNode? actions, bool isProfile = false)
        {
            if (actions is not JsonArray array)
            {
                throw new JsonException($"Operations must be an array in {file}.");
            }

            foreach (var entry in array)
            {
                if (entry is not JsonObject action)
                {
                    throw new JsonException($"Expected operation object in {file}.");
                }

                CheckKeys(
                    action,
                    ["operation", "key", "item"],
                    file);
                ValidateOperation(action, json);
                if (isProfile && action["key"]!.GetValue<string>()
                    .Split('.')[0] == "ScheduleDerivationConfig")
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

    private static (Type Block, PropertyInfo[] Members, Type Value) OperationPath(
        string key,
        JsonSerializerOptions json)
    {
        var parts = key.Split('.');
        global::Anton.LayeredData.NodeDataKeyRegistry nodeDataKeyRegistry = NodeDataKey.Registry;
        global::Anton.LayeredData.NodeDataKey key2 = new(parts[0]);
        var block = nodeDataKeyRegistry.TryGetTypeFromKey(key2);
        if (block is null || !SupportedBlocks.Contains(parts[0]))
        {
            throw new JsonException($"Unsupported setting key: {key}");
        }

        var type = block;
        var members = new List<PropertyInfo>();
        foreach (var part in parts.Skip(1))
        {
            var property = json.GetTypeInfo(type).Properties
                .FirstOrDefault(x => x.Name == part);
            if (property?.AttributeProvider is not PropertyInfo member)
            {
                throw new JsonException($"Unsupported operation key: {key}");
            }

            members.Add(member);
            type = member.PropertyType;
        }
        return (block, members.ToArray(), type);
    }

    private static void ValidateOperation(JsonObject action, JsonSerializerOptions json)
    {
        if (action["key"] is not JsonValue keyValue
            || !keyValue.TryGetValue<string>(out var key)
            || action["operation"] is not JsonValue opValue
            || !opValue.TryGetValue<string>(out var operation))
        {
            throw new JsonException("Operation requires string key and operation members.");
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

            if (!(path.Members.Length == 0))
            {
                return false;
            }

            if (!(!hasItem))
            {
                return false;
            }

            return true;
        }

        if (CheckCondition())
        {
            return;
        }

        if (!list)
        {
            throw new JsonException(
                $"Unsupported {operation} operation for {key}; remove accepts a block or collection --item, clear accepts a collection.");
        }

        if (operation is not ("remove" or "clear"))
        {
            throw new JsonException(
                $"Unsupported {operation} operation for {key}; remove accepts a block or collection --item, clear accepts a collection.");
        }
        if ((operation == "remove") != hasItem)
        {
            throw new JsonException(
                $"Unsupported {operation} operation for {key}; remove accepts a block or collection --item, clear accepts a collection.");
        }

        if (hasItem)
        {
            if (action["item"] is not JsonObject item)
            {
                throw new JsonException("item must be a typed JSON object containing the registered key.");
            }

            try
            {
                global::System.Text.Json.Nodes.JsonNode node = item;
                global::System.Type[] genericArguments = path.Value.GetGenericArguments();
                int int32 = 0;
                global::System.Type type = genericArguments[int32];
                global::System.Text.Json.JsonSerializerOptions json2 = json;
                string file = "operation item";
                NormalizeInheritance(
                    node, type, json2, file);
                global::System.Type CalculateReturnType()
                {
                    global::System.Type[] genericArguments2 = path.Value.GetGenericArguments();
                    int int322 = 0;
                    global::System.Type returnType = genericArguments2[int322];
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
                throw new JsonException(
                    $"Invalid typed item for {key}; include its registered identity and any required $type discriminator.",
                    error);
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
        global::System.IServiceProvider iServiceProvider = settings.Services;
        global::System.Type serviceType = typeof(IKeyEqualityComparer<>).MakeGenericType(element);
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
            || !values.Any(x => KeyMatches(
                element,
                comparer,
                item,
                x!.Deserialize(element, settings.Json)!)))
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
        bool conditionResult;
        if (path.Members.Length != 0)
        {
            global::System.Type CalculateTypeArguments()
            {
                global::System.Type[] genericArguments = path.Value.GetGenericArguments();
                int int32 = 0;
                global::System.Type typeArguments = genericArguments[int32];
                return typeArguments;
            }

            global::System.Type CalculateServiceType()
            {
                global::System.Type serviceType = typeof(IKeyEqualityComparer<>).MakeGenericType(CalculateTypeArguments());
                return serviceType;
            }

            object? conditionValue = provider.GetService(
                                        CalculateServiceType());
            conditionResult = conditionValue is null;
        }
        else
        {
            conditionResult = false;
        }

        if (conditionResult)
        {
            throw new JsonException("Collection has no registered item key.");
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
        global::Anton.LayeredData.NodeDataKey<T> CalculateKey()
        {
            string[] split = action["key"]!.GetValue<string>()
                .Split('.');
            int int32 = 0;
            string value2 = split[int32];
            global::Anton.LayeredData.NodeDataKey value3 = new(value2);
            global::Anton.LayeredData.NodeDataKey<T> key = new NodeDataKey<T>(value3);
            return key;
        }

        var updates = node.GetOrAdd(CalculateKey())
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
                global::System.IServiceProvider iServiceProvider = sp;
                global::System.Type serviceType = typeof(IBasicOperations<>).MakeGenericType(members[^1].PropertyType);
                var basic = iServiceProvider.GetRequiredService(
                    serviceType);
                basic.GetType()
                    .GetMethod("Reset")!
                    .Invoke(basic, [list]);
            }
            else
            {
                var element = members[^1].PropertyType.GetGenericArguments()[0];
                global::System.IServiceProvider iServiceProvider2 = sp;
                global::System.Type serviceType2 = typeof(IKeyEqualityComparer<>).MakeGenericType(element);
                var comparer = iServiceProvider2.GetRequiredService(serviceType2);
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

        JsonObject target = operations;
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
                string value = (string) text2;
                global::System.StringComparison comparisonType = StringComparison.Ordinal;
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
                        string value2 = (string) text4;
                        global::System.StringComparison comparisonType2 = StringComparison.Ordinal;
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
