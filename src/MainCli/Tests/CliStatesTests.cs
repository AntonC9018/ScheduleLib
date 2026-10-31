using System.Text.Json;
using ScheduleLib.Cli;

public sealed class CliStatesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Theory]
    [InlineData(ActionState.Planned, "planned")]
    [InlineData(ActionState.Completed, "completed")]
    [InlineData(ActionState.Uncertain, "uncertain")]
    [InlineData(ActionState.Failed, "failed")]
    [InlineData(ActionState.NotAttempted, "not-attempted")]
    [InlineData(ActionState.Omitted, "omitted")]
    [InlineData(ActionState.Cancelled, "cancelled")]
    public void ActionStatesSerializeAsTheirWireNames(ActionState state, string wire)
    {
        Assert.Equal(expected: wire, actual: state.ToWire());
        Assert.Equal(expected: $"\"{wire}\"", actual: JsonSerializer.Serialize(state, JsonOptions));
    }

    [Theory]
    [InlineData(RunStatus.Succeeded, "succeeded")]
    [InlineData(RunStatus.Partial, "partial")]
    [InlineData(RunStatus.Failed, "failed")]
    [InlineData(RunStatus.Cancelled, "cancelled")]
    [InlineData(RunStatus.Preview, "preview")]
    [InlineData(RunStatus.Applied, "applied")]
    [InlineData(RunStatus.Generated, "generated")]
    [InlineData(RunStatus.Downloaded, "downloaded")]
    public void RunStatusesSerializeAsTheirWireNames(RunStatus status, string wire)
    {
        Assert.Equal(expected: wire, actual: status.ToWire());
        Assert.Equal(expected: $"\"{wire}\"", actual: JsonSerializer.Serialize(status, JsonOptions));
    }

    [Fact]
    public void StateDictionaryKeysUseWireNames()
    {
        var counts = new Dictionary<ActionState, int>
        {
            [ActionState.NotAttempted] = 2,
        };

        Assert.Equal(
            expected: """{"not-attempted":2}""",
            actual: JsonSerializer.Serialize(counts, JsonOptions));
    }

    [Fact]
    public void DriveActionsKeepTheirFlatWireShape()
    {
        DriveAction[] actions =
        [
            new DriveDeleteAction(name: "old.pdf", remoteFileId: "r1"),
            new DriveCreateAction(name: "new.ics", sourcePath: "/out/new.ics"),
            new DriveUpdateAction(name: "a.xlsx", remoteFileId: "r2", sourcePath: "/out/a.xlsx"),
        ];
        var outcome = new DriveOutcome(actions[0], ActionState.Completed, ResultFileId: "r1");

        Assert.Equal(
            expected:
            """[{"action":"delete","name":"old.pdf","fileId":"r1","localPath":null},{"action":"create","name":"new.ics","fileId":null,"localPath":"/out/new.ics"},{"action":"update","name":"a.xlsx","fileId":"r2","localPath":"/out/a.xlsx"}]""",
            actual: JsonSerializer.Serialize(actions, JsonOptions));
        object item = outcome;
        Assert.Equal(
            expected:
            """{"action":{"action":"delete","name":"old.pdf","fileId":"r1","localPath":null},"state":"completed","resultFileId":"r1"}""",
            actual: JsonSerializer.Serialize(item, JsonOptions));
    }
}
