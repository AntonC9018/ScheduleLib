using System.Linq.Expressions;

namespace ScheduleLib.Helper.Expressions;

public sealed record class Box<T>(T Value)
{
    public readonly record struct Expressions(ConstantExpression Constant, MemberExpression Member);

    public Expressions MakeExpressions()
    {
        var constant = Expression.Constant(this);

        var valueProperty = typeof(Box<T>).GetProperty(nameof(Value))!;
        var member = Expression.MakeMemberAccess(constant, valueProperty);
        return new(constant, member);
    }
}

public sealed record class Box<T, U>(T Value1, U Value2)
{
    public readonly record struct Expressions(
        ConstantExpression Constant,
        MemberExpression Member1,
        MemberExpression Member2);

    public Expressions MakeExpressions()
    {
        var boxConstant = Expression.Constant(this);

        var value1Property = typeof(Box<T, U>).GetProperty(nameof(Value1))!;
        var member1 = Expression.MakeMemberAccess(boxConstant, value1Property);

        var value2Property = typeof(Box<T, U>).GetProperty(nameof(Value2))!;
        var member2 = Expression.MakeMemberAccess(boxConstant, value2Property);
        return new(
            boxConstant,
            Member1: member1,
            Member2: member2);
    }
}

public static class Box
{
    public static Box<T> Create<T>(T value) => new(value);
    public static Box<T, U> Create<T, U>(T value, U value1) => new(value, value1);
}
