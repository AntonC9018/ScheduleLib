using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.ViewModelData;

namespace Desktop.NodeData.Common;

public interface INodeDataViewModelFactory
{
    NodeDataKey Key { get; }
    NodeDataViewModelResult Create(NodeDataVMCreateParams p);
}

public readonly record struct NodeDataVMCreateParams(
    IServiceProvider ServiceProvider,
    DataStore DataStore);

public readonly record struct NodeDataViewModelResult(
    ObservableObject ViewModel,
    IDisposable Disposable) : IDisposable
{
    public static NodeDataViewModelResult Create<T>(T vm)
        where T : ObservableObject, IDisposable
    {
        return new(vm, vm);
    }

    public void Dispose() => Disposable.Dispose();
}

