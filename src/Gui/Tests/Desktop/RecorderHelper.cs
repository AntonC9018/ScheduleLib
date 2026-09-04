using System.Collections;
using System.ComponentModel;
using System.Windows.Input;
using Anton.LayeredData;
using Anton.LayeredData.TreeEnumeration;
using Desktop.MainWindow;
using Desktop.MvvmEssentials;
using Desktop.ViewModelData;

namespace Desktop.Tests;

public readonly record struct Ev(EventRecorder Recorder, object? Payload)
{
    public T? GetPayload<T>() => (T?) Payload;
}

public sealed class EventRecorderSource : IEnumerable<Ev>
{
    private readonly List<Ev> Events = new();
    private bool _isRecording = true;

    public bool StartRecording() => _isRecording = true;
    public bool PauseRecording() => _isRecording = false;
    public void ClearRecorded() => Events.Clear();

    public void AddEvent(Ev ev)
    {
        if (_isRecording)
        {
            Events.Add(ev);
        }
    }

    public EventRecorder<T> Record<T>(Event<T> e) => new(this, e);
    public PropertyChangedRecorder Record(INotifyPropertyChanged e) => new(e, this);
    public CanExecuteChangedRecorder Record(ICommand e) => new(e, this);

    public IEnumerator<Ev> GetEnumerator() => Events.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => Events.GetEnumerator();
}

public sealed class AllRecorders : IDisposable
{
    public EventRecorder<NodePath> NodePath { get; }
    public EventRecorder<Layer> SelectedLayer { get; }
    public EventRecorder<MutableNode?> SelectedNode { get; }
    public EventRecorder<LayerLevel> SelectedLevel { get; }
    public EventRecorder<UiNode> SelectedUiNode { get; }
    public PropertyChangedRecorder NodeSelected { get; }
    public EventRecorder<Nothing> NodeData { get; }
    public PropertyChangedRecorder Main { get; }
    public CanExecuteChangedRecorder CanEnableSelectedUser { get; }
    public CanExecuteChangedRecorder CanRemoveSelectedUser { get; }
    public EventRecorder<Nothing> TreeChangedRecorder { get; }

    public AllRecorders(EventRecorderSource s, MainWindowViewModel main)
    {
        var store = main._dataStore;
        NodePath = s.Record(store.SelectedNodePath.NodePath.Changed).SetName("NodePath");
        SelectedLayer = s.Record(store.SelectedNodePath.SelectedLayer.Changed).SetName("SelectedLayer");
        SelectedNode = s.Record(store.SelectedNodePath.SelectedNode.Changed).SetName("SelectedNode");
        SelectedUiNode = s.Record(store.UiSelectedNodeViewModel.NodeSelected()).SetName("UiSelectedNode");
        SelectedLevel = s.Record(main.LayerLevelSelection.LayerLevelChanged).SetName("LayerLevel");
        NodeSelected = s.Record(main.NodeSelection).SetName("NodeSelectionProperty");
        NodeData = s.Record(store.NodeDataChangeDispatcher.DataChanged.As()).SetName("NodeData");
        Main = s.Record(main).SetName("MainProperty");
        CanEnableSelectedUser = s.Record(main.EnableSelectedUserCommand).SetName("CanEnableSelectedUser");
        CanRemoveSelectedUser = s.Record(main.RemoveSelectedUserCommand).SetName("CanRemoveSelectedUser");
        TreeChangedRecorder = s.Record(store.TreeStructureChanged.As()).SetName("TreeStructureChanged");
    }

    public void Dispose()
    {
        TreeChangedRecorder.Dispose();
        NodePath.Dispose();
        SelectedLayer.Dispose();
        SelectedNode.Dispose();
        SelectedLevel.Dispose();
        SelectedUiNode.Dispose();
        NodeSelected.Dispose();
        NodeData.Dispose();
        Main.Dispose();
    }
}

public abstract class EventRecorder : IDisposable
{
    public string? Name { get; set; }
    public override string? ToString() => Name;
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);
    }
}

public abstract class EventRecorderBase<T> : EventRecorder
{
}

public static partial class RecorderHelper
{
    public static T SetName<T>(this T e, string name) where T : EventRecorder
    {
        e.Name = name;
        return e;
    }
}

public sealed class PropertyChangedRecorder : EventRecorderBase<PropertyChangedEventArgs>
{
    private readonly EventRecorderSource _s;
    private readonly INotifyPropertyChanged _x;

    public PropertyChangedRecorder(
        INotifyPropertyChanged x,
        EventRecorderSource s)
    {
        _x = x;
        _s = s;
        x.PropertyChanged += Update;
    }

    public void Update(object? o, PropertyChangedEventArgs? args)
    {
        _s.AddEvent(new(this, args));
    }

    public override void Dispose()
    {
        _x.PropertyChanged -= Update;
    }
}

public sealed class CanExecuteChangedRecorder : EventRecorderBase<EventArgs>
{
    private readonly EventRecorderSource _s;
    private readonly ICommand _x;

    public CanExecuteChangedRecorder(
        ICommand x,
        EventRecorderSource s)
    {
        _x = x;
        _s = s;
        x.CanExecuteChanged += Update;
    }

    public void Update(object? o, EventArgs? args)
    {
        _s.AddEvent(new(this, args));
    }

    public override void Dispose()
    {
        _x.CanExecuteChanged -= Update;
    }
}

public static partial class RecorderHelper
{
    public static void Expect(
        this Ev e,
        PropertyChangedRecorder rec,
        Action<PropertyChangedEventArgs?> a)
    {
        Assert.Same(rec, e.Recorder);
        a((PropertyChangedEventArgs?) e.Payload);
    }
}


public sealed partial class EventRecorder<T> : EventRecorderBase<T>
{
    private readonly EventSubscription<T> _eventSub;

    public EventRecorder(
        EventRecorderSource source,
        Event<T> e)
    {
        _eventSub = e.Sub(x => source.AddEvent(new(this, x)));
    }

    public override void Dispose()
    {
        _eventSub.Dispose();
    }
}

public readonly record struct ExpectHelper<T>(IEnumerable<Ev> Events)
{
    public void NotToBeFound()
    {
        Assert.Empty(Events);
    }

    public ExpectHelper<T> Where(Func<Ev, bool> f)
    {
        var t = Events.Where(f);
        return new(t);
    }

    public ExpectHelper<T> WhereValue(Func<T?, bool> f)
    {
        var t = Events
            .Select(x => (Ev: x, Payload: x.GetPayload<T>()))
            .Where(x => f(x.Payload))
            .Select(x => x.Ev);
        return new(t);
    }

    public ExpectHelperSingle<T> SingleEvent()
    {
        var v = Assert.Single(Events);
        return new(v);
    }

    // public ExpectHelperOne<T> OnlyOne() => new(Events, HelperOneMode.OnlyOne);
    public ExpectHelperOne<T> AtLeastOne()
    {
        Assert.NotEmpty(Events);
        return new(Events, HelperOneMode.AtLeastOne);
    }

    public ExpectHelperAll<T> All() => new(Events);
}

public readonly record struct ExpectHelperAll<T>(IEnumerable<Ev> Events)
{
    public void WithValue(Action<T?> a)
    {
        Assert.All(Events.Select(x => x.GetPayload<T>()), a);
    }

    public void WithValues(params Action<T?>[] a)
    {
        Action<Ev> Wrapped(Action<T?> f)
        {
            return x => f(x.GetPayload<T>());
        }
        Assert.Collection(Events, a.Select(Wrapped).ToArray());
    }
}

public enum HelperOneMode
{
    // Single,
    OnlyOne,
    AtLeastOne,
}

public readonly record struct ExpectHelperOne<T>(IEnumerable<Ev> Events, HelperOneMode Mode)
{
    public void WithValue(Func<T?, bool> a)
    {
        switch (Mode)
        {
            case HelperOneMode.OnlyOne:
            {
                Assert.Single(Events.Select(x => x.GetPayload<T>()), x => a(x));
                break;
            }
            case HelperOneMode.AtLeastOne:
            {
                Assert.Contains(Events.Select(x => x.GetPayload<T>()), x => a(x));
                break;
            }
        }
    }
}

public readonly record struct ExpectHelperSingle<T>(Ev Event)
{
    public void WithValue(Action<T?> a)
    {
        a(Event.GetPayload<T>());
    }
}

public static partial class RecorderHelper
{
    public static void Expect<T>(
        this Ev e,
        EventRecorder<T> rec,
        Action<T> a)
    {
        _ = rec;
        Assert.Same(rec, e.Recorder);
        a((T) e.Payload!);
    }

    // ?
    // public static IEnumerable<(Ev Event, T? Value)> Expect<T>(
    //     IEnumerable<Ev> s,
    //     EventRecorderBase<T> rec)
    // {
    // }

    public static ExpectHelper<T> Expect<T>(
        this EventRecorderSource s,
        EventRecorderBase<T> rec)
    {
        var x = s.Where(e => ReferenceEquals(e.Recorder, rec));
        return new(x);
    }
}
