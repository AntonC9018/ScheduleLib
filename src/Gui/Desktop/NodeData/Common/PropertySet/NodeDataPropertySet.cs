using System.ComponentModel;
using System.Linq.Expressions;
using Anton.LayeredData;
using Avalonia.Controls;
using Desktop.NodeData.Features.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.NodeData.Common;

public sealed class YYYPropertySetBuilder<T>
    where T : class
{
    public NodeDataKey<T> Key { get; }
    private readonly IServiceCollection _services;

    public YYYPropertySetBuilder(IServiceCollection services, NodeDataKey<T> key)
    {
        _services = services;
        Key = key;
    }

    public YYYPropertyBuilder<T, TProperty> Property<TProperty>(string name)
    {
        var id = new PropertyId(name);
        return Property<TProperty>(id);
    }

    public YYYPropertyBuilder<T, TProperty> Property<TProperty>(PropertyId id)
    {
        var model = new YYYPropertyModel
        {
            Id = id,
        };
        var ret = new YYYPropertyBuilder<T, TProperty>(new()
        {
            Model = model,
            NodeDataKey = Key.Value,
            Services = _services,
        });
        return ret;
    }
}

public sealed class YYYPropertySetBuilderDefault
{
    public readonly IServiceCollection Services;
    private readonly List<(PropertyFilter Filter, Delegate Func)> _propertiesConfigs = new();
    private readonly List<(ParentFilter Filter, Delegate Func)> _parentConfigs = new();

    public YYYPropertySetBuilderDefault(IServiceCollection services)
    {
        Services = services;
    }

    public void PropertiesWithType<TProperty>(
        Action<YYYPropertyBuilder2<TProperty, TProperty>> configure)
    {
        var filter = new PropertyFilter(typeof(TProperty));
        _propertiesConfigs.Add((filter, configure));
    }

    public void Parent<TParent>(
        Action<YYYPropertySetBuilder<TParent>> configure)
        where TParent : class
    {
        var filter = new ParentFilter(typeof(TParent));
        _parentConfigs.Add((filter, configure));
    }
}

public readonly record struct ParentFilter(Type Interface);
public readonly record struct PropertyFilter(Type Type);

public readonly record struct OptionalNullValue(object? Boxed)
{
    public bool IsUnspecified => Boxed is null;
}
public readonly record struct RegistryId(string? Name)
{
}

public readonly record struct OptionalViewId(Type? ViewType)
{
    public bool IsNull => ViewType == null;
}
public readonly record struct ViewId(Type ViewType)
{
    public static implicit operator OptionalViewId(ViewId id) => new(id.ViewType);
}
public readonly record struct ViewId<T>()
    where T : UserControl
{
}

public readonly record struct OptionalViewModelId(Type? ViewModelType)
{
    public bool IsNull => ViewModelType == null;
}
public readonly record struct ViewModelId(Type ViewModelType)
{
    public static implicit operator OptionalViewModelId(ViewModelId id) => new(id.ViewModelType);
}
public readonly record struct ViewModelId<T>(ViewModelId Id)
    where T : INotifyPropertyChanged
{
}


public sealed class YYYPropertyModel
{
    public required PropertyId Id { get; init; }
    public string? NewName { get; set; }
    public LambdaExpression? Using { get; set; }
    public Delegate? Get { get; set; }
    public Delegate? Set { get; set; }

    public OptionalNullValue NullValue { get; set; } = new(null);
    public RegistryKey? RegistryId { get; set; }
    public Delegate? RegistryConfig { get; set; }
    public OptionalViewId ViewId { get; set; }
    public OptionalViewModelId ViewModelId { get; set; }
}

public readonly struct YYYPropertySSS()
{
    public required YYYPropertyModel Model { get; init; }
    public required NodeDataKey NodeDataKey { get; init; }
    public required IServiceCollection Services { get; init; }
}

// public interface IYYYPropertyBuilderCommon
// {
//     public YYYPropertyModelCommon CommonModel { get; }
// }
//
public interface IYYYPropertyBuilder // : IYYYPropertyBuilderCommon
{
    public YYYPropertySSS State { get; }
}

public sealed class YYYPropertyBuilder<TParent, TProperty> : IYYYPropertyBuilder
{
    public YYYPropertySSS State { get; }

    public YYYPropertyBuilder(YYYPropertySSS state)
    {
        State = state;
    }
}

public sealed class YYYPropertyBuilder2<TSource, TDerived> : IYYYPropertyBuilder
{
    public YYYPropertySSS State { get; }

    public YYYPropertyBuilder2(YYYPropertySSS state)
    {
        State = state;
    }
}

public static class YYYExtensions
{
    extension (IServiceCollection services)
    {
        public void ConfigurePropertySet<T>(
            NodeDataKey<T> key,
            Action<YYYPropertySetBuilder<T>> configure)
            where T : class
        {
            var builder = new YYYPropertySetBuilder<T>(services, key);
            configure(builder);
        }

        public void ConfigurePropertySetDefaults(
            Action<YYYPropertySetBuilderDefault> configure)
        {
            var builder = new YYYPropertySetBuilderDefault(services);
            configure(builder);
        }
    }

    extension<TParent>(YYYPropertySetBuilder<TParent> builder)
        where TParent : class
    {
        public YYYPropertyBuilder2<TProperty, TProperty> Property<TProperty>(
            Expression<Func<TParent, TProperty>> access)
        {
            var id = PropertySet.GetPropertyId(access);
            var ret = builder.Property<TProperty>(id);
            return new(ret.State);
        }
    }

    extension<TParent, TDerived>(YYYPropertyBuilder<TParent, TDerived> builder)
        where TParent : class
    {
        public YYYPropertyBuilder2<TSource, TDerived> Uses<TSource>(
            Expression<Func<TParent, TSource>> access)
        {
            builder.Model.Using = access;
            var ret = new YYYPropertyBuilder2<TSource, TDerived>(builder.State);
            return ret;
        }
    }

    extension<TBuilder>(TBuilder builder) where TBuilder : IYYYPropertyBuilder
    {
        public YYYPropertyModel Model => builder.State.Model;
        public IServiceCollection Services => builder.State.Services;
        public NodeDataKey NodeDataKey => builder.State.NodeDataKey;

        public TBuilder Rename(string newName)
        {
            builder.Model.NewName = newName;
            return builder;
        }

        public TBuilder UseVm(ViewModelId id)
        {
            builder.Model.ViewModelId = id;
            return builder;
        }

        public TBuilder UseView(ViewId id)
        {
            builder.Model.ViewId = id;
            return builder;
        }
    }

    extension<TSource, TDerived>(YYYPropertyBuilder2<TSource, TDerived> builder)
    {
        public YYYPropertyBuilder2<TSource, TDerived> Get(
            Func<TSource, TDerived> getter)
        {
            builder.Model.Get = getter;
            return builder;
        }

        public YYYPropertyBuilder2<TSource, TDerived> Set(
            Func<TSource, TDerived, TSource> setter)
        {
            builder.Model.Set = setter;
            return builder;
        }

        public YYYPropertyBuilder2<TSource, TDerived> UseDefaultRegistry()
        {
            builder.Services.AddRegistry<TDerived>();
            builder.Model.RegistryId = default(RegistryKey);
            return builder;
        }
    }

    extension<TSource, TDerived>(YYYPropertyBuilder2<TSource, TDerived> builder)
        where TDerived : class
    {
        public YYYPropertyBuilder2<TSource, TDerived> UseRegistry(
            Action<ThingRegistryOptions<TDerived>>? configure = null)
        {
            var key = new RegistryKey<TDerived>(new(builder.NodeDataKey, builder.Model.Id));
            builder.Services.AddRegistry(key, configure);
            return builder;
        }
    }
}

public static class YYYStructExtensions
{
    extension<TSource, TDerived>(YYYPropertyBuilder2<TSource, TDerived> builder)
        where TDerived : struct
    {
        public YYYPropertyBuilder2<TSource, TDerived> UseRegistry(
            Action<ThingRegistryOptions<TDerived>>? configure = null)
        {
            var key = new RegistryKey<TDerived>(new(builder.NodeDataKey, builder.Model.Id));
            builder.Services.AddRegistry(key, configure);
            return builder;
        }

        // public YYYPropertyBuilder2<TSource, TDerived> UseDefaultRegistry()
        // {
        //     builder.Services.AddRegistry<TDerived>();
        //     builder.Model.RegistryId = default(RegistryKey);
        //     return builder;
        // }
    }
}

public static class YYYNullableStructExtensions
{
    extension<TSource, TDerived>(YYYPropertyBuilder2<TSource?, TDerived> builder)
        where TSource : struct
    {
        public YYYPropertyBuilder2<TSource?, TDerived> GetUnwrap(
            Func<TSource, TDerived> getter)
        {
            builder.Model.Get = (TSource? source) =>
            {
                if (source is not { } val)
                {
                    return default;
                }
                var ret = getter(val);
                return ret;
            };
            return builder;
        }

        public YYYPropertyBuilder2<TSource?, TDerived> SetUnwrap(
            Func<TSource, TDerived, TSource?> setter)
        {
            builder.Model.Set = (TSource? source, TDerived value) =>
            {
                if (source is not { } val)
                {
                    return null;
                }
                var ret = setter(val, value);
                return ret;
            };
            return builder;
        }
    }

    extension<TSource, TDerived>(YYYPropertyBuilder2<TSource, TDerived?> builder)
        where TDerived : struct
    {
        public YYYPropertyBuilder2<TSource, TDerived?> UseRegistry(
            Action<ThingRegistryOptions<TDerived>>? configure = null)
        {
            var key = new RegistryKey<TDerived>(new(builder.NodeDataKey, builder.Model.Id));
            builder.Services.AddRegistry(key, configure);
            return builder;
        }

        public YYYPropertyBuilder2<TSource, TDerived?> UseDefaultRegistry()
        {
            builder.Services.AddRegistry<TDerived>();
            builder.Model.RegistryId = default(RegistryKey);
            return builder;
        }
    }
}
