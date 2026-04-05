using System.Linq.Expressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using FastExpressionCompiler;

namespace Desktop.NodeData.Features.Registry;

public abstract class SelectionHelper<TProperty> : ObservableObject
{
    public abstract Named<TProperty>? Value { get; set; }
    public abstract IReadOnlyList<Named<TProperty>> Values { get; }
    public void UpdateSelection()
    {
        OnPropertyChanged(nameof(Value));
    }
}

public sealed class SelectionHelperClass<TParent, TProperty> : SelectionHelper<TProperty>
    where TParent : class
    where TProperty : class
{
    public readonly Registry<TProperty> Registry;
    private readonly PropertyAccess _propertyAccess;
    private readonly NodeDataAccessor<TParent> _accessor;
    private readonly bool _allowNull;

    internal SelectionHelperClass(
        Registry<TProperty> registry,
        Expression<Func<TParent, TProperty?>> selector,
        NodeDataAccessor<TParent> accessor,
        bool allowNull)
    {
        Registry = registry;
        _accessor = accessor;
        _allowNull = allowNull;
        _propertyAccess = CreateAccess(selector);
    }

    public override Named<TProperty>? Value
    {
        get
        {
            if (_accessor.Data is not { } data)
            {
                if (_allowNull)
                {
                    return Named<TProperty>.Default;
                }
                return null;
            }

            var v = _propertyAccess.Getter(data);
            if (v is null)
            {
                if (_allowNull)
                {
                    return Named<TProperty>.Default;
                }
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

            if (value is null || ReferenceEquals(value, Named<TProperty>.Default))
            {
                _propertyAccess.SetterToNull(data);
            }
            else
            {
                _propertyAccess.Setter(data, value.Value);
            }
        }
    }

    public override IReadOnlyList<Named<TProperty>> Values => Registry.Values;

    private static PropertyAccess CreateAccess(Expression<Func<TParent, TProperty?>> expression)
    {
        var getter = expression.CompileFast();

        var memberExpr = (MemberExpression) expression.Body;
        var param = Expression.Parameter(typeof(TParent));
        var valueParam = Expression.Parameter(typeof(TProperty));

        var memberAccess = Expression.MakeMemberAccess(param, memberExpr.Member);
        var setter = Expression.Lambda<Action<TParent, TProperty>>(
            Expression.Assign(memberAccess, valueParam),
            param,
            valueParam).CompileFast();

        var setterToNull = Expression.Lambda<Action<TParent>>(
            Expression.Assign(memberAccess, Expression.Constant(null, typeof(TProperty))),
            param).CompileFast();

        return new(
            getter,
            setter,
            setterToNull);
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
    public readonly Registry<TProperty> Registry;
    private readonly PropertyAccess _propertyAccess;
    private readonly NodeDataAccessor<TParent> _accessor;
    private readonly bool _allowNull;

    internal SelectionHelperNullableStruct(
        Registry<TProperty> registry,
        Expression<Func<TParent, TProperty?>> selector,
        NodeDataAccessor<TParent> accessor,
        bool allowNull)
    {
        Registry = registry;
        _accessor = accessor;
        _allowNull = allowNull;
        _propertyAccess = CreateAccess(selector);
    }

    public override Named<TProperty>? Value
    {
        get
        {
            if (_accessor.Data is not { } data)
            {
                if (_allowNull)
                {
                    return Named<TProperty>.Default;
                }
                return null;
            }

            var v = _propertyAccess.Getter(data);
            if (v is null)
            {
                if (_allowNull)
                {
                    return Named<TProperty>.Default;
                }
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

            if (value is null || ReferenceEquals(value, Named<TProperty>.Default))
            {
                _propertyAccess.SetterToNull(data);
            }
            else
            {
                _propertyAccess.Setter(data, value.Value);
            }
        }
    }

    public override IReadOnlyList<Named<TProperty>> Values => Registry.Values;

    private static PropertyAccess CreateAccess(Expression<Func<TParent, TProperty?>> expression)
    {
        var getter = expression.CompileFast();

        var memberExpr = (MemberExpression) expression.Body;
        var param = Expression.Parameter(typeof(TParent));
        var valueParam = Expression.Parameter(typeof(TProperty));

        var memberAccess = Expression.MakeMemberAccess(param, memberExpr.Member);

        // Widen TProperty -> TProperty? (Nullable<TProperty>) for the assign
        var setter = Expression.Lambda<Action<TParent, TProperty>>(
            Expression.Assign(memberAccess, Expression.Convert(valueParam, typeof(TProperty?))),
            param,
            valueParam).CompileFast();

        // default(TProperty?) is null for Nullable<T>
        var setterToNull = Expression.Lambda<Action<TParent>>(
            Expression.Assign(memberAccess, Expression.Default(typeof(TProperty?))),
            param).CompileFast();

        return new(
            getter,
            setter,
            setterToNull);
    }

    private readonly record struct PropertyAccess(
        Func<TParent, TProperty?> Getter,
        Action<TParent, TProperty> Setter,
        Action<TParent> SetterToNull);
}

public static class SelectionHelper
{
    // How to do this better?
    // This allocates lots of garbage, because it's not cached.
    extension<TParent>(NodeDataAccessor<TParent> accessor) where TParent : class
    {
        public SelectionHelperClass<TParent, TProperty> SelectionHelper<TProperty>(
            Registry<TProperty> registry,
            Expression<Func<TParent, TProperty?>> selector,
            bool allowNull = false) where TProperty : class
            => new(
                registry,
                selector,
                accessor,
                allowNull);

        public SelectionHelperNullableStruct<TParent, TProperty> SelectionHelper<TProperty>(
            Registry<TProperty> registry,
            Expression<Func<TParent, TProperty?>> selector,
            bool allowNull = false)
            where TProperty : struct
            => new(
                registry,
                selector,
                accessor,
                allowNull);
    }
}

