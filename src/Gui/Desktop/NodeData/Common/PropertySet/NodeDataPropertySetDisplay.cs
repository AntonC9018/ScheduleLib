using System.Linq.Expressions;
using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Helper.Expressions;

namespace Desktop.NodeData.Common;

public sealed class SourcedPropertySetDisplayFactoryBuilder
{
}

public sealed class VmDisplayFactoryBuilder<T>
{
}

public sealed class RootXXXPropertySetBuilder
{
    public readonly XXXPropertySetModel RootModel = new();
}

public sealed class PropertySetDisplayFactoryBuilder
{
    private readonly IServiceProvider _sp;
    public PropertySetDisplayFactoryBuilder(IServiceProvider sp)
    {
        _sp = sp;
    }

    public PropertySetDisplayFactoryBuilder Source(
        Action<RootXXXPropertySetBuilder> configure)
    {
        var builder = new RootXXXPropertySetBuilder();
        configure(builder);
        return this;
    }

    // public VmDisplayFactoryBuilder<T> UseVm<T, TVM>(NodeDataKey<T> key)
    //     where T : NodeDataViewModelBase<T>
    // {
    // }

    public PropertySetDisplayFactory CreateFactory()
    {
        throw new NotImplementedException();
    }
}

public sealed class PropertySetDisplayFactory
{
    public NodeDataViewModelResult BuildVm(NodeDataVMCreateParams p)
    {
        throw new NotImplementedException();
    }
}

public sealed class VmFactoryService(
    PropertySetDisplayFactory _impl,
    PropertySetId _key)
    : IPropertySetViewModelFactory
{
    public PropertySetId Key => _key;
    public NodeDataViewModelResult Create(NodeDataVMCreateParams p)
    {
        var ret = _impl.BuildVm(p);
        return ret;
    }
}

public readonly record struct PropertySetId(string Key);

public static class PropertySetRegistration
{
    public static void AddPropertySetDisplayFactory(
        this IServiceCollection services,
        PropertySetId key,
        Action<PropertySetDisplayFactoryBuilder> configure)
    {
        services.AddSingleton<IPropertySetViewModelFactory>(sp =>
        {
            var builder = new PropertySetDisplayFactoryBuilder(sp);
            configure(builder);
            var factory = builder.CreateFactory();
            var wrapper = new VmFactoryService(factory, key);
            return wrapper;
        });
    }
}

public readonly record struct PropertyId(string Name);

public sealed class XXXPropertyConfig
{
    public required PropertyId Id { get; init; }
}

public readonly record struct Application();

public abstract class CommonPropertyConfig
{
    public List<Application> Applications { get; } = new();
    public OptionalViewId ViewId { get; set; }
}

public sealed class PropertyConfig : CommonPropertyConfig
{
    public PropertyId Id { get; }

    public PropertyConfig(PropertyId id)
    {
        Id = id;
    }
}

public sealed class XXXPropertySetModel : CommonPropertyConfig
{
    public bool IncludesAll { get; set; } = false;
    public List<PropertyConfig> Properties { get; } = new();
    public List<XXXPropertySetModel> ChildModels { get; } = new();
}

public sealed class XXXPropertySetBuilder<T>
    : IXXXPropertyConfigBuilder
    where T : class
{
    public XXXPropertySetModel Model { get; }
    public NodeDataKey<T> DataKey { get; }

    public XXXPropertySetBuilder(
        NodeDataKey<T> dataKey,
        XXXPropertySetModel model)
    {
        DataKey = dataKey;
        Model = model;
    }

    public CommonPropertyConfig CommonConfig => Model;
}

public sealed class XXXPropertySetIncludeBuilder<T>
    : IXXXPropertyConfigBuilder

    where T : class
{
    public XXXPropertySetModel Model { get; }

    public XXXPropertySetIncludeBuilder(XXXPropertySetModel model)
    {
        Model = model;
    }

    public CommonPropertyConfig CommonConfig => Model;
}

public sealed class XXXPropertyBuilder<T>
    : IXXXPropertyConfigBuilder
    where T : class
{
    public PropertyConfig Config { get; }

    public XXXPropertyBuilder(PropertyConfig config)
    {
        Config = config;
    }

    public CommonPropertyConfig CommonConfig => Config;
}

public static class PropertySet
{
    public static XXXPropertySetBuilder<T> From<T>(NodeDataKey<T> key)
        where T : class
    {
        return new(key, new());
    }

    extension<T>(XXXPropertySetBuilder<T> builder) where T : class
    {
        public XXXPropertySetBuilder<T> Include(Action<XXXPropertySetIncludeBuilder<T>> configure)
        {
            var model = new XXXPropertySetModel();
            builder.Model.ChildModels.Add(model);
            var x = new XXXPropertySetIncludeBuilder<T>(model);
            configure(x);
            return builder;
        }

        public XXXPropertyBuilder<T> IncludeProperty<TProperty>(Expression<Func<T, TProperty?>> access)
        {
            var id = GetPropertyId(access);
            var ret = builder.Model.AddProperty(id);
            return new(ret);
        }

        public XXXPropertyBuilder<T> IncludeProperty(string name)
        {
            var ret = builder.Model.AddProperty(new(name));
            return new(ret);
        }

        public XXXPropertySetBuilder<T> IncludeAll()
        {
            builder.Model.IncludesAll = true;
            return builder;
        }

    }

    extension (XXXPropertySetModel model)
    {
        private PropertyConfig AddProperty(PropertyId id)
        {
            var config = new PropertyConfig(id);
            model.Properties.Add(config);
            return config;
        }

        public PropertyConfig? FindProperty(PropertyId id)
        {
            foreach (var x in model.Properties)
            {
                if (x.Id == id)
                {
                    return x;
                }
            }
            return null;
        }

        public PropertyConfig AddOrFindProperty(PropertyId id)
        {
            if (model.FindProperty(id) is { } ret)
            {
                return ret;
            }
            ret = model.AddProperty(id);
            return ret;
        }
    }

    internal static PropertyId GetPropertyId<T, TProperty>(
        Expression<Func<T, TProperty>> access)
    {
        var member = access.ExtractMemberInfo();
        var memberName = member.Name;
        return new(memberName);
    }

    extension<T>(XXXPropertySetIncludeBuilder<T> builder) where T : class
    {
        public XXXPropertyBuilder<T> Property<TProperty>(Expression<Func<T, TProperty?>> access)
        {
            var id = GetPropertyId(access);
            var ret = builder.Model.AddProperty(id);
            return new(ret);
        }

        public XXXPropertyBuilder<T> Property(string name)
        {
            var id = new PropertyId(name);
            var ret = builder.Model.AddProperty(id);
            return new(ret);
        }
    }

    extension<TBuilder>(TBuilder builder) where TBuilder : IXXXPropertyConfigBuilder
    {
        public TBuilder UseView(ViewId id)
        {
            builder.CommonConfig.ViewId = id;
            return builder;
        }
    }

    extension(RootXXXPropertySetBuilder builder)
    {
        public void Add<T>(XXXPropertySetBuilder<T> other) where T : class
        {
            builder.Add(other.Model);
        }

        public void Add(XXXPropertySetModel model)
        {
            builder.RootModel.ChildModels.Add(model);
        }

        public XXXPropertySetBuilder<T> From<T>(NodeDataKey<T> key) where T : class
        {
            var ret = PropertySet.From(key);
            builder.Add(ret);
            return ret;
        }
    }

    extension(PropertySetDisplayFactoryBuilder builder)
    {
        public void SourceFrom<T>(
            NodeDataKey<T> key,
            Action<XXXPropertySetBuilder<T>>? configure = null)

            where T : class
        {
            builder.Source(x =>
            {
                var b = x.From(key);
                if (configure is null)
                {
                    b.IncludeAll();
                }
                else
                {
                    configure(b);
                }
            });
        }
    }
}

public interface IXXXPropertyConfigBuilder
{
    public CommonPropertyConfig CommonConfig { get; }
}
