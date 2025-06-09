using System.Runtime.CompilerServices;
using Argon;
using ScheduleLib;
using ScheduleLib.Builders;

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

    private sealed class TeacherNameConverter : JsonConverter<OptionalFirstNamePart>
    {
        public override void WriteJson(
            JsonWriter writer,
            OptionalFirstNamePart value,
            JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("Full");
            writer.WriteValue(value.Full);
            writer.WritePropertyName("Short");
            writer.WriteValue(value.Short);
            writer.WriteEndObject();
        }

        public override OptionalFirstNamePart ReadJson(
            JsonReader reader,
            Type type,
            OptionalFirstNamePart existingValue,
            bool hasExisting,
            JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }
    }
}
