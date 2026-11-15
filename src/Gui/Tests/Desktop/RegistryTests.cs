using Anton.LayeredData;
using Desktop.NodeData.Common;
using Desktop.NodeData.Features.Registry;
using Microsoft.Extensions.DependencyInjection;

namespace Desktop.Tests;

public sealed class RegistryTests
{
    [Fact]
    public void DefaultRegistryResolvesWithValidatedServicesAndDefaultConfiguration()
    {
        var services = new ServiceCollection();
        services.AddRegistry<string>(options => options.Add(name: "Initial", value: "initial"));
        services.ConfigureRegistry<string>(options => options.Add(name: "Configured", value: "configured"));

        using var provider = AppConfiguration.BuildServiceProvider(services);
        var registry = provider.GetRequiredService<Registry<string>>();

        Assert.Collection(
            registry.Values,
            entry => Assert.Equal(expected: "initial", actual: entry.Value),
            entry => Assert.Equal(expected: "configured", actual: entry.Value));
        Assert.Same(expected: registry, actual: provider.GetRequiredService<Registry<string>>());
    }

    [Fact]
    public void NamedRegistryKeepsConfigurationSeparateFromDefaultRegistry()
    {
        var services = new ServiceCollection();
        var registryKey = new RegistryKey(
            new NodeDataKey("TestRegistry"),
            new PropertyId("NamedValues"));
        var namedKey = new RegistryKey<string>(registryKey);
        services.AddRegistry<string>(options => options.Add(name: "Default", value: "default"));
        services.AddRegistry(namedKey, options => options.Add(name: "Named", value: "named"));
        services.ConfigureRegistry<string>(options => options.Add(name: "Default extra", value: "default-extra"));
        services.ConfigureRegistry(namedKey, options => options.Add(name: "Named extra", value: "named-extra"));

        using var provider = AppConfiguration.BuildServiceProvider(services);
        var defaultRegistry = provider.GetRequiredService<Registry<string>>();
        var optionKey = namedKey.ToString();
        var namedRegistry = provider.GetRequiredKeyedService<Registry<string>>(optionKey);

        Assert.Collection(
            defaultRegistry.Values,
            entry => Assert.Equal(expected: "default", actual: entry.Value),
            entry => Assert.Equal(expected: "default-extra", actual: entry.Value));
        Assert.Collection(
            namedRegistry.Values,
            entry => Assert.Equal(expected: "named", actual: entry.Value),
            entry => Assert.Equal(expected: "named-extra", actual: entry.Value));
        Assert.NotSame(expected: defaultRegistry, actual: namedRegistry);
        Assert.Same(
            expected: namedRegistry,
            actual: provider.GetRequiredKeyedService<Registry<string>>(optionKey));
    }

    [Fact]
    public void FindThrowsUnregisteredPropertyValueExceptionForStringAndEnum()
    {
        var services = new ServiceCollection();
        services.AddRegistry<string>(options => options.Add(name: "Known", value: "known"));
        services.AddRegistry<DayOfWeek>(options => options.Add(name: "Monday", value: DayOfWeek.Monday));

        using var provider = AppConfiguration.BuildServiceProvider(services);
        var stringRegistry = provider.GetRequiredService<Registry<string>>();
        var enumRegistry = provider.GetRequiredService<Registry<DayOfWeek>>();

        var stringError = Assert.Throws<UnregisteredPropertyValueException>(() => stringRegistry.Find("missing"));
        Assert.Equal(expected: typeof(string), actual: stringError.PropertyType);
        Assert.Equal(expected: typeof(string), actual: stringError.ValueType);
        Assert.Equal(expected: "missing", actual: stringError.ValueText);

        var nullError = Assert.Throws<UnregisteredPropertyValueException>(() => stringRegistry.Find(null!));
        Assert.Equal(expected: typeof(string), actual: nullError.PropertyType);
        Assert.Null(nullError.ValueType);
        Assert.Null(nullError.ValueText);

        var enumError = Assert.Throws<UnregisteredPropertyValueException>(() => enumRegistry.Find(DayOfWeek.Friday));
        Assert.Equal(expected: typeof(DayOfWeek), actual: enumError.PropertyType);
        Assert.Equal(expected: typeof(DayOfWeek), actual: enumError.ValueType);
        Assert.Equal(expected: "Friday", actual: enumError.ValueText);
    }

    [Fact]
    public void FindThrowsWithoutInvokingArbitraryToStringOnUnregisteredObject()
    {
        var services = new ServiceCollection();
        services.AddRegistry<object>(options => options.Add(name: "Known", value: new object()));

        using var provider = AppConfiguration.BuildServiceProvider(services);
        var registry = provider.GetRequiredService<Registry<object>>();
        var throwingValue = new ThrowingFormattableValue();

        var error = Assert.Throws<UnregisteredPropertyValueException>(() => registry.Find(throwingValue));
        var expectedTypeText = typeof(ThrowingFormattableValue).ToString();
        Assert.Equal(expected: typeof(object), actual: error.PropertyType);
        Assert.Equal(expected: typeof(ThrowingFormattableValue), actual: error.ValueType);
        Assert.Null(error.ValueText);
        Assert.Contains(
            expectedSubstring: expectedTypeText,
            actualString: error.Message);
    }

    private sealed class ThrowingFormattableValue
    {
        public override string ToString() => throw new InvalidOperationException("ToString should not be called.");
    }
}
