using System.Linq.Expressions;

namespace ScheduleLib.Helper.Expressions;

public static class ExpressionHelper
{
    public static Expression<Func<T, bool>> CurrySecondParameter<T, TValue>(
        this Expression<Func<T, TValue, bool>> originalPredicate,
        Expression value)
    {
        var queryParameter = originalPredicate.Parameters[0];
        var contextParameter = originalPredicate.Parameters[1];
        var body = originalPredicate.Body;

        // x + u  -->   x + box.value
        var lambda = CurryParameter<T, bool>(body, queryParameter, contextParameter, value);

        return lambda;
    }

    public static Expression<Func<T, bool>> CurrySecondParameter<T, TValue>(
        this Expression<Func<T, TValue, bool>> originalPredicate,
        Box<TValue> box)
    {
        var boxAccessExpression = box.MakeExpressions().Member;
        return CurrySecondParameter(originalPredicate, boxAccessExpression);
    }

    public static Expression<Func<T, TReturn>> CurryFirstParameter<T, TValue, TReturn>(
        this Expression<Func<TValue, T, TReturn>> originalFunc,
        Expression value)
    {
        var queryParameter = originalFunc.Parameters[1];
        var contextParameter = originalFunc.Parameters[0];
        var body = originalFunc.Body;

        // x + u  -->   box.value + u
        var lambda = CurryParameter<T, TReturn>(body, queryParameter, contextParameter, value);

        return lambda;
    }

    public static Expression<Func<T, bool>> CurryFirstParameter<T, TValue>(
        this Expression<Func<TValue, T, bool>> originalPredicate,
        Box<TValue> box)
    {
        var boxAccessExpression = box.MakeExpressions().Member;
        return CurryFirstParameter(originalPredicate, boxAccessExpression);
    }

    public static Expression<Func<T, TReturn>> CurryParameter<T, TReturn>(
        Expression body,
        ParameterExpression queryParameter,
        ParameterExpression contextParameter,
        Expression value)
    {
        var visitor = ReplaceVariableExpressionVisitor.GetInstance(value, contextParameter);
        body = visitor.Visit(body);
        var lambda = Expression.Lambda<Func<T, TReturn>>(body, queryParameter);
        return lambda;
    }

    private static LambdaExpression ReplaceFirstParameter(
        LambdaExpression source,
        AccessExpression replacement)
    {
        var parameters = source.Parameters;
        int paramCount = parameters.Count;
        var firstParam = parameters[0];
        var newParameters = new ParameterExpression[paramCount];
        newParameters[0] = replacement.Parameter;
        for (int i = 1; i < paramCount; i++)
        {
            newParameters[i] = source.Parameters[i];
        };

        var oldBody = source.Body;
        var visitor = ReplaceVariableExpressionVisitor.GetInstance(replacement.Access, firstParam);
        var newBody = visitor.Visit(oldBody);
        var ret = Expression.Lambda(newBody, newParameters);
        return ret;
    }

    public static Expression<Func<TReplacement, TReturn>> ReplaceParameter<TTarget, TReplacement, TReturn>(
        Expression<Func<TTarget, TReturn>> source,
        AccessExpression<TReplacement, TTarget> parameterReplacement)
    {
        var ret = ReplaceFirstParameter(source, parameterReplacement);
        return (Expression<Func<TReplacement, TReturn>>) ret;
    }

    public static Expression<Func<TReplacement, TOther, TReturn>> ReplaceParameter<TTarget, TReplacement, TOther, TReturn>(
        Expression<Func<TTarget, TOther, TReturn>> source,
        AccessExpression<TReplacement, TTarget> parameterReplacement)
    {
        var ret = ReplaceFirstParameter(source, parameterReplacement);
        return (Expression<Func<TReplacement, TOther, TReturn>>) ret;
    }
}
