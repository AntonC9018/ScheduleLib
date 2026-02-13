using System.Collections.Frozen;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.ViewModels;

public readonly struct OwnedViewModel : IDisposable, IEquatable<OwnedViewModel>
{
    private readonly IServiceScope _scope;
    public ObservableObject Value { get; }

    public OwnedViewModel(IServiceScope scope, ObservableObject value)
    {
        _scope = scope;
        Value = value;
    }

    public void Dispose() => _scope.Dispose();

    public bool Equals(OwnedViewModel other)
    {
        if (ReferenceEquals(other.Value, this.Value))
        {
            return true;
        }
        return false;
    }
}

public interface INodeDataViewModelFactory
{
    NodeDataKey Key { get; }
    ObservableObject Create(
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
