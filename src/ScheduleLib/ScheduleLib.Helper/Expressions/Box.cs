using System.Linq.Expressions;

namespace ScheduleLib.Helper.Expressions;

/// <summary>
/// Represents a scope needed to capture values in an expression.
/// </summary>
/// <typeparam name="T">The captured value type</typeparam>
public sealed record class Box<T>(T Value)
{
    public (ConstantExpression Constant, MemberExpression Member) MakeExpressions()
    {
        var constant = Expression.Constant(this);
        global::System.Reflection.PropertyInfo CalculateMember()
        {
            global::System.Reflection.PropertyInfo member2 = typeof(Box<T>)
                        .GetProperty(nameof(Value))!;
            return member2;
        }

        var member = Expression.MakeMemberAccess(constant, CalculateMember());
        return (constant, member);
    }
}

public sealed record class Box<T, U>(T Value1, U Value2)
{
    public (ConstantExpression Constant, MemberExpression Member1, MemberExpression Member2) MakeExpressions()
    {
        var boxConstant = Expression.Constant(this);
        global::System.Reflection.PropertyInfo CalculateMember()
        {
            global::System.Reflection.PropertyInfo member = typeof(Box<T, U>)
                        .GetProperty(nameof(Value1))!;
            return member;
        }

        var member1 = Expression.MakeMemberAccess(boxConstant, CalculateMember());
        global::System.Reflection.PropertyInfo CalculateMember2()
        {
            global::System.Reflection.PropertyInfo member3 = typeof(Box<T, U>)
                        .GetProperty(nameof(Value2))!;
            return member3;
        }

        var member2 = Expression.MakeMemberAccess(boxConstant, CalculateMember2());
        return (boxConstant, member1, member2);
    }
}

public static class Box
{
    public static Box<T> Create<T>(T value) => new(value);
    public static Box<T, U> Create<T, U>(T value, U value1) => new(value, value1);
}
