using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace Anton.LayeredData.Tests;

public sealed class SerializationTests
{
    [Fact]
    public async Task SerializationBackAndForthTest()
    {
        var services = new ServiceCollection();
        services.AddLayeredData();
        var serviceCollection = services;
        var descriptor = new ServiceDescriptor(
            serviceType: typeof(TreeBuilder),
            implementationType: typeof(TreeBuilder),
            ServiceLifetime.Transient);
        serviceCollection.Replace(descriptor);
        CredentialsSource.Register(services);
        RegistryConfig.Register(services);
        TeacherLayerConfig.Register(services);
        var sp = services.BuildServiceProvider();
        var t1 = sp.GetRequiredService<TreeBuilder>();

        var t1a = t1.Defaults.AddLayer(new Layer("A"));

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
        t1a.Builder<RegistryConfig>()
            .Enable()
            .SetValue(value);

        var serializer = sp.GetRequiredService<TreeSerializer>();
        var output = new MemoryStream();
        await serializer.SerializeValues([t1a.Node], output);

        output.Seek(0, SeekOrigin.Begin);
        await serializer.DeserializeValues(
            output,
            (layer, configs) =>
            {
                if (layer is { } l)
                {
                    Assert.Equal(expected: "A", actual: l.Value);
                }
                var config = Assert.Single(configs);
                Assert.Equal(expected: RegistryConfig.Key.Value, actual: config.Key);
                Assert.Equal(
                    expected: value,
                    actual: (RegistryConfig?) config.Value,
                    Comparer.Instance);
                var treeBuilder = t2;
                Layer layer2 = new("A");
                var addNode = treeBuilder.AddNode(layer2);
                return addNode;
            });

        var child0 = t2.BaseNode.ChildNodes[0];
        var config = child0.Get(RegistryConfig.Key).Value
            .GetValue();
        Assert.Equal(
            expected: value,
            actual: config,
            Comparer.Instance);
    }
}

file sealed class Comparer : IEqualityComparer<RegistryConfig?>
{
    public static readonly Comparer Instance = new();

    public bool Equals(RegistryConfig? x, RegistryConfig? y)
    {
        {
            if (ComparisonHelper.AtLeastOneIsNull(
                    x: x,
                    y: y,
                    out bool bothNull))
            {
                return bothNull;
            }
        }
        if (!x.CommandProcessingConfig.ValueEquals(y.CommandProcessingConfig))
        {
            return false;
        }
        if (!x.Credentials.ValueEquals(y.Credentials, CredentialsSourcesMatch))
        {
            return false;
        }
        if (!x.EquationCommandsDerivation.ValueEquals(
                y.EquationCommandsDerivation,
                (xv, yv) => xv.GetType() == yv.GetType()))
        {
            return false;
        }

        if (x.ExtraLessonInstanceAction == y.ExtraLessonInstanceAction)
        {
            return true;
        }

        return false;
    }

    private static bool CredentialsSourcesMatch(CredentialsSource expected, CredentialsSource actual)
    {
        if (expected.GetType() != actual.GetType())
        {
            return false;
        }

        // Other sources are compared by type alone.
        if (expected is not ValueCredentialsSource expectedValue)
        {
            return true;
        }
        if (actual is not ValueCredentialsSource actualValue)
        {
            return true;
        }

        static bool CredentialsMatch(Credentials expected, Credentials actual)
        {
            return expected.Login == actual.Login && expected.Password == actual.Password;
        }

        if (expectedValue.Value.ValueEquals(actualValue.Value, CredentialsMatch))
        {
            return true;
        }

        return false;
    }

    public int GetHashCode(RegistryConfig obj) => obj.GetHashCode();
}
