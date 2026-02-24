using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common.Config;

namespace Anton.LayeredData.Tests;

public sealed class SerializationTests
{
    [Fact]
    public async Task SerializationBackAndForthTest()
    {
        var services = new ServiceCollection();
        services.AddLayeredData();
        services.Replace(
            new ServiceDescriptor(
                typeof(TreeBuilder), typeof(TreeBuilder), ServiceLifetime.Transient));
        CredentialsSource.Register(services);
        RegistryConfig.Register(services);
        TeacherLayerConfig.Register(services);
        var sp = services.BuildServiceProvider();
        var t1 = sp.GetRequiredService<TreeBuilder>();
        var t1a = t1.Defaults.AddLayer(new("A"));

        var t2 = sp.GetRequiredService<TreeBuilder>();

        var value = new RegistryConfig
        {
            Credentials = new ValueCredentialsSource
            {
                Value = new()
                {
                    Login = "login",
                    Password = "pass",
                },
            },
            CommandProcessingConfig = CommandProcessingConfig.DryRun,
        };
        t1a.Builder<RegistryConfig>().Enable().SetValue(value);

        var serializer = sp.GetRequiredService<TreeSerializer>();
        var output = new MemoryStream();
        await serializer.SerializeValues([t1a.Node], output);

        output.Seek(0, SeekOrigin.Begin);
        await serializer.DeserializeValues(output, (layer, configs) =>
        {
            if (layer is { } l)
            {
                Assert.Equal("A", l.Value);
            }
            var config = Assert.Single(configs);
            Assert.Equal(RegistryConfig.Key.Value, config.Key);
            Assert.Equal(value, (RegistryConfig?) config.Value, Comparer.Instance);
            return t2.AddNode(new("A"));
        });

        var child0 = t2.BaseNode.ChildNodes[0];
        var config = child0.Get(RegistryConfig.Key).Value.GetValue();
        Assert.Equal(value, config, Comparer.Instance);
    }
}

file sealed class Comparer : IEqualityComparer<RegistryConfig?>
{
    public static readonly Comparer Instance = new();
    public bool Equals(RegistryConfig? x, RegistryConfig? y)
    {
        {
            if (ComparisonHelper.AtLeastOneIsNull(x, y, out bool bothNull))
            {
                return bothNull;
            }
        }
        if (!x.CommandProcessingConfig.ValueEquals(y.CommandProcessingConfig))
        {
            return false;
        }
        if (!x.Credentials.ValueEquals(y.Credentials, (a, b) =>
            {
                if (a.GetType() != b.GetType())
                {
                    return false;
                }
                // only supporting this currently
                if (a is not ValueCredentialsSource va)
                {
                    return true;
                }
                if (b is not ValueCredentialsSource vb)
                {
                    return true;
                }
                return va.Value.ValueEquals(vb.Value, (ca, cb) =>
                {
                    if (ca.Login != cb.Login)
                    {
                        return false;
                    }
                    if (ca.Password != cb.Password)
                    {
                        return false;
                    }
                    return true;
                });
            }))
        {
            return false;
        }
        if (!x.EquationCommandsDerivation.ValueEquals(
                y.EquationCommandsDerivation,
                (xv, yv) => xv.GetType() == yv.GetType()))
        {
            return false;
        }
        if (x.ExtraLessonInstanceAction != y.ExtraLessonInstanceAction)
        {
            return false;
        }
        return true;
    }

    public int GetHashCode(RegistryConfig obj) => obj.GetHashCode();
}
