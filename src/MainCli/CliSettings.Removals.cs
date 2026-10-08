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
    private static JsonObject? ReadOperations(JsonNode? node, string file, JsonSerializerOptions json, IEnumerable<string> teachers)
    {
        if (node is null) return null;
        if (node is not JsonObject operations) throw new JsonException($"operations must be an object in {file}.");
        CheckKeys(operations, ["defaults", "profiles"], file);
        if (operations.ContainsKey("defaults")) ReadActions(operations["defaults"]);
        if (operations["profiles"] is { } profiles)
        {
            if (profiles is not JsonObject map) throw new JsonException($"operations.profiles must be an object in {file}.");
            foreach (var entry in map)
            {
                if (!teachers.Contains(entry.Key)) throw new JsonException($"Unknown teacher profile '{entry.Key}' in {file}.");
                ReadActions(entry.Value);
            }
        }
        return operations;

        void ReadActions(JsonNode? actions)
        {
            if (actions is not JsonArray array) throw new JsonException($"Operations must be an array in {file}.");
            foreach (var entry in array)
            {
                if (entry is not JsonObject action) throw new JsonException($"Expected operation object in {file}.");
                CheckKeys(action, ["operation", "key", "item"], file);
                ValidateOperation(action, json);
                RejectSecrets(action["item"], file);
                ResolvePaths(action["item"], Path.GetDirectoryName(Path.GetFullPath(file))!);
            }
        }
    }

    private static JsonArray? GetOperations(JsonObject? operations, string? profile) =>
        (profile is null ? operations?["defaults"] : operations?["profiles"]?[profile]) as JsonArray;

    private static (Type Block, PropertyInfo[] Members, Type Value) OperationPath(string key, JsonSerializerOptions json)
    {
        var parts = key.Split('.');
        var block = NodeDataKey.Registry.TryGetTypeFromKey(new(parts[0]));
        if (block is null || !SupportedBlocks.Contains(parts[0])) throw new JsonException($"Unsupported setting key: {key}");
        var type = block;
        var members = new List<PropertyInfo>();
        foreach (var part in parts.Skip(1))
        {
            var property = json.GetTypeInfo(type).Properties.FirstOrDefault(x => x.Name == part);
            if (property?.AttributeProvider is not PropertyInfo member) throw new JsonException($"Unsupported operation key: {key}");
            members.Add(member);
            type = member.PropertyType;
        }
        return (block, members.ToArray(), type);
    }

    private static void ValidateOperation(JsonObject action, JsonSerializerOptions json)
    {
        if (action["key"] is not JsonValue keyValue || !keyValue.TryGetValue<string>(out var key)
            || action["operation"] is not JsonValue opValue || !opValue.TryGetValue<string>(out var operation))
            throw new JsonException("Operation requires string key and operation members.");
        var path = OperationPath(key, json);
        var list = path.Value.IsGenericType && path.Value.GetGenericTypeDefinition() == typeof(List<>);
        var hasItem = action.ContainsKey("item");
        if (operation is "remove" or "reset" && path.Members.Length == 0 && !hasItem) return;
        if (!list || operation is not ("remove" or "clear") || (operation == "remove") != hasItem)
            throw new JsonException($"Unsupported {operation} operation for {key}; remove accepts a block or collection --item, clear accepts a collection.");
        if (hasItem)
        {
            if (action["item"] is not JsonObject item) throw new JsonException("item must be a typed JSON object containing the registered key.");
            try
            {
                NormalizeInheritance(item, path.Value.GetGenericArguments()[0], json, "operation item");
                _ = item.Deserialize(path.Value.GetGenericArguments()[0], json) ?? throw new JsonException("item cannot be null.");
            }
            catch (Exception error) when (error is JsonException or ArgumentException or NotSupportedException or InvalidOperationException)
            {
                throw new JsonException($"Invalid typed item for {key}; include its registered identity and any required $type discriminator.", error);
            }
        }
    }

    private static JsonObject CreateOperation(string operation, string key, string? item, ResolvedSettings settings)
    {
        var action = new JsonObject { ["operation"] = operation, ["key"] = key };
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

    private static void ValidateRemoval(JsonObject action, ResolvedSettings settings, string file)
    {
        var path = OperationPath(action["key"]!.GetValue<string>(), settings.Json);
        if (path.Members.Length == 0) return;
        var element = path.Value.GetGenericArguments()[0];
        var comparer = settings.Services.GetService(typeof(IKeyEqualityComparer<>).MakeGenericType(element));
        if (comparer is null) throw new JsonException("Collection has no registered item key.");
        if (action["operation"]!.GetValue<string>() != "remove") return;
        var descriptor = action["item"]!.DeepClone();
        ResolvePaths(descriptor, Path.GetDirectoryName(file)!);
        var item = descriptor.Deserialize(element, settings.Json)!;
        var values = settings.Inspect(action["key"]!.GetValue<string>()).Value as JsonArray;
        if (values is null || !values.Any(x => KeyMatches(element, comparer, item, x!.Deserialize(element, settings.Json)!)))
            throw new JsonException("Unknown collection item key; no resolved entry matches --item.");
    }

    private static bool KeyMatches(Type element, object comparer, object item, object value) =>
        (bool)typeof(IEqualityComparer<>).MakeGenericType(element).GetMethod("Equals", [element, element])!.Invoke(comparer, [item, value])!;

    private static void AddOperation(MutableNode node, JsonObject action, IServiceProvider provider, JsonSerializerOptions json)
    {
        var path = OperationPath(action["key"]!.GetValue<string>(), json);
        if (path.Members.Length != 0 && provider.GetService(typeof(IKeyEqualityComparer<>).MakeGenericType(path.Value.GetGenericArguments()[0])) is null)
            throw new JsonException("Collection has no registered item key.");
        typeof(CliSettings).GetMethod(nameof(AddTypedOperation), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(path.Block).Invoke(null, [node, action, path.Members, provider, json]);
    }

    private static void AddTypedOperation<T>(MutableNode node, JsonObject action, PropertyInfo[] members, IServiceProvider provider,
        JsonSerializerOptions json) where T : class
    {
        var updates = node.GetOrAdd(new NodeDataKey<T>(new(action["key"]!.GetValue<string>().Split('.')[0]))).UpdateActions;
        var operation = action["operation"]!.GetValue<string>();
        if (members.Length == 0)
        {
            updates.Add(operation == "remove" ? RemoveValueUpdater<T>.Instance : ResetValueUpdater<T>.Instance);
            return;
        }
        updates.Add(new DelegateUpdater<T>((sp, value) =>
        {
            object? current = value;
            foreach (var member in members) current = current is null ? null : member.GetValue(current);
            if (current is not IList list) return value;
            if (operation == "clear")
            {
                // The registered basic operation defines collection reset semantics.
                var basic = sp.GetRequiredService(typeof(IBasicOperations<>).MakeGenericType(members[^1].PropertyType));
                basic.GetType().GetMethod("Reset")!.Invoke(basic, [list]);
            }
            else
            {
                var element = members[^1].PropertyType.GetGenericArguments()[0];
                var comparer = sp.GetRequiredService(typeof(IKeyEqualityComparer<>).MakeGenericType(element));
                var item = action["item"]!.Deserialize(element, json)!;
                for (var i = list.Count - 1; i >= 0; i--)
                    if (KeyMatches(element, comparer, item, list[i]!)) list.RemoveAt(i);
            }
            return value;
        }));
    }

    private static void ApplyOperation(JsonObject envelope, string? profile, JsonObject action)
    {
        var operations = envelope["operations"] as JsonObject;
        if (operations is null) envelope["operations"] = operations = new();
        JsonObject target = operations;
        var name = "defaults";
        if (profile is not null)
        {
            if (operations["profiles"] is not JsonObject profiles) operations["profiles"] = profiles = new();
            target = profiles;
            name = profile;
        }
        if (target[name] is not JsonArray actions) target[name] = actions = new();
        if (!actions.Any(x => JsonNode.DeepEquals(x, action))) actions.Add(action.DeepClone());
    }

    private static void RemoveOperations(JsonObject envelope, string? profile, string key, bool includeAncestors)
    {
        if (envelope["operations"] is not JsonObject operations || GetOperations(operations, profile) is not { } actions) return;
        foreach (var action in actions.Cast<JsonObject>().ToArray())
        {
            var actionKey = action["key"]!.GetValue<string>();
            if (actionKey == key || actionKey.StartsWith(key + ".", StringComparison.Ordinal)
                || includeAncestors && key.StartsWith(actionKey + ".", StringComparison.Ordinal)) actions.Remove(action);
        }
        if (actions.Count == 0)
        {
            if (profile is null) operations.Remove("defaults");
            else if (operations["profiles"] is JsonObject profiles)
            {
                profiles.Remove(profile);
                if (profiles.Count == 0) operations.Remove("profiles");
            }
        }
        if (operations.Count == 0) envelope.Remove("operations");
    }
}
