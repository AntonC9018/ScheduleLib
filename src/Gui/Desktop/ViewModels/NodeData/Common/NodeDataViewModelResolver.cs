using System.Collections.Frozen;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.ViewModels;

public readonly struct OwnedViewModel : IDisposable
{
    private readonly IServiceScope _scope;
    private readonly NodeDataViewModelResult _value;

    public ObservableObject Value => _value.ViewModel;

    public bool IsNull => Value == null;

    public OwnedViewModel(IServiceScope scope, NodeDataViewModelResult value)
    {
        _scope = scope;
        _value = value;
    }

    public void Dispose()
    {
        _value.Dispose();
        _scope.Dispose();
    }
}

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

public interface INodeDataViewModelFactory
{
    NodeDataKey Key { get; }
    NodeDataViewModelResult Create(
        IServiceProvider sp,
        ISelectedUserNode selectedUserNode);
}

public sealed class NodeDataViewModelResolver
{
    public FrozenSet<NodeDataKey> Supported { get; }
    private readonly FrozenDictionary<NodeDataKey, INodeDataViewModelFactory> _factories;
    private readonly IServiceProvider _sp;

    public NodeDataViewModelResolver(
        IEnumerable<INodeDataViewModelFactory> factories,
        IServiceProvider sp)
    {
        _sp = sp;
        _factories = factories.ToFrozenDictionary(x => x.Key, x => x);
        Supported = _factories.Keys.ToFrozenSet();
    }

    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<NodeDataViewModelResolver>();
    }

    public OwnedViewModel Resolve(
        NodeDataKey key,
        ISelectedUserNode selectedUser)
    {
        if (!_factories.TryGetValue(key, out var factory))
        {
            throw new ArgumentException(nameof(key));
        }

        var scope = _sp.CreateScope();

        try
        {
            var ret = factory.Create(scope.ServiceProvider, selectedUser);
            return new(scope, ret);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
