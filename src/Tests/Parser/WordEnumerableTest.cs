using ScheduleLib.Parsing.CourseName;

namespace App.Tests;

public sealed class WordEnumerableTests
{
    [Fact]
    public void CountTest()
    {
        var e = new WordEnumerable("hello world", opts: new());
        Assert.Equal(2, e.Count());
    }

    [Fact]
    public void BasicTest()
    {
        var w = new WordEnumerable("hello world",
            opts: new()
            {
                IgnorePunctuation = false,
            });
        Assert.Collection(w.ToArray(),
            s1 => Assert.Equal("hello", s1),
            s2 => Assert.Equal("world", s2));
    }

    [Fact]
    public void PunctuationAreErrors()
    {
        Assert.Throws<InvalidSeparatorException>(() =>
        {
            var w = new WordEnumerable("hello world!",
                opts: new()
                {
                    IgnorePunctuation = false,
                });
            w.ToArray();
        });
    }

    [Fact]
    public void PunctuationIgnored()
    {
        var w = new WordEnumerable("hello world!",
            opts: new()
            {
                IgnorePunctuation = true,
            });
        Assert.Collection(w.ToArray(),
            s1 => Assert.Equal("hello", s1),
            s2 => Assert.Equal("world", s2));
    }

    [Fact]
    public void ConsecutiveIgnoredCharsSkipped()
    {
        var w = new WordEnumerable("hello  world",
            opts: new()
            {
                IgnorePunctuation = true,
            });
        Assert.Collection(w.ToArray(),
            s1 => Assert.Equal("hello", s1),
            s2 => Assert.Equal("world", s2));
    }

    [Fact]
    public void IgnoredCharsInFrontSkipped()
    {
        var w = new WordEnumerable("  hello world",
            opts: new()
            {
                IgnorePunctuation = true,
            });
        Assert.Collection(w.ToArray(),
            s1 => Assert.Equal("hello", s1),
            s2 => Assert.Equal("world", s2));
    }

    [Fact]
    public void StringWithOnlyIgnoredCharsIsEmpty()
    {
        var w = new WordEnumerable(" !,,  ",
            opts: new()
            {
                IgnorePunctuation = true,
            });
        Assert.Empty(w.ToArray());
    }

    [Fact]
    public void PeriodsAreConsideredValidWordCharacters()
    {
        var w = new WordEnumerable("hello. world",
            opts: new()
            {
                IgnorePunctuation = false,
            });
        Assert.Collection(w.ToArray(),
            s1 => Assert.Equal("hello.", s1),
            s2 => Assert.Equal("world", s2));
    }
}

file static class Helper
{
    public static string[] ToArray(this WordEnumerable e)
    {
        var count = e.Count();
        var words = new string[count];
        var i = 0;
        foreach (var word in e)
        {
            words[i] = word.ToString();
            i++;
        }
        return words;
    }
}
