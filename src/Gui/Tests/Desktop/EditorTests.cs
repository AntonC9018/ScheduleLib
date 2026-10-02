using Desktop.NodeData.Common;
using Desktop.NodeData.Features.Registry;
using Desktop.ViewModelData;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common.Config;

namespace Desktop.Tests;

public sealed class EditorTests
{
    [Fact]
    public async Task RegistryEditorIsSelectedOnStartup()
    {
        await using var c = Context.Create();
        var editor = c.Main.NodeDataEditor;
        Assert.Equal(RegistryEditorFactory.EditorKey, editor.CurrentConfigType?.Key);
        var host = Assert.IsAssignableFrom<INodeDataVmHost>(editor.SelectedNodeEditorViewModel);
        Assert.IsType<RegistryConfigViewModel>(host.Inner);
        Assert.False(host.IsEditable);
    }

    [Fact]
    public async Task EnablingTeacherEditingUpdatesEditorAndAllowsResetToInheritedSettings()
    {
        await using var c = Context.Create();
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.ProgrammableUser;
        c.Main.NodeSelection.Model.SelectedNode = c.Main.NodeSelection.Model.AllNodes.First(x => !x.IsNull);
        var host = Assert.IsAssignableFrom<INodeDataVmHost>(c.Main.NodeDataEditor.SelectedNodeEditorViewModel);
        Assert.False(host.IsEditable);
        c.Main.EnableSelectedUser();
        Assert.True(host.IsEditable);
        var registry = Assert.IsType<RegistryConfigViewModel>(host.Inner);
        registry.DryRun = true;
        Assert.True(registry.DryRun);
        registry.DryRun = false;
        Assert.False(registry.DryRun);
        registry.DryRun = null;
        Assert.Null(registry.DryRun);

        registry.ExtraLesson.Value = registry.ExtraLesson.Values.First(x => x.Name == "Delete");
        Assert.Equal(ExtraLessonInstanceAction.Delete, registry.ExtraLesson.Value?.Value);
        registry.ExtraLesson.Value = Named<ExtraLessonInstanceAction>.Default;
        Assert.Same(Named<ExtraLessonInstanceAction>.Default, registry.ExtraLesson.Value);
    }

    [Fact]
    public void ReadingOrWritingReadOnlyCredentialsPreservesTheConfiguredSource()
    {
        var source = new AppConfigCredentialsSource();
        var config = new RegistryConfig { Credentials = source };
        var credentials = new ObservableCredentials<RegistryConfig>();
        credentials.Set(new(false, config));
        Assert.Equal("", credentials.Login);
        Assert.Equal("", credentials.Password);
        credentials.Login = "ignored";
        credentials.Password = "ignored";
        Assert.Same(source, config.Credentials);
    }

    [Fact]
    public async Task MissingSavedLayersReportsAnEmptyState()
    {
        await using var c = Context.Create();
        c.SerializerTreeOutputProvider.IsReadAvailable = false;
        await c.Main.DeserializeUiLayers();
        Assert.False(c.Main.IsBusy);
        Assert.StartsWith("No saved UI layers found", c.Main.StatusMessage);
    }

    [Fact]
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
