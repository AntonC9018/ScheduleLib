using System.Text.Json;
using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public sealed class RentedConverterTests
{
    private enum Portion
    {
        First,
        Second,
    }

    private sealed class DisposableValue : IDisposable
    {
        public static int DisposeCount;

        public int Value { get; set; }

        public void Dispose()
        {
            DisposeCount++;
        }
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(OneForEach.RentedConverterFactory);
        return options;
    }

    [Fact]
    public void DisposesParsedElementsWhenReadingFails()
    {
        DisposableValue.DisposeCount = 0;
        string json = """{"First":{"Value":1},"Second":"not an object"}""";

        Assert.Throws<JsonException>(() =>
            JsonSerializer.Deserialize<
                RentedOneForEachEnumMemberArray<Portion, DisposableValue>>(
                json,
                CreateOptions()));

        Assert.Equal(expected: 1, actual: DisposableValue.DisposeCount);
    }
}
