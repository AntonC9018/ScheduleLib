// I'm sorry, I overengineered this.
// There's only 1 wrapper class currently, this is not needed.
using System.Diagnostics;
using Anton.LayeredData;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.ViewModels;

public static class VmBuilder
{
    extension (NodeDataVMCreateParams p)
    {
        private VmBuilderData<T> CreateBuilderData<T>(NodeDataKey<T> key)
            where T : class
        {
            return new(key, p);
        }

        public NodeDataViewModelResult BuildVm<T>(
            NodeDataKey<T> key,
            VmFactory<T> vmFactory)
            where T : class
        {
            var data = p.CreateBuilderData(key);
            try
            {
                vmFactory(new(ref data));
                var ret = data.Build();
                return ret;
            }
            catch
            {
                data.Dispose();
                throw;
            }
        }
    }
}

public struct VmBuilderData<T> : IDisposable
    where T : class
{
    public ConfigAccessor<T> Accessor { get; }
    public NodeDataKey<T> Key { get; }
    public DataStore DataStore { get; }
    public IServiceProvider ServiceProvider { get; }
    internal ObservableObject? OutermostObject { get; set; }

    public VmBuilderData(
        NodeDataKey<T> key,
        NodeDataVMCreateParams p)
    {
        Key = key;
        DataStore = p.DataStore;
        ServiceProvider = p.ServiceProvider;
        Accessor = new(
            p.DataStore.TreeBuilder,
            p.DataStore.SelectedNodePath,
            key);
    }

    public void Dispose()
    {
        if (OutermostObject is IDisposable d)
        {
            d.Dispose();
        }
    }

    public NodeDataViewModelResult Build()
    {
        // Move
        var vm = OutermostObject;
        OutermostObject = vm;

        var ret = new NodeDataViewModelResult((ObservableObject) vm!, (IDisposable) vm!);
        return ret;
    }
}

public readonly ref struct VmBuilder<T>
    where T : class
{
    public readonly ref VmBuilderData<T> Data;
    public VmBuilder(ref VmBuilderData<T> data)
    {
        Data = ref data;
    }
}

public delegate void VmFactory<T>(VmBuilder<T> builder) where T : class;
public delegate VmFactory<T> VmFactoryMiddleware<T>(VmFactory<T> next) where T : class;

public readonly struct VmFactoryBuilder<T>()
    where T : class
{
    internal readonly List<VmFactoryMiddleware<T>> Creators = new();

    public VmFactoryBuilder<T, TVM> VM<TVM>()
        where TVM : ObservableObject, IDisposable
    {
        VM(x =>
        {
            var ret = ActivatorUtilities.CreateInstance<RegistryConfigViewModel>(
                x.Data.ServiceProvider,
                x.Data.Accessor);
            return ret;
        });
        return new(this);
    }

    public VmFactoryBuilder<T, TVM> VM<TVM>(Func<VmBuilder<T>, TVM> factory)
        where TVM : ObservableObject, IDisposable
    {
        Debug.Assert(Creators.Count == 0);
        Creators.Add(next =>
        {
            _ = next;
            return x =>
            {
                Debug.Assert(x.Data.OutermostObject == null);
                var ret = factory(x);
                x.Data.OutermostObject = ret;
            };
        });
        return new(this);
    }

    public VmFactory<T> CreateFactory()
    {
        Debug.Assert(Creators.Count != 0);
        VmFactory<T>? last = null;
        foreach (var x in Creators)
        {
            last = x(last!);
            Debug.Assert(last != null);
        }
        return last!;
    }
}

public readonly ref struct VmBuilder<T, TVM>
    where T : class
    where TVM : ObservableObject, IDisposable
{
    public readonly VmBuilder<T> _x;
    public VmBuilder(VmBuilder<T> x) => _x = x;
    public TVM VM => (TVM) _x.Data.OutermostObject!;
    public ref VmBuilderData<T> Data => ref _x.Data;
}

public readonly struct VmFactoryBuilder<T, TVM>
    where T : class
    where TVM : ObservableObject, IDisposable
{
    private readonly VmFactoryBuilder<T> _builder;

    public VmFactoryBuilder(VmFactoryBuilder<T> builder)
    {
        _builder = builder;
    }

    public VmFactoryBuilder<T, TVM2> Use<TVM2>(Func<VmBuilder<T, TVM>, TVM2> factory)
        where TVM2 : ObservableObject, IDisposable
    {
        _builder.Creators.Add(next => x =>
        {
            next(x);
            var builder = new VmBuilder<T, TVM>(x);
            var ret = factory(builder);
            x.Data.OutermostObject = ret;
        });
        return new(_builder);
    }
}

public static class VmBuilderExtensions
{
    public static VmFactoryBuilder<T, ConfigNodeVmHost<T>> UseUpdateOnDataChange<T, TVM>(
        this VmFactoryBuilder<T, TVM> builder)

        where T : class
        where TVM : ObservableObject, IConfigViewModel<T>, IDisposable
    {
        var ret = builder.Use(b =>
        {
            var vm = b.VM;
            var host = new ConfigNodeVmHost<T>(
                b.Data.Accessor,
                vm,
                vm.UpdateSelection,
                b.Data.DataStore.NodeDataChangeDispatcher.DataChanged);
            return host;
        });
        return ret;
    }

#if false
    public static VmFactoryBuilder<T, ConfigNodeVmHost<T>> UseUpdateOnDataChange<T, TVM>(
        this VmFactoryBuilder<T, TVM> builder,
        Action<TVM, NodeDataBuilder<T>> onChange)

        where T : class
        where TVM : ObservableObject, IDisposable
    {
        var ret = builder.Use(b =>
        {
            var vm = b.VM;
            var host = new ConfigNodeVmHost<T>(
                b.Data.Accessor,
                vm,
                x => onChange(vm, x),
                b.Data.DataStore.NodeDataChangeDispatcher);
            return host;
        });
        return ret;
    }
#endif
}

public sealed class VmFactoryService<T> : INodeDataViewModelFactory
    where T : class
{
    private readonly VmFactory<T> _vmFactory;
    public NodeDataKey<T> Key { get; }

    public VmFactoryService(NodeDataKey<T> key, VmFactory<T> vmFactory)
    {
        Key = key;
        _vmFactory = vmFactory;
    }

    NodeDataKey INodeDataViewModelFactory.Key => Key.Value;

    public NodeDataViewModelResult Create(NodeDataVMCreateParams p)
    {
        var ret = p.BuildVm(Key, _vmFactory);
        return ret;
    }
}

public static class VmFactoryRegistration
{
    public static void AddVmFactory<T>(
        this IServiceCollection services,
        NodeDataKey<T> key,
        Action<VmFactoryBuilder<T>> configure) where T : class
    {
        var builder = new VmFactoryBuilder<T>();
        configure(builder);
        var factory = builder.CreateFactory();
        var instance = new VmFactoryService<T>(key, factory);
        services.AddSingleton<INodeDataViewModelFactory>(instance);
    }
}
