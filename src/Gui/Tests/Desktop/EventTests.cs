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
        var recorder = new Recorder(selection);

        await main.SerializeUiLayers();
        recorder.Expect([]);

        var name = NameHelper.Parse("Curmanschii Anton");
        var meNode = main.UserNodeSelection.AllNodes.First(x => !x.IsNull && x.Name == name);
        main.UserNodeSelection.SelectedNode = meNode;
        recorder.Expect(
            e =>
            {
                Assert.Equal(Type.SelectedNodeChanged, e.Type);
                Assert.Same(meNode, e.Node);
            },
            e =>
            {
                Assert.Equal(Type.DataChanged, e.Type);
            });
    }

    private enum Type
    {
        SelectedNodeChanged,
        DataChanged,
    }

    private readonly record struct RecordedEvent(Type Type, WrappedNode Node);
    private sealed class Recorder
    {
        private readonly List<RecordedEvent> _recorded;

        public Recorder(ISelectedUserNode node)
        {
            _recorded = new();
            node.OnSelectedNodeChanged += n => _recorded.Add(new(Type.SelectedNodeChanged, n));
            node.OnSelectedNodeChanged += n => _recorded.Add(new(Type.DataChanged, n));
        }

        public void Expect(params Action<RecordedEvent>[] expectedEvents)
        {
            Assert.Collection(_recorded, expectedEvents);
            _recorded.Clear();
        }
    }
}
