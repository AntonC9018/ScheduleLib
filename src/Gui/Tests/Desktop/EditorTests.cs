using Avalonia.Headless.XUnit;
using Desktop.NodeData.Common;
using Desktop.NodeData.Features.Registry;
using Desktop.ViewModelData;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.Tests;

public sealed class EditorTests
{
    [AvaloniaFact]
    public async Task RegistryEditorIsSelectedOnStartup()
    {
        await using var c = Context.Create();
        var editor = c.Main.NodeDataEditor;
        Assert.Equal(RegistryEditorRegistration.EditorKey, editor.CurrentConfigType?.Key);
        var generated = Assert.IsType<PropertySetViewModel>(editor.SelectedNodeEditorViewModel);
        Assert.IsType<CredentialsPropertyViewModel>(generated.Properties.Single(p => p.Id.Name == "Credentials").Editor);
        Assert.False(generated.IsEditable);
    }

    [AvaloniaFact]
    public async Task EnablingTeacherEditingUpdatesEditorAndAllowsResetToInheritedSettings()
    {
        await using var c = Context.Create();
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.ProgrammableUser;
        c.Main.NodeSelection.Model.SelectedNode = c.Main.NodeSelection.Model.AllNodes.First(x => !x.IsNull);
        var generated = Assert.IsType<PropertySetViewModel>(c.Main.NodeDataEditor.SelectedNodeEditorViewModel);
        Assert.False(generated.IsEditable);
        c.Main.EnableSelectedUser();
        Assert.True(generated.IsEditable);
        var dryRun = generated.Properties.Single(p => p.Id.Name == "DryRun");
        dryRun.BooleanValue = true;
        Assert.True(dryRun.BooleanValue);
        dryRun.BooleanValue = false;
        Assert.False(dryRun.BooleanValue);
        dryRun.BooleanValue = null;
        Assert.Null(dryRun.BooleanValue);

        var extra = generated.Properties.Single(p => p.Id.Name == "ExtraLessonInstanceAction");
        extra.SelectedChoice = extra.Choices.First(x => x.Name == "Delete");
        Assert.Equal(ExtraLessonInstanceAction.Delete, extra.Value);
        extra.SelectedChoice = extra.Choices.First(x => x.IsDefault);
        Assert.Null(extra.Value);

    }

    [AvaloniaFact]
    public void ReadingOrWritingReadOnlyCredentialsPreservesTheConfiguredSource()
    {
        var source = new AppConfigCredentialsSource();
        var config = new RegistryConfig { Credentials = source };
        using var credentials = new CredentialsPropertyViewModel();
        credentials.Update(new(config, source, false, _ => throw new InvalidOperationException("Read-only credentials must not be written.")));
        Assert.Equal("", credentials.Login);
        Assert.Equal("", credentials.Password);
        credentials.Login = "ignored";
        credentials.Password = "ignored";
        Assert.Same(source, config.Credentials);
    }

    [AvaloniaFact]
    public async Task MissingSavedLayersReportsAnEmptyState()
    {
        await using var c = Context.Create();
        c.SerializerTreeOutputProvider.IsReadAvailable = false;
        await c.Main.DeserializeUiLayers();
        Assert.False(c.Main.IsBusy);
        Assert.StartsWith("No saved UI layers found", c.Main.StatusMessage);
    }

    [AvaloniaFact]
    public async Task MalformedLoadReportsFailureAndDoesNotLeaveTheDispatcherQueueing()
    {
        await using var c = Context.Create();
        c.SerializerTreeOutputProvider.SetValue("invalid json"u8);
        await c.Main.DeserializeUiLayers();
        Assert.False(c.Main.IsBusy);
        Assert.StartsWith("Could not save or load", c.Main.StatusMessage);

        // A second tree action must still work after the failed async action.
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.ProgrammableUser;
        c.Main.NodeSelection.Model.SelectedNode = c.Main.NodeSelection.Model.AllNodes.First(x => !x.IsNull);
        c.Main.EnableSelectedUser();
        Assert.True(c.Main.CanRemoveSelectedUser);
    }
}
