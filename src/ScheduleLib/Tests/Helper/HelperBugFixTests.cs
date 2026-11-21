using System.Text;
using System.Text.Json;
using ScheduleLib.Helper;
using ScheduleLib.Helper.JsonConverters;

namespace ScheduleLib.Tests;

public sealed class HelperBugFixTests
{
    private sealed class SampleDto
    {
        public string? Name { get; set; }
    }

    private sealed class NoDefaultCtor(int value)
    {
        public int Value { get; } = value;
    }

    [Theory]
    [InlineData("file.txt", ".pdf", "file.pdf")]
    [InlineData("dir.v1/file", ".pdf", "dir.v1/file.pdf")]
    [InlineData("dir.v1\\file", ".pdf", "dir.v1\\file.pdf")]
    [InlineData("dir.v1/.gitignore", ".bak", "dir.v1/.gitignore.bak")]
    [InlineData(".gitignore", ".bak", ".gitignore.bak")]
    public void WithExtensionOnlyReplacesFileNameExtension(string input, string extension, string expected)
    {
        string actual = PathHelper.WithExtension(file: input, extension: extension);
        Assert.Equal(expected: expected, actual: actual);
    }

    [Fact]
    public void RemoveDiacriticsHandlesFormDExpansionWithoutOverflow()
    {
        string hangul = "\uAC00\uAC01\uAC02";
        string actual = DiacriticsHelper.RemoveDiacritics(hangul);
        Assert.Equal(expected: hangul, actual: actual);
    }

    [Fact]
    public void SingleValueOrArrayConverterRoundTripsSingleAndArrayWithoutRecursion()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new SingleValueOrArrayConverter());

        string json = JsonSerializer.Serialize<List<string>>(["a", "b"], options);
        Assert.Equal(expected: "[\"a\",\"b\"]", actual: json);

        var array = JsonSerializer.Deserialize<List<string>>("[\"a\",\"b\"]", options);
        Assert.Equal(expected: ["a", "b"], actual: array);

        var single = JsonSerializer.Deserialize<List<string>>("\"only\"", options);
        Assert.Equal(expected: ["only"], actual: single);
    }

    [Fact]
    public void OnlySerializeSettersFactoryRejectsBuiltInAndNonConstructibleTypes()
    {
        var factory = new OnlySerializeSettersForUserDefinedTypesConverterFactory();

        Assert.False(factory.CanConvert(typeof(DateTime)));
        Assert.False(factory.CanConvert(typeof(DateOnly)));
        Assert.False(factory.CanConvert(typeof(Guid)));
        Assert.False(factory.CanConvert(typeof(decimal)));
        Assert.False(factory.CanConvert(typeof(Uri)));
        Assert.False(factory.CanConvert(typeof(JsonElement)));
        Assert.False(factory.CanConvert(typeof(StringBuilder)));
        Assert.False(factory.CanConvert(typeof(NoDefaultCtor)));
        Assert.True(factory.CanConvert(typeof(SampleDto)));
    }

    [Fact]
    public void DateOnlyJsonConverterThrowsJsonExceptionOnInvalidInput()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new DateOnlyJsonConverter());

        Assert.Throws<UnexpectedJsonTokenException>(() =>
            JsonSerializer.Deserialize<DateOnly>("12345", options));
        Assert.Throws<InvalidJsonDateException>(() =>
            JsonSerializer.Deserialize<DateOnly>("\"not-a-date\"", options));
    }
}
