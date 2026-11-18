using System.Text.Json;

namespace ScheduleLib.Helper.JsonConverters;

public enum JsonMemberProblem
{
    /// <summary>A non-nullable member was null.</summary>
    Null,
    /// <summary>A collection of non-nullable elements contained null.</summary>
    NullElement,
    /// <summary>A required member was not supplied.</summary>
    Missing,
}

/// <summary>A JSON object member that is null, contains null or is missing against its declaration.</summary>
public sealed class InvalidJsonMemberException(Type owner, string member, JsonMemberProblem problem)
    : JsonException(CreateMessage(owner, member, problem))
{
    public Type Owner { get; } = owner;
    public string Member { get; } = member;
    public JsonMemberProblem Problem { get; } = problem;

    private static string CreateMessage(Type owner, string member, JsonMemberProblem problem)
    {
        var requirement = problem switch
        {
            JsonMemberProblem.Null => "must not be null",
            JsonMemberProblem.NullElement => "must not contain null",
            JsonMemberProblem.Missing => "is required",
            _ => throw new ArgumentOutOfRangeException(nameof(problem), problem, null),
        };
        return $"{owner.Name}.{member} {requirement}";
    }
}
