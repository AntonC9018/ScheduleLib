using System.Text.Json;
using ScheduleLib.Cli;
using ScheduleLib.OnlineRegistry;

public sealed class CliStatesTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    [Theory]
    [InlineData(RegistrySyncActionKind.Create, "create")]
    [InlineData(RegistrySyncActionKind.Update, "update")]
    [InlineData(RegistrySyncActionKind.Delete, "delete")]
    public void RegistryActionsKeepTheirWireNames(RegistrySyncActionKind kind, string wire)
    {
        var outcome = new RegistryActionOutcome(
            kind,
            Destination: "destination",
            Course: "course",
            Groups: "group",
            default,
            Topic: null,
            null,
            ActionState.Planned,
            Detail: null);
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(outcome, JsonOptions));
        Assert.Equal(
            expected: wire,
            actual: document.RootElement.GetProperty("kind")
                .GetString());
        Assert.Equal(expected: wire, actual: kind.ToLowercaseValue());
    }

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
        var expectedJson = $"\"{wire}\"";
        var serializedState = JsonSerializer.Serialize(state, JsonOptions);
        Assert.Equal(expected: expectedJson, actual: serializedState);
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
        var expectedJson = $"\"{wire}\"";
        var serializedStatus = JsonSerializer.Serialize(status, JsonOptions);
        Assert.Equal(expected: expectedJson, actual: serializedStatus);
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
            new DriveUpdateAction(
                name: "a.xlsx",
                remoteFileId: "r2",
                sourcePath: "/out/a.xlsx"),
        ];
        var outcome = new DriveOutcome(
            actions[0],
            ActionState.Completed,
            ResultFileId: "r1");

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
