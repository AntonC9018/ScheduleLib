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

        var existingRule = Rule(faculty: "DJ",
            subGroup: "eng",
            Language.En,
            matchByLanguage: true,
            allowParentGroup: true);
        var unrelatedRule = Rule(faculty: "IA",
            subGroup: "eng",
            Language.En,
            matchByLanguage: true);
        var replacementRule = Rule(faculty: "DJ",
            subGroup: "eng",
            Language.Ro,
            matchByLanguage: false);

        var result = merger.Merge(@from: [replacementRule], @into: [existingRule, unrelatedRule]);

        Assert.Equal(expected: 2, actual: result.Count);
        Assert.Contains(result,
            rule =>
            {
                bool CheckCondition()
                {
                    if (rule.Faculty != "DJ")
                    {
                        return false;
                    }

                    if (rule.SubGroup != "eng")
                    {
                        return false;
                    }

                    if (rule.Language != Language.Ro)
                    {
                        return false;
                    }

                    if (rule.MatchByLanguage)
                    {
                        return false;
                    }

                    if (!rule.AllowParentGroup)
                    {
                        return true;
                    }

                    return false;
                }

                return CheckCondition();
            });
        Assert.Contains(result,
            rule =>
            {
                if (rule.Faculty != "IA")
                {
                    return false;
                }
                if (rule.SubGroup != "eng")
                {
                    return false;
                }
                if (rule.Language != Language.En)
                {
                    return false;
                }
                return rule.MatchByLanguage;
            });
    }

    [Fact]
    public void MapperUsesDefaultDelayWhenUnset()
    {
        var mapped = new RegistryConfigMapper().Map(ValidConfig());
        Assert.Equal(expected: BuiltRegistryConfig.DefaultHttpCallDelay, actual: mapped.HttpCallDelay);
    }

    [Fact]
    public void MapperPreservesCustomDelay()
    {
        var input = ValidConfig();
        input.HttpCallDelay = TimeSpan.FromSeconds(2);
        var mapped = new RegistryConfigMapper().Map(input);
        var expected2 = TimeSpan.FromSeconds(2);
        var actual2 = mapped.HttpCallDelay;
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void MapperAllowsZeroDelayForExperiments()
    {
        var input = ValidConfig();
        input.HttpCallDelay = TimeSpan.Zero;
        var mapped = new RegistryConfigMapper().Map(input);
        Assert.Equal(expected: TimeSpan.Zero, actual: mapped.HttpCallDelay);
    }

    [Fact]
    public void MapperRejectsNegativeDelay()
    {
        var input = ValidConfig();
        input.HttpCallDelay = TimeSpan.FromSeconds(-1);
        Assert.Throws<InvalidOperationException>(() => new RegistryConfigMapper().Map(input));
    }

    private static RegistryConfig ValidConfig()
    {
        return new()
        {
            Credentials = new ValueCredentialsSource
            {
                Value = new()
                {
                    Login = "login",
                    Password = "pass",
                },
            },
            EquationCommandsDerivation = new AnyDayDerivation(),
        };
    }

    [Fact]
    public void LaterHttpCallDelayOverridesEarlierLayer()
    {
        var services = new ServiceCollection();
        RegistryConfig.Register(services);
        using var provider = services.BuildServiceProvider();
        var merger = provider.GetRequiredService<IMerger<RegistryConfig>>();

        var overridden = merger.Merge(
            @from: new RegistryConfig
            {
                HttpCallDelay = TimeSpan.FromSeconds(1),
            },
            @into: new RegistryConfig
            {
                HttpCallDelay = TimeSpan.FromSeconds(5),
            });
        var expected2 = TimeSpan.FromSeconds(1);
        TimeSpan? expected3 = expected2;
        var actual2 = overridden.HttpCallDelay;
        Assert.Equal(expected: expected3, actual: actual2);

        var inherit = merger.Merge(
            @from: new RegistryConfig(),
            @into: new RegistryConfig
            {
                HttpCallDelay = TimeSpan.FromSeconds(5),
            });
        var expected4 = TimeSpan.FromSeconds(5);
        TimeSpan? expected5 = expected4;
        var actual3 = inherit.HttpCallDelay;
        Assert.Equal(expected: expected5, actual: actual3);
    }

    [Fact]
    public void HistoryHandlerAllowsZeroDelayForExperiments()
    {
        using var inner = new StubHandler();
        var index = new Uri("https://registry.test/studregistry/LessonAttendance");

        Uri CalculateLogin()
        {
            Uri login2 = new(index, "../Account/Login");
            return login2;
        }

        using var guard = new RegistryHistoryRequestHandler(inner,
            index: index,
            login: CalculateLogin(),
            TimeSpan.Zero);
        guard.CompleteAuthentication();
        Assert.NotNull(guard);
    }

    [Fact]
    public void HistoryHandlerRejectsNegativeDelay()
    {
        using var inner = new StubHandler();
        var index = new Uri("https://registry.test/studregistry/LessonAttendance");
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            HttpMessageHandler inner2 = inner;
            var index2 = index;
            Uri login2 = new(index, "../Account/Login");
            var spacing = TimeSpan.FromSeconds(-1);
            var registryHistoryRequestHandler = new RegistryHistoryRequestHandler(inner2,
                index: index2,
                login: login2,
                spacing);
            return registryHistoryRequestHandler;
        });
    }

    [Fact]
    public void RegistryScrapingDelayValidationRejectsNegativeSpacing()
    {
        var builder = new ScrapingContextBuilder();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            var b = builder;
            var delay = TimeSpan.FromSeconds(-1);
            RegistryScraping.AddDefaultConfigWithoutHandlers(b, delay);
        });
    }

    [Fact]
    public void RegistryScrapingDelayValidationAllowsZeroSpacing()
    {
        var builder = new ScrapingContextBuilder();
        RegistryScraping.AddDefaultConfigWithoutHandlers(builder, TimeSpan.Zero);
    }

    private static RegistryGroupMatchRule Rule(
        string faculty,
        string subGroup,
        Language language,
        bool matchByLanguage,
        bool allowParentGroup = false)
    {
        return new()
        {
            Faculty = faculty,
            SubGroup = subGroup,
            Language = language,
            MatchByLanguage = matchByLanguage,
            AllowParentGroup = allowParentGroup,
        };
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var result = new HttpResponseMessage(System.Net.HttpStatusCode.OK);
            var fromResult = Task.FromResult(result);
            return fromResult;
        }
    }
}
