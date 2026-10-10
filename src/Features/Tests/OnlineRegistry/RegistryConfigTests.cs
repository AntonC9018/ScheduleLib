using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.OnlineRegistry;

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

    private static RegistryGroupMatchRule Rule(string faculty, string subGroup, Language language,
        bool matchByLanguage, bool allowParentGroup = false) => new()
    {
        Faculty = faculty,
        SubGroup = subGroup,
        Language = language,
        MatchByLanguage = matchByLanguage,
        AllowParentGroup = allowParentGroup,
    };
}
