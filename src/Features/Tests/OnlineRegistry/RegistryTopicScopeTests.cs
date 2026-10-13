using ScheduleLib.Builders;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryTopicScopeTests
{
    private static (Schedule Schedule, LessonGroups Groups) ScheduleWith(params string[] groupNames)
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var groups = new LessonGroups();
        foreach (var name in groupNames)
        {
            groups.Add(builder.Group(name).Id);
        }
        return (builder.Build(), groups);
    }

    [Fact]
    public void Format_ListsOrderedParentsWithTeachingLanguages()
    {
        var (schedule, groups) = ScheduleWith("DJ2602(en)", "DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        Assert.Equal("DJ2601 (ru), DJ2602 (en)", scope);
    }

    [Fact]
    public void Format_DefaultGroupLanguageUsesRoTag()
    {
        var (schedule, groups) = ScheduleWith("IA2501");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        Assert.Equal("IA2501 (ro)", scope);
    }

    [Fact]
    public void Format_UnknownGroupLanguageOmitsTag()
    {
        var builder = new ScheduleBuilder();
        builder.GroupParseContext = GroupParseContext.Create(new() { CurrentStudyYear = new(2026) });
        var group = builder.Group("IA2501");
        group.Ref.Language = Language.None;
        var groups = new LessonGroups();
        groups.Add(group.Id);
        var scope = RegistryTopicScope.Format(builder.Build(), groups, GroupPartitionKey.All);
        Assert.Equal("IA2501", scope);
    }

    [Theory]
    [InlineData(1, "/I")]
    [InlineData(2, "/II")]
    public void Format_NumericSubgroupUsesRomanSuffix(int number, string suffix)
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var partition = new GroupPartitionKey(SubGroup.CreateNumeric(number), Specialization.All);
        var scope = RegistryTopicScope.Format(schedule, groups, partition);
        Assert.Equal($"DJ2601 (ru) {suffix}", scope);
    }

    [Fact]
    public void Format_EngSubgroupUsesShortEnSuffix()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var partition = new GroupPartitionKey(SpecialSubGroups.Eng, Specialization.All);
        var scope = RegistryTopicScope.Format(schedule, groups, partition);
        Assert.Equal("DJ2601 (ru) /en", scope);
    }

    [Fact]
    public void Format_SpecializationAndAlternativeUseUnambiguousPrefixes()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var partition = new GroupPartitionKey(
            SubGroup.CreateNumeric(1),
            new Specialization("DJ"),
            new Alternative("Antreprenoriat"));
        var scope = RegistryTopicScope.Format(schedule, groups, partition);
        Assert.Equal("DJ2601 (ru) /alt:Antreprenoriat /spec:DJ /I", scope);
    }

    [Fact]
    public void Format_OtherSubgroupUsesSubPrefix()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var partition = new GroupPartitionKey(SpecialSubGroups.Beginners, Specialization.All);
        var scope = RegistryTopicScope.Format(schedule, groups, partition);
        Assert.StartsWith("DJ2601 (ru) /sub:", scope);
    }

    [Fact]
    public void AppendAndTrySplit_RoundTripNestedLanguageParentheses()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];
        var appended = RegistryTopicScope.Append("Curs introductiv", scope, known);
        Assert.Equal($"Curs introductiv ({scope})", appended);
        Assert.True(RegistryTopicScope.TrySplit(appended, known, out var baseTopic, out var splitScope));
        Assert.Equal("Curs introductiv", baseTopic);
        Assert.Equal(scope, splitScope);
    }

    [Fact]
    public void AppendAndTrySplit_RoundTripMultipleGroups()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)", "DJ2602(en)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];
        var appended = RegistryTopicScope.Append("Laborator", scope, known);
        Assert.True(RegistryTopicScope.TrySplit(appended, known, out var baseTopic, out var splitScope));
        Assert.Equal("Laborator", baseTopic);
        Assert.Equal(scope, splitScope);
    }

    [Fact]
    public void Append_ReplacesRecognizedSuffixAndIsIdempotent()
    {
        var (schedule, oldGroups) = ScheduleWith("DJ2601(ru)");
        var oldScope = RegistryTopicScope.Format(schedule, oldGroups, GroupPartitionKey.All);
        var newScope = RegistryTopicScope.Format(schedule, oldGroups,
            new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All));
        var known = (IReadOnlyCollection<string>) [oldScope, newScope];

        var replaced = RegistryTopicScope.Append($"Baza ({oldScope})", newScope, known);
        Assert.Equal($"Baza ({newScope})", replaced);

        var first = RegistryTopicScope.Append("Baza", newScope, known);
        var second = RegistryTopicScope.Append(first, newScope, known);
        Assert.Equal(first, second);
    }

    [Fact]
    public void TrySplit_PreservesUnknownTrailingParentheses()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];

        Assert.False(RegistryTopicScope.TrySplit("Feature (optional)", known, out var baseTopic, out var splitScope));
        Assert.Null(baseTopic);
        Assert.Null(splitScope);

        Assert.False(RegistryTopicScope.TrySplit("Topic (unrelated audience)", known, out _, out _));

        var appended = RegistryTopicScope.Append("Feature (optional)", scope, known);
        Assert.Equal($"Feature (optional) ({scope})", appended);
    }

    [Fact]
    public void TrySplit_KeepsInternalParenthesesWhileRecognizingTerminalScope()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];

        Assert.False(RegistryTopicScope.TrySplit("Intro (part 1) rest", known, out _, out _));

        var appended = RegistryTopicScope.Append("Intro (part 1)", scope, known);
        Assert.Equal($"Intro (part 1) ({scope})", appended);
        Assert.True(RegistryTopicScope.TrySplit(appended, known, out var baseTopic, out var splitScope));
        Assert.Equal("Intro (part 1)", baseTopic);
        Assert.Equal(scope, splitScope);
    }

    [Fact]
    public void TrySplit_DoesNotFuzzyMatchPartialScopes()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var full = RegistryTopicScope.Format(schedule, groups,
            new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All));
        var known = (IReadOnlyCollection<string>) [full];
        Assert.False(RegistryTopicScope.TrySplit("Baza (DJ2601)", known, out _, out _));
        Assert.False(RegistryTopicScope.TrySplit("Baza (DJ2601 (ru))", known, out _, out _));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Append_ReturnsBlankTopicsUnchanged(string? topic)
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];
        Assert.Equal(topic, RegistryTopicScope.Append(topic, scope, known));
        Assert.False(RegistryTopicScope.TrySplit(topic, known, out _, out _));
    }

    [Theory]
    [InlineData("DJ2601 (ru) /eng", "DJ2601 (ru) /en")]
    [InlineData("DJ2601 (eng)", "DJ2601 (en)")]
    [InlineData("DJ2601 (ru) /1", "DJ2601 (ru) /I")]
    public void TrySplit_SupportsBackwardAliasesForLanguageAndNumericPartitions(string alias, string canonical)
    {
        var known = (IReadOnlyCollection<string>) [canonical];
        var topic = $"Baza ({alias})";
        Assert.True(RegistryTopicScope.TrySplit(topic, known, out var baseTopic, out var splitScope));
        Assert.Equal("Baza", baseTopic);
        Assert.Equal(canonical, splitScope);

        var replaced = RegistryTopicScope.Append(topic, canonical, known);
        Assert.Equal($"Baza ({canonical})", replaced);
    }

    [Fact]
    public void TrySplit_MatchesKnownScopesCaseInsensitively()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups,
            new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All));
        var known = (IReadOnlyCollection<string>) [scope];
        var lower = $"Baza ({scope.ToLowerInvariant()})";
        Assert.True(RegistryTopicScope.TrySplit(lower, known, out var baseTopic, out var splitScope));
        Assert.Equal("Baza", baseTopic);
        Assert.Equal(scope, splitScope);
    }
}
