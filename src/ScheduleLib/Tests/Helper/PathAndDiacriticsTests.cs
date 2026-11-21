using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public sealed class PathAndDiacriticsTests
{
    [Theory]
    [InlineData("file.txt", ".pdf", "file.pdf")]
    [InlineData("dir.v1/file", ".pdf", "dir.v1/file.pdf")]
    [InlineData("dir.v1\\file", ".pdf", "dir.v1\\file.pdf")]
    [InlineData("dir.v1/.gitignore", ".bak", "dir.v1/.gitignore.bak")]
    [InlineData(".gitignore", ".bak", ".gitignore.bak")]
    public void WithExtensionOnlyReplacesFileNameExtension(string input, string extension, string expected)
    {
        string actual = PathHelper.WithExtension(file: input, extension: extension);
        Assert.Equal(expected: expected, actual: actual);
    }

    [Fact]
    public void RemoveDiacriticsHandlesFormDExpansionWithoutOverflow()
    {
        string hangul = "\uAC00\uAC01\uAC02";
        string actual = DiacriticsHelper.RemoveDiacritics(hangul);
        Assert.Equal(expected: hangul, actual: actual);
    }
}
