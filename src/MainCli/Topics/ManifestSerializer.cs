using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using ScheduleLib;
using ScheduleLib.Helper.JsonConverters;
using ScheduleLib.JsonConverters;
using ScheduleLib.Parsing;

namespace MainCli.Topics;

public static class ManifestSerializer
{
    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new SingleValueWrapperConverterFactory());
        options.Converters.Add(new NameJsonConverter());
        options.Converters.Add(new JsonStringEnumConverter<LessonType>());
        options.AllowTrailingCommas = true;

        var textEncoder = new TextEncoderSettings();
        textEncoder.AllowRanges(
            UnicodeRanges.BasicLatin,
            UnicodeRanges.Latin1Supplement,
            UnicodeRanges.LatinExtendedA,
            UnicodeRanges.LatinExtendedB,
            UnicodeRanges.LatinExtendedC,
            UnicodeRanges.LatinExtendedD,
            UnicodeRanges.LatinExtendedE);
        options.Encoder = JavaScriptEncoder.Create(textEncoder);
        options.ReferenceHandler = null;
        options.WriteIndented = true;
        options.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        options.ReadCommentHandling = JsonCommentHandling.Skip;
        return options;
    }

    public static async Task<Manifest> Deserialize(
        Stream stream,
        CancellationToken cancellationToken,
        Name? teacherName = null)
    {
        var options = CreateSerializerOptions();
        var manifest = await JsonSerializer.DeserializeAsync<Manifest>(
            stream,
            options,
            cancellationToken);
        if (manifest is null)
        {
            throw new InvalidDataException("Could not deserialize manifest");
        }
        InitTeacher();
        return manifest;

        void InitTeacher()
        {
            if (manifest.Teacher == null)
            {
                if (teacherName is null)
                {
                    throw new InvalidOperationException("No teacher name found in manifest or provided");
                }
                manifest.Teacher = teacherName;
                return;
            }

            if (teacherName is null)
            {
                return;
            }

            if (!manifest.Teacher.Equals(teacherName))
            {
                throw new InvalidOperationException(
                    $"Read teacher name {manifest.Teacher}, expected name {teacherName}");
            }
        }
    }

    public static async Task<Manifest> Serialize(
        Stream stream,
        Manifest manifest,
        CancellationToken cancellationToken)
    {
        var options = CreateSerializerOptions();
        await JsonSerializer.SerializeAsync(
            stream,
            manifest,
            options,
            cancellationToken);
        return manifest;
    }
}

public sealed class Manifest
{
    internal Manifest()
    {
    }
    public Name Teacher { get; set; } = null!;
    public required List<Document> Documents { get; set; }
}

public sealed class Document
{
    public required string Path { get; set; }
    public required string? Course { get; set; }
    [JsonConverter(typeof(SingleValueOrArrayConverter))]
    public List<Faculty>? Faculty { get; set; }
    public LessonType? LessonType { get; set; }
    public string? Delimiter { get; set; }
    [JsonConverter(typeof(SingleValueOrArrayConverter))]
    public List<Grade>? Grade { get; set; }
}
