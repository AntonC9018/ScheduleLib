using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

public sealed class FormattingTests
{
    [Theory]
    [InlineData("var value = new[] {\n1,\n2 // Keep this comment.\n};")]
    [InlineData("var value = new {\nFirst = 1,\nLast = 2 // Keep this comment.\n};")]
    [InlineData("var value = new Thing {\nFirst = 1,\nLast = 2 // Keep this comment.\n};")]
    [InlineData("int[] value = [\n1,\n2 // Keep this comment.\n];")]
    [InlineData("enum Choice {\nFirst,\nLast // Keep this comment.\n}")]
    [InlineData("var value = input switch {\ntrue => 1,\nfalse => 2 // Keep this comment.\n};")]
    [InlineData("var value = input is [\n1,\n_ // Keep this comment.\n];")]
    [InlineData("var value = input is {\nFirst: 1,\nLast: 2 // Keep this comment.\n};")]
    public void MultilineListsKeepCommentsAndRoundTripWithoutChangingOtherSyntax(string source)
    {
        var formatted = Arrange(source, add: true);
        Assert.NotEqual(source, formatted);
        Assert.Contains(", // Keep this comment.", formatted);
        Assert.Equal(formatted, Arrange(formatted, add: true));
        Assert.Equal(source, Arrange(formatted, add: false));
    }

    [Fact]
    public void SingleLineListsAndMultilineArgumentListsAreNotRewritten()
    {
        const string source = "var one = new[] { 1, 2 };\nvar two = new { First = 1 };\nUse(\n1,\n2\n);";
        Assert.Equal(source, Arrange(source, add: true));
    }

    private static string Arrange(string source, bool add)
    {
        var options = new CSharpParseOptions(LanguageVersion.Preview);
        var root = CSharpSyntaxTree.ParseText(source, options).GetRoot();
        var updated = new TrailingCommaRewriter(add).Visit(root)!.ToFullString();
        var reparsed = CSharpSyntaxTree.ParseText(updated, options);
        Assert.DoesNotContain(reparsed.GetDiagnostics(), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        return updated;
    }
}
