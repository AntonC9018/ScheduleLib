using System.Text.Json;

namespace ScheduleLib.Helper.JsonConverters;

public enum JsonTokenExpectation
{
    StartObject,
    PropertyName,
}

public sealed class UnexpectedJsonTokenException(JsonTokenType actualToken, JsonTokenExpectation expectation)
    : JsonException(CreateMessage(actualToken, expectation))
{
    public JsonTokenType ActualToken { get; } = actualToken;
    public JsonTokenExpectation Expectation { get; } = expectation;

    private static string CreateMessage(JsonTokenType actualToken, JsonTokenExpectation expectation)
    {
        return expectation switch
        {
            JsonTokenExpectation.StartObject => $"Expected StartObject token, got {actualToken}",
            JsonTokenExpectation.PropertyName => $"Unexpected token {actualToken}",
            _ => throw new ArgumentOutOfRangeException(nameof(expectation),
                expectation,
                null),
        };
    }
}
