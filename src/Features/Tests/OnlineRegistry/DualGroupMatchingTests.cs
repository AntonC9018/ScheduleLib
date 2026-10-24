using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class DualGroupMatchingTests
{
    [Theory]
    [InlineData("IA2601", 0)]
    [InlineData("IA2601DU", 1)]
    [InlineData("IA2601(Dual)", 1)]
    [InlineData("IA2601fr", 2)]
    [InlineData("IA2601DUfr", 3)]
    [InlineData("IA2601frDU", 3)]
    [InlineData("IAFR2601DU", 3)]
    [InlineData("IAFR2601(Dual)", 3)]
    [InlineData("DJ2601DU", 4)]
    public void CurrentRegistrySelectsExactProgramAndMode(string name, int expectedGroup)
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var builder = new ScheduleBuilder { GroupParseContext = context };
        foreach (var group in new[] { "IA2601", "DU-IA2601", "IAFR2601", "DU-IAFR2601", "DU-DJ2601" })
            builder.Group(group);
        var parsed = RegistryScraping.ParseGroupFromOnlineRegistry(context, name);
        var match = GroupsNavigator.FindGroupMatch(builder.Build(), parsed);
        Assert.Equal(expected: new GroupId(expectedGroup), actual: Assert.Single(match));
    }

    [Fact]
    public void ConfirmedLegacyAliasMatchesThisYearsDualRegistry()
    {
        var context = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var builder = new ScheduleBuilder { GroupParseContext = context };
        Config.ConfigureRemappings(builder.Remappings);
        builder.Group("DU2501");
        builder.Group("IA2501");
        var parsed = RegistryScraping.ParseGroupFromOnlineRegistry(context, "IA2501DU");
        Assert.Equal(expected: 2, actual: parsed.Grade.Value);
        Assert.Equal(expected: new GroupId(0), actual: Assert.Single(GroupsNavigator.FindGroupMatch(builder.Build(), parsed)));
    }
}
