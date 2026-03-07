using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;
using Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleLib.Parsing;

namespace Desktop.Tests;

public sealed class EventTests
{
    [Fact]
    public async Task LayerChangedTest()
    {
        await using var c = Context.Create();

        var layerSelection = c.Main.LayerLevelSelection;
        layerSelection.LayerLevel = LayerLevel.UiUser;

        c.RecorderSource
            .Expect(c.Tags.SelectedLevel)
            .SingleEvent()
            .WithValue(x => Assert.Equal(LayerLevel.UiUser, x));

        c.RecorderSource
            .Expect(c.Tags.SelectedLayer)
            .SingleEvent()
            .WithValue(x => Assert.Equal(UiLayerHelper.UiTeacherLayer, x));

        var mainEvents = c.RecorderSource.Expect(c.Tags.Main);
        mainEvents
            .WhereValue(x => x?.PropertyName == nameof(c.Main.CanSelectUser))
            .SingleEvent();

        Assert.True(c.Main.CanSelectUser);
    }

    private void SelectPerson(Context c)
    {
        var name = NameHelper.Parse("Curmanschii Anton");
        var nodeSelection = c.Main.NodeSelection;
        var nodeToSelect = nodeSelection.Model.AllNodes
            .Single(x => !x.IsNull && x.Name == name);
        nodeSelection.Model.SelectedNode = nodeToSelect;
    }

    [Fact]
    public async Task NodeChangedTest()
    {
        await using var c = Context.Create();

        c.RecorderSource.PauseRecording();
        c.Main.LayerLevelSelection.LayerLevel = LayerLevel.ProgrammableUser;
        c.RecorderSource.StartRecording();

        SelectPerson(c);

        c.RecorderSource
            .Expect(c.Tags.SelectedNode)
            .SingleEvent();

        c.RecorderSource
            .Expect(c.Tags.SelectedUiNode)
            .SingleEvent();

        c.RecorderSource
            .Expect(c.Tags.CanEnableSelectedUser)
            .AtLeastOne();

        Assert.True(c.Main.CanEnableSelectedUser);

        c.RecorderSource.ClearRecorded();

        c.Main.EnableSelectedUser();
        Assert.True(c.Main.LayerLevelSelection.LayerLevel == LayerLevel.UiUser);
        Assert.True(c.Data.SelectedNodePath.SelectedLayer.Get() == UiLayerHelper.UiTeacherLayer);

        c.RecorderSource
            .Expect(c.Tags.CanEnableSelectedUser)
            .AtLeastOne();

        c.RecorderSource
            .Expect(c.Tags.CanRemoveSelectedUser)
            .AtLeastOne();

        Assert.False(c.Main.CanEnableSelectedUser);
        Assert.True(c.Main.CanRemoveSelectedUser);
    }

    [Fact]
    public async Task ReloadWorks()
    {
        await using var c = Context.Create();

        c.RecorderSource.PauseRecording();

        var layerSelection = c.Main.LayerLevelSelection;
        layerSelection.LayerLevel = LayerLevel.UiUser;
        SelectPerson(c);
        c.Main.EnableSelectedUser();

        c.RecorderSource.StartRecording();

        c.SerializerTreeOutputProvider.SetValue("[]"u8);
        await c.Main.DeserializeUiLayers();

        c.RecorderSource
            .Expect(c.Tags.TreeChangedRecorder)
            .SingleEvent();

        Assert.Equal("Curmanschii Anton", c.Data.UiSelectedNodeViewModel.Value.Name.ToString());
    }
}

public sealed class MockUiTreeOutputProvider : IUiTreeOutputProvider, IDisposable
{
    private readonly MemoryStream _mem = new();

    public void SetValue(ReadOnlySpan<byte> str)
    {
        _mem.Seek(0, SeekOrigin.Begin);
        _mem.Write(str);
        _mem.SetLength(_mem.Position);
    }

    public Stream GetWrite(TreeBuilder tree)
    {
        _mem.Seek(0, SeekOrigin.Begin);
        return _mem;
    }

    public Stream GetRead(TreeBuilder tree)
    {
        return GetWrite(tree);
    }

    public void Dispose()
    {
        _mem.Dispose();
    }
}

public sealed class Context : IAsyncDisposable
{
    public ServiceProvider ServiceProvider { get; }
    public EventRecorderSource RecorderSource { get; }
    public MainWindowViewModel Main { get; }
    public AllRecorders Tags { get; }
    public DataStore Data => Main._dataStore;
    public MockUiTreeOutputProvider SerializerTreeOutputProvider { get; }

    public Context(
        ServiceProvider serviceProvider,
        EventRecorderSource recorderSource,
        MainWindowViewModel main,
        AllRecorders tags,
        MockUiTreeOutputProvider serializerTreeOutputProvider)
    {
        ServiceProvider = serviceProvider;
        RecorderSource = recorderSource;
        Main = main;
        Tags = tags;
        SerializerTreeOutputProvider = serializerTreeOutputProvider;
    }

    public async ValueTask DisposeAsync()
    {
        await ServiceProvider.DisposeAsync();
        Tags.Dispose();
    }

    public static Context Create()
    {
        var services = new ServiceCollection();
        AppConfiguration.ConfigureServices(services);
        App.AddViewModels(services);

        services.RemoveAll<IUiTreeOutputProvider>();
        services.AddSingleton<IUiTreeOutputProvider, MockUiTreeOutputProvider>();

        var sp = AppConfiguration.BuildServiceProvider(services);

        try
        {
            AppConfiguration.ConfigureLayeredConfig(sp);
            var recorderSource = new EventRecorderSource();
            var main = sp.GetRequiredService<MainWindowViewModel>();

    #pragma warning disable CA2000
            var tags = new AllRecorders(recorderSource, main);
            _ = tags;
    #pragma warning restore CA2000

            var t = (MockUiTreeOutputProvider) sp.GetRequiredService<IUiTreeOutputProvider>();

            return new(
                sp,
                recorderSource,
                main,
                tags,
                t);
        }
        catch
        {
            sp.Dispose();
            throw;
        }
    }
}

