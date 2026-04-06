using System.Linq.Expressions;
using CommunityToolkit.Mvvm.ComponentModel;
using Desktop.NodeData.Common;
using FastExpressionCompiler;
using ScheduleLib.Helper.Expressions;

namespace Desktop.NodeData.Features.Registry;

public abstract class SelectionHelper<TProperty> : ObservableObject
{
    public abstract void UpdateSelection();
}

public readonly record struct AnyNullable<T>(bool HasValue, T? Value = default)
{
    public static implicit operator AnyNullable<T>(T value) => new(HasValue: true, Value: value);
}

public sealed class SelectionHelper<TParent, TProperty> : SelectionHelper<TProperty>
    where TParent : class
{
    public readonly Registry<TProperty> Registry;
    private readonly bool _allowNullSelection;
    private readonly NodeDataAccessor<TParent> _accessor;
    private readonly PropertyAccess<TParent, TProperty> _propertyAccess;

    public SelectionHelper(
        Registry<TProperty> registry,
        bool allowNullSelection,
        NodeDataAccessor<TParent> accessor,
        PropertyAccess<TParent, TProperty> propertyAccess)
    {
        _allowNullSelection = allowNullSelection;
        _accessor = accessor;
        _propertyAccess = propertyAccess;
        Registry = registry;
    }

    private Named<TProperty>? Null => _allowNullSelection ? Named<TProperty>.Default : null;

    public Named<TProperty>? Value
    {
        get
        {
            if (_accessor.Data is not { } data)
            {
                return Null;
            }
            var v = _propertyAccess.Getter(data);
            if (!v.HasValue)
            {
                return Null;
            }
            return Registry.Find(v.Value!);
        }
        set
        {
            if (_accessor.EditableData is not { } data)
            {
                throw new InvalidOperationException("Data is not editable.");
            }

            AnyNullable<TProperty> setVal;
            if (value is null || ReferenceEquals(value, Named<TProperty>.Default))
            {
                setVal = new(HasValue: false);
            }
            else
            {
                setVal = value.Value;
            }

            _propertyAccess.Setter(data, setVal);
        }
    }

    public IReadOnlyList<Named<TProperty>> Values
    {
        get
        {
            if (_allowNullSelection)
            {
                return [Named<TProperty>.Default, .. Registry.Values];
            }
            else
            {
                return Registry.Values;
            }
        }
    }

    public override void UpdateSelection()
    {
        OnPropertyChanged(nameof(Value));
    }
}

public readonly record struct PropertyAccess<TProperty>(
    Delegate Getter,
    Delegate Setter)
{
    public PropertyAccess<TParent, TProperty> AsFullGeneric<TParent>()
    {
        return new(
            (Func<TParent, AnyNullable<TProperty>>) Getter,
            (Action<TParent, AnyNullable<TProperty>>) Setter);
    }
}

public readonly record struct PropertyAccess<TParent, TProperty>(
    Func<TParent, AnyNullable<TProperty>> Getter,
    Action<TParent, AnyNullable<TProperty>> Setter)
{
    public static implicit operator PropertyAccess<TProperty>(PropertyAccess<TParent, TProperty> a)
        => new(a.Getter, a.Setter);
}

internal static class PropertyAccess
{
    public static PropertyAccess<TParent, TProperty> Create<TParent, TProperty>(
        AccessExpression<TParent, TProperty?> access,
        AnyNullable<TProperty>? nullValue = null)
    {
        if (typeof(TProperty).IsGenericType && typeof(TProperty).GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            throw new InvalidOperationException("Must call the nullable struct overload!");
        }

        Expression<Func<TProperty?, AnyNullable<TProperty>>> getterTemplate;

        var nullValueX = nullValue ?? new(HasValue: true, Value: default);
        if (nullValueX.HasValue)
        {
            var nullValueValue = nullValueX.Value;
            getterTemplate = x => new(
                HasValue: !EqualityComparer<TProperty?>.Default.Equals(x, nullValueValue),
                Value: x);
        }
        else
        {
            getterTemplate = x => new(HasValue: true, Value: x);
        }

        var getter = ExpressionHelper.ReplaceParameter(getterTemplate, access);

        // (nullableX) => nullableX.Value
        var nullableValueAccessor = AccessExpression.Create((AnyNullable<TProperty> x) => x.Value);
        // parent.X = nullableX.Value
        var setterBody = Expression.Assign(access.Access, nullableValueAccessor.Access);
        // ('parent', 'nullableX') => 'parent.X = nullableX.Value',
        var setter = Expression.Lambda<Action<TParent, AnyNullable<TProperty>>>(
            setterBody,
            access.Parameter,
            nullableValueAccessor.Parameter);

        return new(
            getter.CompileFast(),
            setter.CompileFast());
    }

    public static PropertyAccess<TParent, TProperty> CreateNullable<TParent, TProperty>(
        AccessExpression<TParent, TProperty?> access)
        where TProperty : struct
    {
        var nullableAccessor = access.MapReturn(x => new AnyNullable<TProperty>(x.HasValue, x ?? default));
        var getter = nullableAccessor.ToLambda();

        var backward = AccessExpression.Create((AnyNullable<TProperty> x) => (TProperty?)(x.HasValue ? x.Value : null));
        // 'parent.X = backward(nullableX)'
        var setterBody = Expression.Assign(access.Access, backward.Access);
        // ('parent', 'nullableX') => 'parent.X = backward(nullableX)'
        var setter = Expression.Lambda<Action<TParent, AnyNullable<TProperty>>>(
            setterBody,
            access.Parameter,
            backward.Parameter);

        return new(
            getter.CompileFast(),
            setter.CompileFast());
    }
}

public static class SelectionHelper1
{
    extension<TParent>(NodeDataAccessor<TParent> accessor)
        where TParent : class
    {
        public SelectionHelper<TParent, TProperty> SelectionHelper<TProperty>(
            Registry<TProperty> registry,
            PropertyAccess<TParent, TProperty> selector,
            bool allowNull = false)
        {
            return new(
                registry,
                allowNull,
                accessor,
                selector);
        }

        public SelectionHelper<TParent, TProperty> SelectionHelper<TProperty>(
            Registry<TProperty> registry,
            Expression<Func<TParent, TProperty?>> selector,
            bool allowNull = false)

            where TProperty : struct
        {
            var access = AccessExpression.Create(selector);
            var propAccess = PropertyAccess.CreateNullable(access);
            return accessor.SelectionHelper(registry, propAccess, allowNull);
        }

        public SelectionHelper<TParent, TProperty> SelectionHelper<TProperty>(
            Registry<TProperty> registry,
            Expression<Func<TParent, TProperty?>> selector,
            bool allowNull = false,
            AnyNullable<TProperty>? nullValue = null)
        {
            var access = AccessExpression.Create(selector);
            var propAccess = PropertyAccess.Create(access, nullValue);
            return accessor.SelectionHelper(registry, propAccess, allowNull);
        }
    }
}

