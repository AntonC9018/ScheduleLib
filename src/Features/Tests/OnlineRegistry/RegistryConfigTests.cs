using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Scraping.Common;
using ScheduleLib.Scraping.Common.Config;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistryConfigTests
{
    [Fact]
    public void LaterGroupMatchRuleReplacesMatchingFacultyAndSubGroupKey()
    {
        var services = new ServiceCollection();
        RegistryConfig.Register(services);
        using var provider = services.BuildServiceProvider();
        var merger = provider.GetRequiredService<IMerger<List<RegistryGroupMatchRule>>>();

        var existingRule = Rule("DJ", "eng", Language.En, matchByLanguage: true, allowParentGroup: true);
        var unrelatedRule = Rule("IA", "eng", Language.En, matchByLanguage: true);
        var replacementRule = Rule("DJ", "eng", Language.Ro, matchByLanguage: false);

        var result = merger.Merge([replacementRule], [existingRule, unrelatedRule]);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, rule => rule.Faculty == "DJ" && rule.SubGroup == "eng" &&
            rule.Language == Language.Ro && !rule.MatchByLanguage && !rule.AllowParentGroup);
        Assert.Contains(result, rule => rule.Faculty == "IA" && rule.SubGroup == "eng" &&
            rule.Language == Language.En && rule.MatchByLanguage);
    }

    [Fact]
    public void MapperUsesDefaultDelayWhenUnset()
    {
        var mapped = new RegistryConfigMapper().Map(ValidConfig());
        Assert.Equal(BuiltRegistryConfig.DefaultHttpCallDelay, mapped.HttpCallDelay);
    }

    [Fact]
    public void MapperPreservesCustomDelay()
    {
        var input = ValidConfig();
        input.HttpCallDelay = TimeSpan.FromSeconds(2);
        var mapped = new RegistryConfigMapper().Map(input);
        Assert.Equal(TimeSpan.FromSeconds(2), mapped.HttpCallDelay);
    }

    [Fact]
    public void MapperAllowsZeroDelayForExperiments()
    {
        var input = ValidConfig();
        input.HttpCallDelay = TimeSpan.Zero;
        var mapped = new RegistryConfigMapper().Map(input);
        Assert.Equal(TimeSpan.Zero, mapped.HttpCallDelay);
    }

    [Fact]
    public void MapperRejectsNegativeDelay()
    {
        var input = ValidConfig();
        input.HttpCallDelay = TimeSpan.FromSeconds(-1);
        Assert.Throws<InvalidOperationException>(() => new RegistryConfigMapper().Map(input));
    }

    private static RegistryConfig ValidConfig() => new()
    {
        Credentials = new ValueCredentialsSource { Value = new() { Login = "login", Password = "pass" } },
        EquationCommandsDerivation = new AnyDayDerivation(),
    };

    [Fact]
    public void LaterHttpCallDelayOverridesEarlierLayer()
    {
        var services = new ServiceCollection();
        RegistryConfig.Register(services);
        using var provider = services.BuildServiceProvider();
        var merger = provider.GetRequiredService<IMerger<RegistryConfig>>();

        var overridden = merger.Merge(
            new RegistryConfig { HttpCallDelay = TimeSpan.FromSeconds(1) },
            new RegistryConfig { HttpCallDelay = TimeSpan.FromSeconds(5) });
        Assert.Equal(TimeSpan.FromSeconds(1), overridden.HttpCallDelay);

        var inherit = merger.Merge(
            new RegistryConfig(),
            new RegistryConfig { HttpCallDelay = TimeSpan.FromSeconds(5) });
        Assert.Equal(TimeSpan.FromSeconds(5), inherit.HttpCallDelay);
    }

    [Fact]
    public void HistoryHandlerAllowsZeroDelayForExperiments()
    {
        using var inner = new StubHandler();
        var index = new Uri("https://registry.test/studregistry/LessonAttendance");
        using var guard = new RegistryHistoryRequestHandler(inner, index, new(index, "../Account/Login"), TimeSpan.Zero);
        guard.CompleteAuthentication();
        Assert.NotNull(guard);
    }

    [Fact]
    public void HistoryHandlerRejectsNegativeDelay()
    {
        using var inner = new StubHandler();
        var index = new Uri("https://registry.test/studregistry/LessonAttendance");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new RegistryHistoryRequestHandler(inner, index, new(index, "../Account/Login"), TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void RegistryScrapingDelayValidationRejectsNegativeSpacing()
    {
        var builder = new ScrapingContextBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RegistryScraping.AddDefaultConfigWithoutHandlers(builder, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void RegistryScrapingDelayValidationAllowsZeroSpacing()
    {
        var builder = new ScrapingContextBuilder();
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder, TimeSpan.Zero);
    }

    private static RegistryGroupMatchRule Rule(string faculty, string subGroup, Language language,
        bool matchByLanguage, bool allowParentGroup = false) => new()
    {
        Faculty = faculty,
        SubGroup = subGroup,
        Language = language,
        MatchByLanguage = matchByLanguage,
        AllowParentGroup = allowParentGroup,
    };

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }
}
