using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry.Impl;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryScopeReconciliationTests
{
    [Fact]
    public void UnspecifiedLegacyTypeStillUpdatesWhenTheOwnedAudienceTimeChanged()
    {
        var local = new[] { Local(id: 0, hour: 9) };
        var saved = Remote(0, local[0].DateTime.AddHours(-1), "Old") with { LessonType = LessonType.Unspecified };
        var inferred = new Dictionary<Uri, LessonType>();
        var owners = RegistryScopeReconciliation.Assign(new ScheduleBuilder().Build(), local, [saved],
            ["A"], Scope, new AnyDayDerivation(), inferredTypes: inferred);
        var commands = RegistryScopeReconciliation.Derive(new ScheduleBuilder().Build(), local, [saved],
            owners, Scope, new AnyDayDerivation(), inferred).ToArray();
        Assert.Equal(expected: LessonEquationCommandType.Update, actual: Assert.Single(commands).Type);
        Assert.Equal(expected: saved.ViewUri, actual: commands[0].Remote.ViewUri);
    }

    [Fact]
    public void FinalDerivationNeverUpdatesOneScopeWithAnotherScopesSession()
    {
        var local = new[] { Local(id: 0, hour: 9), Local(id: 1, hour: 11) };
        var saved = Remote(1, local[1].DateTime, "Old (B)");
        var owners = Assign(local, [saved], false);
        var commands = RegistryScopeReconciliation.Derive(new ScheduleBuilder().Build(), local, [saved],
            owners, Scope, new AnyDayDerivation()).ToArray();
        Assert.Contains(commands, command => command.Type == LessonEquationCommandType.Create && command.Local.LessonId == local[0].LessonId);
        var update = Assert.Single(commands.Where(command => command.Type == LessonEquationCommandType.Update));
        Assert.Equal(expected: local[1].LessonId, actual: update.Local.LessonId);
        Assert.Equal(expected: saved.ViewUri, actual: update.Remote.ViewUri);
    }

    [Fact]
    public void AnOldLabCannotBeRepurposedAsALectureInTheSameScope()
    {
        var local = new[] { Local(id: 0, hour: 9) with { RegistryLessonType = LessonType.Curs } };
        var saved = Remote(0, local[0].DateTime, "Old (A)");
        var commands = RegistryScopeReconciliation.Derive(new ScheduleBuilder().Build(), local, [saved],
            Assign(local, [saved], false), Scope, new AnyDayDerivation()).ToArray();
        Assert.DoesNotContain(commands, command => command.Type == LessonEquationCommandType.Update);
        Assert.Contains(commands, command => command.Type == LessonEquationCommandType.Create);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedRowsCannotTakeTheMissingAudienceOccurrence(bool sameDay)
    {
        var local = new[] { Local(id: 0, hour: 9), Local(id: 1, hour: 11) };
        var saved = Remote(1, local[1].DateTime, "Second (B)");
        var owners = Assign(local, [saved], sameDay);
        Assert.Equal(expected: "B", actual: owners[saved.ViewUri]);
        var first = Derivation(sameDay).DeriveCommands(new(new ScheduleBuilder().Build(), [], [local[0]])).ToArray();
        Assert.Equal(expected: LessonEquationCommandType.Create, actual: Assert.Single(first).Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateLegacyRowsRemainAvailableForNormalReconciliation(bool sameDay)
    {
        var local = new[] { Local(id: 0, hour: 9) };
        var first = Remote(0, local[0].DateTime, "First");
        var duplicate = Remote(1, local[0].DateTime, "First");
        var owners = Assign(local, [first, duplicate], sameDay);
        Assert.Single(owners);
        var commands = Derivation(sameDay).DeriveCommands(new(new ScheduleBuilder().Build(), [first, duplicate], local)).ToArray();
        Assert.Contains(commands, command => command.Type == LessonEquationCommandType.Delete);
    }

    [Fact]
    public void LegacyAssessmentCanUseUniqueSameDayAudienceWhenTimeChanged()
    {
        var local = new[] { Local(id: 0, hour: 9) };
        var assessment = Remote(0, local[0].DateTime.AddHours(-1), "Atestare 1");
        Assert.Equal(expected: "A", actual: Assign(local, [assessment], true)[assessment.ViewUri]);
    }

    [Fact]
    public void AssessmentWithTwoSameDayAudiencesIsReportedOnceAndHasNoGuessedOwner()
    {
        var local = new[] { Local(id: 0, hour: 9), Local(id: 1, hour: 11) };
        var assessment = Remote(0, local[0].DateTime.AddHours(-1), "Atestare 1");
        var unresolved = new List<RemoteLessonInstance>();
        var owners = RegistryScopeReconciliation.Assign(new ScheduleBuilder().Build(), local, [assessment],
            ["A", "B"], Scope, new SameDayDerivation(), unresolved.Add);
        Assert.Empty(owners);
        Assert.Equal(expected: assessment, actual: Assert.Single(unresolved));
    }

    [Fact]
    public void SharedScopeMigrationCanRetainTheRowForItsRemainingAudience()
    {
        var local = new[] { Local(id: 1, hour: 11) };
        var saved = Remote(0, local[0].DateTime, "Topic (A, B)");
        var owners = RegistryScopeReconciliation.Assign(new ScheduleBuilder().Build(), local, [saved],
            ["A", "B", "A, B"], Scope, new AnyDayDerivation(), resolveScope: (_, scope) => scope == "A, B" ? "B" : scope);
        Assert.Equal(expected: "B", actual: owners[saved.ViewUri]);
    }

    private static Dictionary<Uri, string> Assign(LessonInstance[] local, RemoteLessonInstance[] remote, bool sameDay)
        => RegistryScopeReconciliation.Assign(new ScheduleBuilder().Build(), local, remote, ["A", "B"], Scope, Derivation(sameDay));

    private static IEquationCommandsDerivation Derivation(bool sameDay) => sameDay ? new SameDayDerivation() : new AnyDayDerivation();
    private static string Scope(LessonInstance lesson) => lesson.LessonId.Id == 0 ? "A" : "B";
    private static LessonInstance Local(int id, int hour)
    {
        return new()
        {
            LessonId = new WeeklyLessonId(id).AsAny(),
            DateTime = new(year: 2026, month: 10, day: 12, hour: hour, minute: 0, second: 0),
            Topic = "Topic",
            RegistryLessonType = LessonType.Lab,
            Attendance = null,
        };
    }

    private static RemoteLessonInstance Remote(int id, DateTime date, string topic)
    {
        return new()
        {
            DateTime = date,
            Topic = topic,
            LessonType = LessonType.Lab,
            Attendance = [Attendance.NotPresent],
            ViewUri = new($"https://registry.test/view/{id}"),
            EditUri = new($"https://registry.test/edit/{id}"),
        };
    }
}
