using System.Linq.Expressions;

namespace ScheduleLib.Helper.Expressions;

public sealed class ReplaceVariableExpressionVisitor : ExpressionVisitor
{
    private static ThreadLocal<ReplaceVariableExpressionVisitor?> _ThreadInstance = new();

    public static ReplaceVariableExpressionVisitor GetInstance(
        Expression replacement, ParameterExpression parameter)
    {
        var visitor = _ThreadInstance.Value;
        if (visitor is null)
        {
            visitor = new(replacement, parameter);
            _ThreadInstance.Value = visitor;
        }
        else
        {
            visitor.Replacement = replacement;
            visitor.Parameter = parameter;
        }

        return visitor;
    }

    public Expression Replacement { get; set; }
    public ParameterExpression Parameter { get; set; }

    private ReplaceVariableExpressionVisitor(
        Expression replacement,
        ParameterExpression parameter)
    {
        Replacement = replacement;
        Parameter = parameter;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        if (node == Parameter)
        {
            return Replacement;
        }

        return base.VisitParameter(node);
    }
}

public sealed class ReplaceMultipleVariablesExpressionVisitor : ExpressionVisitor, IDisposable
{
    private static ThreadLocal<ReplaceMultipleVariablesExpressionVisitor?> _ThreadInstance = new();

    public static ReplaceMultipleVariablesExpressionVisitor GetInstance()
    {
        var visitor = _ThreadInstance.Value;
        if (visitor is null)
        {
            visitor = new();
            _ThreadInstance.Value = visitor;
        }
        return visitor;
    }

    public List<Expression> Replacements { get; } = new();
    public List<ParameterExpression> Parameters { get; } = new();

    protected override Expression VisitParameter(ParameterExpression node)
    {
        for (int index = 0; index < Parameters.Count; index++)
        {
            var p = Parameters[index];
            if (p == node)
            {
                return Replacements[index];
            }
        }
        return base.VisitParameter(node);
    }

    public void Dispose()
    {
        Replacements.Clear();
        Parameters.Clear();
    }
}

public static class ReplaceVariableHelper
{
    public static Expression ReplaceParameterAndGetBody(
        this LambdaExpression lambda,
        Expression parameterReplacement)
    {
        var visitor = ReplaceVariableExpressionVisitor.GetInstance(parameterReplacement, lambda.Parameters[0]);
        return visitor.Visit(lambda.Body);
    }

    public static Expression ReplaceParameterAndGetExpression(
        this AccessExpression lambda,
        Expression parameterReplacement)
    {
        var visitor = ReplaceVariableExpressionVisitor.GetInstance(parameterReplacement, lambda.Parameter);
        return visitor.Visit(lambda.Access);
    }

    public static Expression ReplaceMultipleParametersAndGetBody(
        this LambdaExpression lambda,
        Expression parameter1Replacement,
        Expression parameter2Replacement)
    {
        const int expectedCount = 2;

        var parameters = lambda.Parameters;
        if (parameters.Count < expectedCount)
        {
            throw new ArgumentException("Lambda must have at least 2 parameters.");
        }

        using var visitor = ReplaceMultipleVariablesExpressionVisitor.GetInstance();
        {
            var outParams = visitor.Parameters;
            for (int i = 0; i < expectedCount; i++)
            {
                outParams.Add(parameters[i]);
            }
        }
        {
            var outReplacements = visitor.Replacements;
            outReplacements.Add(parameter1Replacement);
            outReplacements.Add(parameter2Replacement);
        }
        return visitor.Visit(lambda.Body);
    }
}
