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
        Assert.Equal(expected: "DJ2601 (ru), DJ2602 (en)", actual: scope);
    }

    [Fact]
    public void Format_DefaultGroupLanguageUsesRoTag()
    {
        var (schedule, groups) = ScheduleWith("IA2501");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        Assert.Equal(expected: "IA2501 (ro)", actual: scope);
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
        Assert.Equal(expected: "IA2501", actual: scope);
    }

    [Theory]
    [InlineData(1, "/I")]
    [InlineData(2, "/II")]
    public void Format_NumericSubgroupUsesRomanSuffix(int number, string suffix)
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var partition = new GroupPartitionKey(SubGroup.CreateNumeric(number), Specialization.All);
        var scope = RegistryTopicScope.Format(schedule, groups, partition);
        Assert.Equal(expected: $"DJ2601 (ru) {suffix}", actual: scope);
    }

    [Fact]
    public void Format_EngSubgroupUsesShortEnSuffix()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var partition = new GroupPartitionKey(SpecialSubGroups.Eng, Specialization.All);
        var scope = RegistryTopicScope.Format(schedule, groups, partition);
        Assert.Equal(expected: "DJ2601 (ru) /en", actual: scope);
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
        Assert.Equal(expected: "DJ2601 (ru) /alt:Antreprenoriat /spec:DJ /I", actual: scope);
    }

    [Fact]
    public void Format_OtherSubgroupUsesSubPrefix()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var partition = new GroupPartitionKey(SpecialSubGroups.Beginners, Specialization.All);
        var scope = RegistryTopicScope.Format(schedule, groups, partition);
        Assert.StartsWith(expectedStartString: "DJ2601 (ru) /sub:", actualString: scope);
    }

    [Fact]
    public void AppendAndTrySplit_RoundTripNestedLanguageParentheses()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];
        var appended = RegistryTopicScope.Append(topic: "Curs introductiv", scope: scope, known);
        Assert.Equal(expected: $"Curs introductiv ({scope})", actual: appended);
        Assert.True(RegistryTopicScope.TrySplit(topic: appended, known, baseTopic: out var baseTopic, scope: out var splitScope));
        Assert.Equal(expected: "Curs introductiv", actual: baseTopic);
        Assert.Equal(expected: scope, actual: splitScope);
    }

    [Fact]
    public void AppendAndTrySplit_RoundTripMultipleGroups()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)", "DJ2602(en)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];
        var appended = RegistryTopicScope.Append(topic: "Laborator", scope: scope, known);
        Assert.True(RegistryTopicScope.TrySplit(topic: appended, known, baseTopic: out var baseTopic, scope: out var splitScope));
        Assert.Equal(expected: "Laborator", actual: baseTopic);
        Assert.Equal(expected: scope, actual: splitScope);
    }

    [Fact]
    public void Append_ReplacesRecognizedSuffixAndIsIdempotent()
    {
        var (schedule, oldGroups) = ScheduleWith("DJ2601(ru)");
        var oldScope = RegistryTopicScope.Format(schedule, oldGroups, GroupPartitionKey.All);
        var newScope = RegistryTopicScope.Format(schedule, oldGroups,
            new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All));
        var known = (IReadOnlyCollection<string>) [oldScope, newScope];

        var replaced = RegistryTopicScope.Append(topic: $"Baza ({oldScope})", scope: newScope, known);
        Assert.Equal(expected: $"Baza ({newScope})", actual: replaced);

        var first = RegistryTopicScope.Append(topic: "Baza", scope: newScope, known);
        var second = RegistryTopicScope.Append(topic: first, scope: newScope, known);
        Assert.Equal(expected: first, actual: second);
    }

    [Fact]
    public void TrySplit_PreservesUnknownTrailingParentheses()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];

        Assert.False(RegistryTopicScope.TrySplit(topic: "Feature (optional)", known, baseTopic: out var baseTopic, scope: out var splitScope));
        Assert.Null(baseTopic);
        Assert.Null(splitScope);

        Assert.False(RegistryTopicScope.TrySplit(topic: "Topic (unrelated audience)", known, baseTopic: out _, scope: out _));

        var appended = RegistryTopicScope.Append(topic: "Feature (optional)", scope: scope, known);
        Assert.Equal(expected: $"Feature (optional) ({scope})", actual: appended);
    }

    [Fact]
    public void TrySplit_KeepsInternalParenthesesWhileRecognizingTerminalScope()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups, GroupPartitionKey.All);
        var known = (IReadOnlyCollection<string>) [scope];

        Assert.False(RegistryTopicScope.TrySplit(topic: "Intro (part 1) rest", known, baseTopic: out _, scope: out _));

        var appended = RegistryTopicScope.Append(topic: "Intro (part 1)", scope: scope, known);
        Assert.Equal(expected: $"Intro (part 1) ({scope})", actual: appended);
        Assert.True(RegistryTopicScope.TrySplit(topic: appended, known, baseTopic: out var baseTopic, scope: out var splitScope));
        Assert.Equal(expected: "Intro (part 1)", actual: baseTopic);
        Assert.Equal(expected: scope, actual: splitScope);
    }

    [Fact]
    public void TrySplit_DoesNotFuzzyMatchPartialScopes()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var full = RegistryTopicScope.Format(schedule, groups,
            new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All));
        var known = (IReadOnlyCollection<string>) [full];
        Assert.False(RegistryTopicScope.TrySplit(topic: "Baza (DJ2601)", known, baseTopic: out _, scope: out _));
        Assert.False(RegistryTopicScope.TrySplit(topic: "Baza (DJ2601 (ru))", known, baseTopic: out _, scope: out _));
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
        Assert.Equal(expected: topic, actual: RegistryTopicScope.Append(topic: topic, scope: scope, known));
        Assert.False(RegistryTopicScope.TrySplit(topic: topic, known, baseTopic: out _, scope: out _));
    }

    [Theory]
    [InlineData("DJ2601 (ru) /eng", "DJ2601 (ru) /en")]
    [InlineData("DJ2601 (eng)", "DJ2601 (en)")]
    [InlineData("DJ2601 (ru) /1", "DJ2601 (ru) /I")]
    public void TrySplit_SupportsBackwardAliasesForLanguageAndNumericPartitions(string alias, string canonical)
    {
        var known = (IReadOnlyCollection<string>) [canonical];
        var topic = $"Baza ({alias})";
        Assert.True(RegistryTopicScope.TrySplit(topic: topic, known, baseTopic: out var baseTopic, scope: out var splitScope));
        Assert.Equal(expected: "Baza", actual: baseTopic);
        Assert.Equal(expected: canonical, actual: splitScope);

        var replaced = RegistryTopicScope.Append(topic: topic, scope: canonical, known);
        Assert.Equal(expected: $"Baza ({canonical})", actual: replaced);
    }

    [Fact]
    public void TrySplit_MatchesKnownScopesCaseInsensitively()
    {
        var (schedule, groups) = ScheduleWith("DJ2601(ru)");
        var scope = RegistryTopicScope.Format(schedule, groups,
            new GroupPartitionKey(SubGroup.CreateNumeric(1), Specialization.All));
        var known = (IReadOnlyCollection<string>) [scope];
        var lower = $"Baza ({scope.ToLowerInvariant()})";
        Assert.True(RegistryTopicScope.TrySplit(topic: lower, known, baseTopic: out var baseTopic, scope: out var splitScope));
        Assert.Equal(expected: "Baza", actual: baseTopic);
        Assert.Equal(expected: scope, actual: splitScope);
    }
}
