using System.ComponentModel;
using Desktop.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

namespace Desktop.Tests;

public sealed class EventTests
{
    [Fact]
    public async Task NodeEventTests()
    {
        var services = new ServiceCollection();
        AppConfiguration.ConfigureServices(services);
        App.AddViewModels(services);
        var sp = AppConfiguration.BuildServiceProvider(services);
        AppConfiguration.ConfigureLayeredConfig(sp);

        var main = sp.GetRequiredService<MainWindowViewModel>();
        var selection = main._nodeSelection;
        var recorder = new NodeRecorder(selection);

        await main.SerializeUiLayers();
        recorder.Expect([]);

        var name = NameHelper.Parse("Curmanschii Anton");
        var meNode = main.UserNodeSelection.AllNodes.First(x => !x.IsNull && x.Name == name);
        main.UserNodeSelection.SelectedNode = meNode;
        recorder.Expect([
            e =>
            {
                Assert.Equal(NodeRecorder.EventType.SelectedNodeChanged, e.Type);
                Assert.Same(meNode, e.Node);
            },
            e =>
            {
                Assert.Equal(NodeRecorder.EventType.DataChanged, e.Type);
            },
        ]);

        main.EnableSelectedUser();
        recorder.Expect([
            e => Assert.Equal(NodeRecorder.EventType.SelectedNodeChanged, e.Type),
            e => Assert.Equal(NodeRecorder.EventType.DataChanged, e.Type),
        ]);


        var editor = main.NodeDataEditor;
        editor.CurrentConfigType = editor.ConfigTypes.First(x => x.Key == RegistryConfig.Key);
        var host = Assert.IsType<ConfigNodeVmHost<RegistryConfig>>(editor.SelectedNodeEditorViewModel);
        var regEditor = Assert.IsType<RegistryConfigViewModel>(host.Inner);
        var regRecorder = new NotifyPropChangedRecorder(regEditor);
        regEditor.DryRun = true;
        // Currently, this doesn't fire anything, but it might change.
        recorder.Expect([]);
        // This too
        regRecorder.Expect([]);

        await main.SerializeUiLayers();
        recorder.Expect([
            // e =>
            // {
            //     Assert.Equal(NodeRecorder.EventType.SelectedNodeChanged, e.Type);
            //     Assert.Same(meNode, e.Node);
            // },
            // e =>
            // {
            //     Assert.Equal(NodeRecorder.EventType.DataChanged, e.Type);
            // },
        ]);
        // Chains this.
        regRecorder.Expect([
            // e => Assert.Null(e.PropName),
        ]);

        regEditor.DryRun = false;
        // Currently only firing for whole external updates.
        regRecorder.Expect([]);

        await main.DeserializeUiLayers();
        recorder.Expect([
            e => Assert.Equal(NodeRecorder.EventType.DataChanged, e.Type),
        ]);
        regRecorder.Expect([
            e => Assert.Null(e.PropName),
        ]);

        Assert.True(regEditor.DryRun);
    }


    private sealed class NodeRecorder
    {
        public enum EventType
        {
            SelectedNodeChanged,
            DataChanged,
        }
        public readonly record struct RecordedEvent(EventType Type, WrappedNode Node);
        private readonly List<RecordedEvent> _recorded;

        public NodeRecorder(ISelectedUserNode node)
        {
            _recorded = new();
            node.OnSelectedNodeChanged +=
                n => _recorded.Add(new(EventType.SelectedNodeChanged, n));
            node.OnDataPossiblyChanged +=
                n => _recorded.Add(new(EventType.DataChanged, n));
        }

        public void Expect(Action<RecordedEvent>[] expectedEvents)
        {
            Assert.Collection(_recorded, expectedEvents);
            _recorded.Clear();
        }
    }

    private sealed class NotifyPropChangedRecorder
    {
        public readonly record struct RecordedEvent(string? PropName);
        private readonly List<RecordedEvent> _recorded;

        public NotifyPropChangedRecorder(INotifyPropertyChanged node)
        {
            _recorded = new();
            node.PropertyChanged += (_, args) => _recorded.Add(new(args.PropertyName));
        }

        public void Expect(Action<RecordedEvent>[] expectedEvents)
        {
            Assert.Collection(_recorded, expectedEvents);
            _recorded.Clear();
        }
    }
}
