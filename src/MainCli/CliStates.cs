using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScheduleLib.Cli;

/// <summary>The state of one streamed item or remote action.</summary>
[JsonConverter(typeof(ActionStateJsonConverter))]
public enum ActionState
{
    Planned,
    Completed,
    Uncertain,
    Failed,
    NotAttempted,
    Omitted,
    Cancelled,
}

/// <summary>The status of a whole invocation, reported in final records and manifests.</summary>
[JsonConverter(typeof(RunStatusJsonConverter))]
public enum RunStatus
{
    Succeeded,
    Partial,
    Failed,
    Cancelled,
    Preview,
    Applied,
    Generated,
    Downloaded,
}

/// <summary>The local authorization state of one stored provider account.</summary>
[JsonConverter(typeof(AuthAccountStateJsonConverter))]
public enum AuthAccountState
{
    Authorized,
    Refreshable,
    Expired,
}

/// <summary>Maps the CLI state enums to their stable wire strings.</summary>
public static class CliWireNames
{
    public static string ToWire(this ActionState state)
    {
        return state switch
        {
            ActionState.Planned => "planned",
            ActionState.Completed => "completed",
            ActionState.Uncertain => "uncertain",
            ActionState.Failed => "failed",
            ActionState.NotAttempted => "not-attempted",
            ActionState.Omitted => "omitted",
            ActionState.Cancelled => "cancelled",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }

    public static string ToWire(this RunStatus status)
    {
        return status switch
        {
            RunStatus.Succeeded => "succeeded",
            RunStatus.Partial => "partial",
            RunStatus.Failed => "failed",
            RunStatus.Cancelled => "cancelled",
            RunStatus.Preview => "preview",
            RunStatus.Applied => "applied",
            RunStatus.Generated => "generated",
            RunStatus.Downloaded => "downloaded",
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
        };
    }

    public static string ToWire(this AuthAccountState state)
    {
        return state switch
        {
            AuthAccountState.Authorized => "authorized",
            AuthAccountState.Refreshable => "refreshable",
            AuthAccountState.Expired => "expired",
            _ => throw new ArgumentOutOfRangeException(nameof(state), state, null),
        };
    }
}

/// <summary>Writes an enum as its wire string, also as a dictionary key. Reading is unsupported.</summary>
public abstract class WireNameJsonConverter<T> : JsonConverter<T> where T : struct, Enum
{
    protected abstract string ToWire(T value);

    public override T Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
        => throw new NotSupportedException($"{typeof(T).Name} is written to output only.");

    public override void Write(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
        => writer.WriteStringValue(ToWire(value));

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        T value,
        JsonSerializerOptions options)
        => writer.WritePropertyName(ToWire(value));
}

public sealed class ActionStateJsonConverter : WireNameJsonConverter<ActionState>
{
    protected override string ToWire(ActionState value) => value.ToWire();
}

public sealed class RunStatusJsonConverter : WireNameJsonConverter<RunStatus>
{
    protected override string ToWire(RunStatus value) => value.ToWire();
}

public sealed class AuthAccountStateJsonConverter : WireNameJsonConverter<AuthAccountState>
{
    protected override string ToWire(AuthAccountState value) => value.ToWire();
}
