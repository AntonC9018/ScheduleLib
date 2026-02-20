using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Helper;

public sealed class SameGenericArgsConverterFactory : JsonConverterFactory
{
    private readonly Type _genericConverterType;
    private readonly Type _genericType;

    public SameGenericArgsConverterFactory(
        Type objectType,
        Type converterType)
    {
        _genericConverterType = converterType;
        _genericType = objectType;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        if (!typeToConvert.IsGenericType)
        {
            return false;
        }
        var t = typeToConvert.GetGenericTypeDefinition();
        if (t != _genericType)
        {
            return false;
        }
        return true;
    }

    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        Debug.Assert(CanConvert(typeToConvert));
        var args = typeToConvert.GetGenericArguments();
        var retType = _genericConverterType.MakeGenericType(args);
        return (JsonConverter) Activator.CreateInstance(retType)!;
    }
}
