using System.Runtime.CompilerServices;
using Argon;
using ScheduleLib;

namespace Curriculum.Tests;

internal static class VerifyInit
{
    [ModuleInitializer]
    public static void Init()
    {
        VerifierSettings.AddExtraSettings(settings =>
        {
            settings.Converters.Add(new ReadOnlyMemoryConverter());
            settings.Converters.Add(new TeacherNameConverter());
            settings.DefaultValueHandling = DefaultValueHandling.Include;
        });
    }

    private sealed class ReadOnlyMemoryConverter : JsonConverter<ReadOnlyMemory<char>>
    {
        public override void WriteJson(
            JsonWriter writer,
            ReadOnlyMemory<char> value,
            JsonSerializer serializer)
        {
            writer.WriteValue(value.Span);
        }

        public override ReadOnlyMemory<char> ReadJson(
            JsonReader reader,
            Type type,
            ReadOnlyMemory<char> existingValue,
            bool hasExisting,
            JsonSerializer serializer)
        {
            if (hasExisting)
            {
                return existingValue;
            }
            string? str = reader.ReadAsString();
            if (str is null)
            {
                throw new JsonException("Expected string");
            }
            return str.AsMemory();
        }
    }

    private sealed class TeacherNameConverter : JsonConverter<OptionalNamePart>
    {
        public override void WriteJson(
            JsonWriter writer,
            OptionalNamePart value,
            JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Full");
            writer.WriteValue(value.Full);
            writer.WritePropertyName("Short");
            writer.WriteValue(value.Short);
            writer.WriteEndObject();
        }

        public override OptionalNamePart ReadJson(
            JsonReader reader,
            Type type,
            OptionalNamePart existingValue,
            bool hasExisting,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
