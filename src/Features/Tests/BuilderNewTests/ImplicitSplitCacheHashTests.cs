using System.Security.Cryptography;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.ScheduleDefaults;

public sealed class ImplicitSplitCacheHashTests
{
    [Fact]
    public void CacheHashIgnoresDeclarationOrder()
    {
        string expected2 = Hash(BuildConfigA());
        string actual2 = Hash(BuildConfigB());
        Assert.Equal(expected: expected2, actual: actual2);
        string expected3 = Hash(BuildConfigA());
        string actual3 = Hash(BuildConfigC());
        Assert.NotEqual(expected: expected3, actual: actual3);
    }

    [Fact]
    public void NullConfigHashesDistinctlyFromEmptyConfig()
    {
        string expected2 = Hash(null);
        string actual2 = Hash(new ImplicitSplitConfigBuilder().Build());
        Assert.NotEqual(expected: expected2, actual: actual2);
    }

    private static string Hash(ImplicitSplitConfig? config)
    {
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        hasher.AppendImplicitSplitConfig(config);
        return hasher.ToHexString();
    }

    private static ImplicitSplitConfig BuildConfigA()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.Grade = new(3);
            x.Specialization("A", Specialization.DJ);
            x.Specialization("B", Specialization.UI);
        });
        builder.Scope(x =>
        {
            x.Grade = new(2);
            var implicitSplitScopeBuilder = x;
            string course = "C";
            Alternative alternative = new("Alt");
            implicitSplitScopeBuilder.Alternative(course, alternative);
        });
        return builder.Build();
    }

    private static ImplicitSplitConfig BuildConfigB()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.Grade = new(2);
            var implicitSplitScopeBuilder = x;
            string course = "C";
            Alternative alternative = new("Alt");
            implicitSplitScopeBuilder.Alternative(course, alternative);
        });
        builder.Scope(x =>
        {
            x.Grade = new(3);
            x.Specialization("B", Specialization.UI);
            x.Specialization("A", Specialization.DJ);
        });
        return builder.Build();
    }

    private static ImplicitSplitConfig BuildConfigC()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.Grade = new(3);
            x.Specialization("A", Specialization.GA2D);
        });
        return builder.Build();
    }
}
