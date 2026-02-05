using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Anton.LayeredData;

internal sealed class ConfigureSerializationOptions : IConfigureNamedOptions<JsonSerializerOptions>
{
    private readonly OpenHierarchyOptions _hierarchy;

    public ConfigureSerializationOptions(IOptions<OpenHierarchyOptions> hierarchy)
    {
        _hierarchy = hierarchy.Value;
    }

    public void Configure(string? name, JsonSerializerOptions options)
    {
        if (name != ConfigSerializationHelper.ServiceKey)
        {
            return;
        }
        options.TypeInfoResolver = new PolymorphicResolver(_hierarchy);
        options.Converters.Add(new JsonStringEnumConverter());
    }

    public void Configure(JsonSerializerOptions options)
    {
    }
}

internal sealed class PolymorphicResolver : DefaultJsonTypeInfoResolver
{
    private readonly OpenHierarchyOptions _hierarhy;

    public PolymorphicResolver(OpenHierarchyOptions hierarhy)
    {
        _hierarhy = hierarhy;
    }

    public override JsonTypeInfo GetTypeInfo(Type type, JsonSerializerOptions options)
    {
        var typeInfo = base.GetTypeInfo(type, options);
        if (!_hierarhy.HierarchyRootToDerived.TryGetValue(type, out var derived))
        {
            return typeInfo;
        }

        var polymorphicOptions = new JsonPolymorphismOptions
        {
            TypeDiscriminatorPropertyName = "$type",
            IgnoreUnrecognizedTypeDiscriminators = false,
        };
        {
            var derivedTypes = polymorphicOptions.DerivedTypes;
            foreach (var dt in derived)
            {
                var t = new JsonDerivedType(dt, dt.Name);
                derivedTypes.Add(t);
            }
        }
        typeInfo.PolymorphismOptions = polymorphicOptions;
        return typeInfo;
    }

}

public sealed class ConfigSerializationHelper
{
    public const string ServiceKey = "LayeredConfig";

    private readonly JsonSerializerOptions _jsonSerializerOptions;

    public ConfigSerializationHelper(
        IOptionsMonitor<JsonSerializerOptions> jsonSerializerOptions)
    {
        _jsonSerializerOptions = jsonSerializerOptions.Get(ServiceKey);
    }

    private const string LayerNameName = "$LayerName";

    public async Task SerializeValues(
        IEnumerable<MutableNode> layers,
        Stream output,
        bool serializeLayerName = true)
    {
        await using var writer = new Utf8JsonWriter(
            output,
            _jsonSerializerOptions.ToWriterOptions());
        writer.WriteStartArray();
        foreach (var layer in layers)
        {
            writer.WriteStartObject();
            if (serializeLayerName)
            {
                writer.WritePropertyName(LayerNameName);
                writer.WriteStringValue(layer.Layer.Value);
            }
            foreach (var config in layer.Configs)
            {
                var helper = config.Container;
                var value = helper.GetValue();
                if (value is null)
                {
                    continue;
                }

                writer.WritePropertyName(config.Key.Value);
                JsonSerializer.Serialize(writer, value, _jsonSerializerOptions);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        await writer.FlushAsync();
    }

    public readonly record struct PendingConfig(NodeDataKey Key, object Value);
    public sealed class PendingConfigs : List<PendingConfig>
    {
    }

    public async Task DeserializeValues(
        Stream input,
        Func<Layer?, PendingConfigs, NodeBuilder> findLayer)
    {
        using var ms = new MemoryStream();
        await input.CopyToAsync(ms);
        var buffer = ms.GetBuffer();
        var reader = new Utf8JsonReader(buffer.AsSpan(0, (int)ms.Length));

        reader.Read();
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndArray)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.StartObject)
            {
                throw new JsonException("Expected start of layer object.");
            }

            Layer? layerName = null;
            var pendingProperties = new PendingConfigs();

            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndObject)
                {
                    break;
                }

                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    throw new JsonException("Expected property name.");
                }

                string propertyName = reader.GetString()!;

                if (propertyName == LayerNameName)
                {
                    reader.Read();
                    layerName = new Layer(reader.GetString()!);
                    continue;
                }

                reader.Read();
                var key = new NodeDataKey(propertyName);
                var type = NodeDataKey.Registry.TryGetTypeFromKey(key);
                if (type == null)
                {
                    throw new JsonException($"Invalid config key: {key.Value}");
                }

                var value = JsonSerializer.Deserialize(ref reader, type, _jsonSerializerOptions);
                if (value == null)
                {
                    continue;
                }

                pendingProperties.Add(new(key, value));
            }

            var layer = findLayer(layerName, pendingProperties);
            foreach (var (key, value) in pendingProperties)
            {
                var type = NodeDataKey.Registry.GetTypeFromKey(key);
                var method = _setValueGenericMethod.MakeGenericMethod(type);
                var deleg = method.CreateDelegate<Action<SetValueArgs>>();
                deleg(new(layer, key, value));
            }
        }
    }

    private readonly record struct SetValueArgs(NodeBuilder Layer, NodeDataKey Key, object Value);
    private static readonly MethodInfo _setValueGenericMethod = typeof(ConfigSerializationHelper)
        .GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(x => x.Name == nameof(SetValue));
    private static void SetValue<T>(SetValueArgs args)
        where T : class
    {
        var conf = args.Layer.Node.GetOrAdd<T>(new(args.Key));
        var value = conf.GetValue();
        if (value == null)
        {
            conf.SetValue((T) args.Value);
            return;
        }
        var merger = args.Layer.SingletonServiceProvider.GetRequiredService<IMerger<T>>();
        var ret = merger.Merge(from: (T) args.Value, into: value);
        conf.SetValue(ret);
    }
}

public static class SerializationExtensions
{
    public static void ConfigureConfigJsonSerialization(
        this IServiceCollection services,
        Action<JsonSerializerOptions> configure)
    {
        services.Configure(ConfigSerializationHelper.ServiceKey, configure);
    }
}

public static class JsonSerializationOptionsExtensions
{
    public static JsonWriterOptions ToWriterOptions(this JsonSerializerOptions opts)
    {
        return new()
        {
            Encoder = opts.Encoder,
            IndentCharacter = opts.IndentCharacter,
            Indented = opts.WriteIndented,
            IndentSize = opts.IndentSize,
            MaxDepth = opts.MaxDepth,
            NewLine = opts.NewLine,
        };
    }
}
