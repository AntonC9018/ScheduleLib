using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

[assembly: InternalsVisibleTo("MainCli.Tests")]

if (args.Length < 2 || args[0] is not ("--add" or "--remove"))
{
    Console.Error.WriteLine("Usage: Formatting --add|--remove file.cs [file.cs ...]");
    return 2;
}

var add = args[0] == "--add";
foreach (var path in args.Skip(1))
{
    var text = File.ReadAllText(path);
    var tree = CSharpSyntaxTree.ParseText(text, new CSharpParseOptions(LanguageVersion.Preview));
    if (tree.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
    {
        Console.Error.WriteLine($"Cannot arrange commas in syntactically invalid file: {path}");
        return 2;
    }
    var root = tree.GetRoot();
    var formatted = new TrailingCommaRewriter(add).Visit(root)!.ToFullString();
    if (text != formatted)
    {
        File.WriteAllText(path, formatted);
    }
}
return 0;

internal sealed class TrailingCommaRewriter(bool add) : CSharpSyntaxRewriter
{
    public override SyntaxNode? VisitInitializerExpression(InitializerExpressionSyntax node)
    {
        var multiline = Multiline(node.OpenBraceToken, node.CloseBraceToken);
        var visited = (InitializerExpressionSyntax) base.VisitInitializerExpression(node)!;
        return multiline ? visited.WithExpressions(Arrange(visited.Expressions)) : visited;
    }

    public override SyntaxNode? VisitAnonymousObjectCreationExpression(AnonymousObjectCreationExpressionSyntax node)
    {
        var multiline = Multiline(node.OpenBraceToken, node.CloseBraceToken);
        var visited = (AnonymousObjectCreationExpressionSyntax) base.VisitAnonymousObjectCreationExpression(node)!;
        return multiline ? visited.WithInitializers(Arrange(visited.Initializers)) : visited;
    }

    public override SyntaxNode? VisitCollectionExpression(CollectionExpressionSyntax node)
    {
        var multiline = Multiline(node.OpenBracketToken, node.CloseBracketToken);
        var visited = (CollectionExpressionSyntax) base.VisitCollectionExpression(node)!;
        return multiline ? visited.WithElements(Arrange(visited.Elements)) : visited;
    }

    public override SyntaxNode? VisitEnumDeclaration(EnumDeclarationSyntax node)
    {
        var multiline = Multiline(node.OpenBraceToken, node.CloseBraceToken);
        var visited = (EnumDeclarationSyntax) base.VisitEnumDeclaration(node)!;
        return multiline ? visited.WithMembers(Arrange(visited.Members)) : visited;
    }

    public override SyntaxNode? VisitSwitchExpression(SwitchExpressionSyntax node)
    {
        var multiline = Multiline(node.OpenBraceToken, node.CloseBraceToken);
        var visited = (SwitchExpressionSyntax) base.VisitSwitchExpression(node)!;
        return multiline ? visited.WithArms(Arrange(visited.Arms)) : visited;
    }

    public override SyntaxNode? VisitListPattern(ListPatternSyntax node)
    {
        var multiline = Multiline(node.OpenBracketToken, node.CloseBracketToken);
        var visited = (ListPatternSyntax) base.VisitListPattern(node)!;
        return multiline ? visited.WithPatterns(Arrange(visited.Patterns)) : visited;
    }

    public override SyntaxNode? VisitPropertyPatternClause(PropertyPatternClauseSyntax node)
    {
        var multiline = Multiline(node.OpenBraceToken, node.CloseBraceToken);
        var visited = (PropertyPatternClauseSyntax) base.VisitPropertyPatternClause(node)!;
        return multiline ? visited.WithSubpatterns(Arrange(visited.Subpatterns)) : visited;
    }

    private static bool Multiline(SyntaxToken opening, SyntaxToken closing)
    {
        var start = opening.GetLocation().GetLineSpan().StartLinePosition.Line;
        var end = closing.GetLocation().GetLineSpan().StartLinePosition.Line;
        return start != end;
    }

    private SeparatedSyntaxList<T> Arrange<T>(SeparatedSyntaxList<T> list) where T : SyntaxNode
    {
        if (list.Count == 0)
        {
            return list;
        }
        var hasComma = list.SeparatorCount == list.Count;
        if (hasComma == add)
        {
            return list;
        }
        var last = list[^1];
        if (add)
        {
            var trivia = last.GetTrailingTrivia();
            var replacement = (T) last.WithoutTrailingTrivia();
            var nodes = list.Replace(last, replacement).GetWithSeparators();
            var comma = SyntaxFactory.Token(SyntaxKind.CommaToken).WithTrailingTrivia(trivia);
            return SyntaxFactory.SeparatedList<T>(nodes.Add(comma));
        }
        var trailing = list.GetSeparator(list.Count - 1);
        var preserved = last.GetTrailingTrivia().AddRange(trailing.LeadingTrivia).AddRange(trailing.TrailingTrivia);
        var withoutSeparator = list.GetWithSeparators().RemoveAt(list.Count * 2 - 1);
        var result = SyntaxFactory.SeparatedList<T>(withoutSeparator);
        return result.Replace(result[^1], (T) last.WithTrailingTrivia(preserved));
    }
}
