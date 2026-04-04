using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;
using Desktop.NodeData.Common;
using FastExpressionCompiler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.OnlineRegistry.Impl;

namespace Desktop.NodeData.Features.Registry;

public sealed class ThingRegistryOptions<T>
{
    public readonly List<Named<T>> Values = new();
    public IEqualityComparer<T> Comparer = EqualityComparer<T>.Default;
}

public sealed class ThingReadOnlyRegistry<T>(IOptions<ThingRegistryOptions<T>> options)
{
    private readonly List<Named<T>> _values = options.Value.Values;
    private readonly IEqualityComparer<T> _comparer = options.Value.Comparer;

    [return: NotNullIfNotNull(nameof(value))]
    public Named<T>? Find(T? value)
    {
        if (value is null)
        {
            return null;
        }
        var x = _values.FirstOrDefault(x => _comparer.Equals(x.Value, value));
        if (x is null)
        {
            throw new NotImplementedException("Implement the proper registry system!");
        }
        return x;
    }

    public IReadOnlyList<Named<T>> Values => _values;
}

public sealed record class Named<T>(
    string Name,
    T Value)
{
    public override string ToString() => Name;
}

public static class ThingRegistryHelper
{
    extension(IServiceCollection services)
    {
        public OptionsBuilder<ThingRegistryOptions<T>> AddRegistry<T>(Action<ThingRegistryOptions<T>>? configure = null)
        {
            services.AddSingleton<ThingReadOnlyRegistry<T>>();
            var ret = services.AddOptions<ThingRegistryOptions<T>>();
            if (configure != null)
            {
                ret.Configure(configure);
            }
            return ret;
        }

        public void ConfigureRegistry<T>(Action<ThingRegistryOptions<T>> configure)
        {
            services.Configure(configure);
        }
    }


    extension<T>(ThingRegistryOptions<T> opts)
    {
        public void Add(string name, T value)
        {
            opts.Values.Add(new(name, value));
        }

        public void UseTypeComparer()
        {
            opts.Comparer = new TypeComparer<T>();
        }
    }
}

public sealed class TypeComparer<T> : IEqualityComparer<T>
{
    public bool Equals(T? x, T? y)
    {
        if (ComparisonHelper.AtLeastOneIsDefault(x, y, out bool areBothNull))
        {
            return areBothNull;
        }
        return x.GetType() == y.GetType();
    }

    public int GetHashCode([DisallowNull] T obj)
    {
        return obj.GetType().GetHashCode();
    }
}

public interface SelectionHelper<TProperty>
{
    public Named<TProperty>? Value { get; set; }
    public IReadOnlyList<Named<TProperty>> Values { get; }
}

public sealed class SelectionHelperClass<TParent, TProperty> : SelectionHelper<TProperty>
    where TParent : class
    where TProperty : class
{
    public readonly ThingReadOnlyRegistry<TProperty> Registry;
    private readonly PropertyAccess _propertyAccess;
    private readonly NodeDataAccessor<TParent> _accessor;

    internal SelectionHelperClass(
        ThingReadOnlyRegistry<TProperty> registry,
        Expression<Func<TParent, TProperty?>> selector,
        NodeDataAccessor<TParent> accessor)
    {
        Registry = registry;
        _accessor = accessor;
        _propertyAccess = CreateAccess(selector);
    }

    public Named<TProperty>? Value
    {
        get
        {
            if (_accessor.Data is not { } data)
            {
                return null;
            }

            var v = _propertyAccess.Getter(data);
            if (v is null)
            {
                return null;
            }

            return Registry.Find(v);
        }
        set
        {
            if (_accessor.EditableData is not { } data)
            {
                throw new InvalidOperationException("Data is not editable.");
            }

            if (value is null)
            {
                _propertyAccess.SetterToNull(data);
            }
            else
            {
                _propertyAccess.Setter(data, value.Value);
            }
        }
    }

    public IReadOnlyList<Named<TProperty>> Values => Registry.Values;

    private static PropertyAccess CreateAccess(Expression<Func<TParent, TProperty?>> expression)
    {
        var getter = expression.CompileFast();

        var memberExpr = (MemberExpression)expression.Body;
        var param = Expression.Parameter(typeof(TParent));
        var valueParam = Expression.Parameter(typeof(TProperty));

        var memberAccess = Expression.MakeMemberAccess(param, memberExpr.Member);
        var setter = Expression.Lambda<Action<TParent, TProperty>>(
            Expression.Assign(memberAccess, valueParam),
            param, valueParam).CompileFast();

        var setterToNull = Expression.Lambda<Action<TParent>>(
            Expression.Assign(memberAccess, Expression.Constant(null, typeof(TProperty))),
            param).CompileFast();

        return new(getter, setter, setterToNull);
    }

    private readonly record struct PropertyAccess(
        Func<TParent, TProperty?> Getter,
        Action<TParent, TProperty> Setter,
        Action<TParent> SetterToNull);
}

public sealed class SelectionHelperNullableStruct<TParent, TProperty> : SelectionHelper<TProperty>
    where TParent : class
    where TProperty : struct
{
    public readonly ThingReadOnlyRegistry<TProperty> Registry;
    private readonly PropertyAccess _propertyAccess;
    private readonly NodeDataAccessor<TParent> _accessor;

    internal SelectionHelperNullableStruct(
        ThingReadOnlyRegistry<TProperty> registry,
        Expression<Func<TParent, TProperty?>> selector,
        NodeDataAccessor<TParent> accessor)
    {
        Registry = registry;
        _accessor = accessor;
        _propertyAccess = CreateAccess(selector);
    }

    public Named<TProperty>? Value
    {
        get
        {
            if (_accessor.Data is not { } data)
            {
                return null;
            }

            var v = _propertyAccess.Getter(data);
            if (v is null)
            {
                return null;
            }

            return Registry.Find(v.Value);
        }
        set
        {
            if (_accessor.EditableData is not { } data)
            {
                throw new InvalidOperationException("Data is not editable.");
            }

            if (value is null)
            {
                _propertyAccess.SetterToNull(data);
            }
            else
            {
                _propertyAccess.Setter(data, value.Value);
            }
        }
    }

    public IReadOnlyList<Named<TProperty>> Values => Registry.Values;

    private static PropertyAccess CreateAccess(Expression<Func<TParent, TProperty?>> expression)
    {
        var getter = expression.CompileFast();

        var memberExpr = (MemberExpression)expression.Body;
        var param = Expression.Parameter(typeof(TParent));
        var valueParam = Expression.Parameter(typeof(TProperty));

        var memberAccess = Expression.MakeMemberAccess(param, memberExpr.Member);

        // Widen TProperty -> TProperty? (Nullable<TProperty>) for the assign
        var setter = Expression.Lambda<Action<TParent, TProperty>>(
            Expression.Assign(memberAccess, Expression.Convert(valueParam, typeof(TProperty?))),
            param, valueParam).CompileFast();

        // default(TProperty?) is null for Nullable<T>
        var setterToNull = Expression.Lambda<Action<TParent>>(
            Expression.Assign(memberAccess, Expression.Default(typeof(TProperty?))),
            param).CompileFast();

        return new(getter, setter, setterToNull);
    }

    private readonly record struct PropertyAccess(
        Func<TParent, TProperty?> Getter,
        Action<TParent, TProperty> Setter,
        Action<TParent> SetterToNull);
}

public static class SelectionHelper
{
    public static SelectionHelperClass<TParent, TProperty> Create<TParent, TProperty>(
        ThingReadOnlyRegistry<TProperty> registry,
        NodeDataAccessor<TParent> accessor,
        Expression<Func<TParent, TProperty?>> selector)
        where TParent : class
        where TProperty : class
        => new(registry, selector, accessor);

    public static SelectionHelperNullableStruct<TParent, TProperty> Create<TParent, TProperty>(
        ThingReadOnlyRegistry<TProperty> registry,
        NodeDataAccessor<TParent> accessor,
        Expression<Func<TParent, TProperty?>> selector)
        where TParent : class
        where TProperty : struct
        => new(registry, selector, accessor);
}


public sealed class RegistryConfigViewModel(
    NodeDataAccessor<RegistryConfig> _helper,
    ThingReadOnlyRegistry<IEquationCommandsDerivation> _derivations,
    ThingReadOnlyRegistry<ExtraLessonInstanceAction> _extraLessons)

    : NodeDataViewModelBase<RegistryConfig>
{
    public static void Register(IServiceCollection services)
    {
        services.AddVmFactory(
            RegistryConfig.Key,
            b => b.VM<RegistryConfigViewModel>().UseUpdateOnDataChange());

        services.AddRegistry<IEquationCommandsDerivation>(opts =>
        {
            opts.UseTypeComparer();
            opts.Add("Compare lessons from any day", new AnyDayDerivation());
            opts.Add("Only compare lessons in the same day", new SameDayDerivation());
        });

        services.AddRegistry<ExtraLessonInstanceAction>(opts =>
        {
            opts.Add("Delete", ExtraLessonInstanceAction.Delete);
            opts.Add("Leave as is", ExtraLessonInstanceAction.LeaveAlone);
        });
    }

    public override void UpdateSelection()
    {
        Credentials.Set(_helper.ConditionallyEditableData);
        OnPropertyChanged(nameof(DryRun));
    }

    public ObservableCredentials<RegistryConfig> Credentials { get; } = new();

    // How to do this better?
    public SelectionHelper<ExtraLessonInstanceAction> ExtraLesson { get; } =
        SelectionHelper.Create(_extraLessons, _helper, c => c.ExtraLessonInstanceAction);

    public SelectionHelper<IEquationCommandsDerivation> Derivation { get; } =
        SelectionHelper.Create(_derivations, _helper, c => c.EquationCommandsDerivation);

    public bool? DryRun
    {
        get
        {
            if (_helper.Data?.CommandProcessingConfig is not { } c)
            {
                return null;
            }
            if (c.HasAnyDryRun(LessonEquationCommandTypes.All))
            {
                return true;
            }
            return false;
        }
        set
        {
            var v = _helper.EditableData;
            if (v == null)
            {
                throw new InvalidOperationException("Cannot set DryRun when no node selected");
            }
            CommandProcessingConfigBuilder b;
            if (v.CommandProcessingConfig is { } c)
            {
                b = c.Builder();
            }
            else
            {
                b = new();
                b.Log().SetAll();
                b.Process().SetAll();
            }
            b.DryRun().SetAll(value ?? false);
            v.CommandProcessingConfig = b.Build();
        }
    }

}
