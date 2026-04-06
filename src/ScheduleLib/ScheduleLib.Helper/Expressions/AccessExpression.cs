using System.Linq.Expressions;

namespace ScheduleLib.Helper.Expressions;

/// <summary>
/// A wrapper for expressions of type `parameter.(whatever)`.
/// </summary>
public readonly struct AccessExpression
{
    public readonly ParameterExpression Parameter;
    public readonly Expression Access;

    public AccessExpression(ParameterExpression parameter, Expression access)
    {
        Parameter = parameter;
        Access = access;
    }

    public static AccessExpression<TObject, TResult> Create<TObject, TResult>(
        Expression<Func<TObject, TResult>> expression)
    {
        var untyped = new AccessExpression(expression.Parameters[0], expression.Body);
        return new(untyped);
    }

    public static AccessExpression Create(LambdaExpression expression)
    {
        if (expression.Parameters.Count != 1)
        {
            throw new ArgumentException("Expression must have exactly one parameter.", nameof(expression));
        }

        return new AccessExpression(expression.Parameters[0], expression.Body);
    }
}

/// <summary>
/// A typed wrapper for expressions of type `parameter.(whatever)`.
/// </summary>
public readonly struct AccessExpression<TObject, TFieldOrProperty>
{
    public ParameterExpression Parameter => _underlyingExpression.Parameter;
    public Expression Access => _underlyingExpression.Access;

    private readonly AccessExpression _underlyingExpression;

    public AccessExpression(AccessExpression underlyingExpression)
    {
        _underlyingExpression = underlyingExpression;
    }

    public static implicit operator AccessExpression(AccessExpression<TObject, TFieldOrProperty> expression)
    {
        return expression._underlyingExpression;
    }
}


public static class AccessExpressionExtensions
{
    public static AccessExpression<TToSource, TResult> ReplaceParameter<TFromSource, TToSource, TResult>(
        this AccessExpression<TFromSource, TResult> originalAccess,
        AccessExpression<TToSource, TFromSource> parameterReplacement)
    {
        var visitor = ReplaceVariableExpressionVisitor.GetInstance(parameterReplacement.Access, originalAccess.Parameter);
        var newAccess = visitor.Visit(originalAccess.Access);

        return new AccessExpression<TToSource, TResult>(
            new AccessExpression(parameterReplacement.Parameter, newAccess));
    }

    extension<TObject, TFieldOrProperty>(AccessExpression<TObject, TFieldOrProperty> access)
    {
        public Expression<Action<TObject, TFieldOrProperty>> CreateAssignExpression()
        {
            var param = Expression.Parameter(typeof(TFieldOrProperty), "value");

            // parent.X  ->   parent.X = value
            var assignment = Expression.Assign(access.Access, param);

            // parent, parent.X = value   ->  (parent, value) => parent.X = value
            var parameters = new[] { access.Parameter, param };
            var setter = Expression.Lambda<Action<TObject, TFieldOrProperty>>(
                assignment,
                parameters);

            return setter;
        }

        public Expression<Action<TObject, TSource>> CreateAssignExpression<TSource>(Expression<Func<TSource, TFieldOrProperty>> map)
        {
            var param = Expression.Parameter(typeof(TFieldOrProperty), "value");

            // value  ->  map(value)
            var mappedValue = map.ReplaceParameterAndGetBody(param);

            // parent.X  ->   parent.X = map(value)
            var assignment = Expression.Assign(access.Access, mappedValue);

            // parent, parent.X = map(value)   ->  (parent, value) => parent.X = map(value)
            var parameters = new[] { access.Parameter, param };
            var setter = Expression.Lambda<Action<TObject, TSource>>(
                assignment,
                parameters);

            return setter;
        }

        public AccessExpression<TObject, TMapped> MapReturn<TMapped>(Expression<Func<TFieldOrProperty, TMapped>> map)
        {
            var body = map.ReplaceParameterAndGetBody(access.Access);
            return new(new(access.Parameter, body));
        }

        public Expression<Func<TObject, TFieldOrProperty>> ToLambda()
        {
            var ret = Expression.Lambda<Func<TObject, TFieldOrProperty>>(
                access.Access,
                access.Parameter);
            return ret;
        }
    }
}
