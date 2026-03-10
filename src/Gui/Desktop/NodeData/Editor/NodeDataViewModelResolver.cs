using System.Collections.Frozen;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using Desktop.ViewModelData;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.NodeData.Editor;

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
        DataStore dataStore)
    {
        if (!_factories.TryGetValue(key, out var factory))
        {
            throw new ArgumentException(nameof(key));
        }

        var scope = _sp.CreateScope();

        try
        {
            var ret = factory.Create(new(scope.ServiceProvider, dataStore));
            return new(scope, ret);
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }
}
