using System.Linq.Expressions;

namespace ScheduleLib.Helper.Expressions;

/// <summary>
/// Represents a scope needed to capture values in an expression.
/// </summary>
/// <typeparam name="T">The captured value type</typeparam>
public sealed record class Box<T>(T Value)
{
    /// <summary>The constant that holds this box and the member access that reads its value.</summary>
    public readonly record struct Expressions(ConstantExpression Constant, MemberExpression Member);

    public Expressions MakeExpressions()
    {
        var constant = Expression.Constant(this);

        System.Reflection.PropertyInfo CalculateMember()
        {
            var member2 = typeof(Box<T>)
                .GetProperty(nameof(Value))!;
            return member2;
        }

        var member = Expression.MakeMemberAccess(constant, CalculateMember());
        return new(constant, member);
    }
}

public sealed record class Box<T, U>(T Value1, U Value2)
{
    /// <summary>The constant that holds this box and the member accesses that read its values.</summary>
    public readonly record struct Expressions(
        ConstantExpression Constant,
        MemberExpression Member1,
        MemberExpression Member2);

    public Expressions MakeExpressions()
    {
        var boxConstant = Expression.Constant(this);

        System.Reflection.PropertyInfo CalculateMember()
        {
            var member = typeof(Box<T, U>)
                .GetProperty(nameof(Value1))!;
            return member;
        }

        var member1 = Expression.MakeMemberAccess(boxConstant, CalculateMember());

        System.Reflection.PropertyInfo CalculateMember2()
        {
            var member3 = typeof(Box<T, U>)
                .GetProperty(nameof(Value2))!;
            return member3;
        }

        var member2 = Expression.MakeMemberAccess(boxConstant, CalculateMember2());
        return new(boxConstant,
            Member1: member1,
            Member2: member2);
    }
}

public static class Box
{
    public static Box<T> Create<T>(T value) => new(value);
    public static Box<T, U> Create<T, U>(T value, U value1) => new(value, value1);
}
