using System.Collections.ObjectModel;
using System.Linq.Expressions;
using System.Reflection;

namespace ScheduleLib.Helper.Expressions;

public static class ExpressionExtensions
{
    public static PropertyInfo ExtractPropertyInfo(this LambdaExpression memberAccessLambda)
    {
        var member = ExtractMemberInfo(memberAccessLambda);
        var property = (PropertyInfo) member;
        return property;
    }

    public static PropertyInfo ExtractPropertyInfo<T>(this Expression<Func<T, object?>> memberAccessLambda)
    {
        var member = ExtractMemberInfo(memberAccessLambda);
        var property = (PropertyInfo) member;
        return property;
    }

    public static MemberInfo ExtractMemberInfo(this LambdaExpression memberAccessLambda)
    {
        var property = ((MemberExpression) memberAccessLambda.Body).Member;
        return property;
    }

    public static MemberInfo ExtractMemberInfo<T>(this Expression<Func<T, object?>> memberAccessLambda)
    {
        // Can't just apply the logic from above, because we have to handle
        // cast expressions and boxing
        var body = memberAccessLambda.Body;
        if (body is UnaryExpression unaryExpression)
        {
            body = unaryExpression.Operand;
        }

        var member = ((MemberExpression) body).Member;
        return member;
    }

    public static ReadOnlyCollection<MemberBinding> ExtractMemberBindings(this LambdaExpression memberAccessLambda)
    {
        var memberBindings = ((MemberInitExpression) memberAccessLambda.Body).Bindings;
        return memberBindings;
    }

    public static IEnumerable<PropertyInfo> ExtractPropertyChain(this Expression expression)
    {
        var currentExpression = expression;
        while (currentExpression is { } e and not ParameterExpression)
        {
            // Calling into an interface member might do a cast.
            if (e is UnaryExpression unaryExpression)
            {
                currentExpression = unaryExpression.Operand;
                continue;
            }
            var memberExpression = (MemberExpression) e;
            var member = (PropertyInfo) memberExpression.Member;
            yield return member;
            currentExpression = memberExpression.Expression;
        }
    }
}
