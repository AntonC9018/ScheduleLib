using System.Security.Cryptography;
using ScheduleLib;
using ScheduleLib.Application.Core;
using ScheduleLib.ScheduleDefaults;

public sealed class ImplicitSplitCacheHashTests
{
    [Fact]
    public void CacheHashIgnoresDeclarationOrder()
    {
        Assert.Equal(Hash(BuildConfigA()), Hash(BuildConfigB()));
        Assert.NotEqual(Hash(BuildConfigA()), Hash(BuildConfigC()));
    }

    [Fact]
    public void NullConfigHashesDistinctlyFromEmptyConfig()
    {
        Assert.NotEqual(Hash(null), Hash(new ImplicitSplitConfigBuilder().Build()));
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
            x.Alternative("C", new("Alt"));
        });
        return builder.Build();
    }

    private static ImplicitSplitConfig BuildConfigB()
    {
        var builder = new ImplicitSplitConfigBuilder();
        builder.Scope(x =>
        {
            x.Grade = new(2);
            x.Alternative("C", new("Alt"));
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
