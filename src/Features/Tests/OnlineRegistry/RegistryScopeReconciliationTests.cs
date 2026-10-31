using OnlineRegistry.OnlineRegistry.Impl;
using ScheduleLib.Builders;
using ScheduleLib.OnlineRegistry.Impl;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryScopeReconciliationTests
{
    [Fact]
    public void UnspecifiedLegacyTypeStillUpdatesWhenTheOwnedAudienceTimeChanged()
    {
        var local = new[]
        {
            Local(id: 0, hour: 9),
        };

        DateTime CalculateDate()
        {
            var date = local[0].DateTime.AddHours(-1);
            return date;
        }

        var saved = Remote(
                0,
                CalculateDate(),
                "Old") with
        {
            LessonType = LessonType.Unspecified,
        };
        var inferred = new Dictionary<Uri, LessonType>();
        var owners = RegistryScopeReconciliation.Assign(
            new ScheduleBuilder().Build(),
            local,
            [saved],
            ["A"],
            Scope,
            new AnyDayDerivation(),
            inferredTypes: inferred);
        var commands = RegistryScopeReconciliation.Derive(
                new ScheduleBuilder().Build(),
                local,
                [saved],
                owners,
                Scope,
                new AnyDayDerivation(),
                inferred)
            .ToArray();
        Assert.Equal(expected: LessonEquationCommandType.Update, actual: Assert.Single(commands).Type);
        Assert.Equal(expected: saved.ViewUri, actual: commands[0].Remote.ViewUri);
    }

    [Fact]
    public void FinalDerivationNeverUpdatesOneScopeWithAnotherScopesSession()
    {
        var local = new[]
        {
            Local(id: 0, hour: 9),
            Local(id: 1, hour: 11),
        };
        var saved = Remote(
            1,
            local[1].DateTime,
            "Old (B)");
        var owners = Assign(
            local,
            [saved],
            false);
        var commands = RegistryScopeReconciliation.Derive(
                new ScheduleBuilder().Build(),
                local,
                [saved],
                owners,
                Scope,
                new AnyDayDerivation())
            .ToArray();
        Assert.Contains(
            commands,
            command => command.Type == LessonEquationCommandType.Create && command.Local.LessonId == local[0].LessonId);
        var update = Assert.Single(commands.Where(command => command.Type == LessonEquationCommandType.Update));
        Assert.Equal(expected: local[1].LessonId, actual: update.Local.LessonId);
        Assert.Equal(expected: saved.ViewUri, actual: update.Remote.ViewUri);
    }

    [Fact]
    public void AnOldLabCannotBeRepurposedAsALectureInTheSameScope()
    {
        var local = new[]
        {
            Local(id: 0, hour: 9) with
            {
                RegistryLessonType = LessonType.Curs,
            },
        };
        var saved = Remote(
            0,
            local[0].DateTime,
            "Old (A)");
        var schedule = new ScheduleBuilder().Build();
        var local2 = local;
        RemoteLessonInstance[] remote = [saved];
        var owners = Assign(
            local,
            [saved],
            false);
        IReadOnlyDictionary<Uri, string> owners2 = owners;
        Func<LessonInstance, string> getScope = Scope;
        IEquationCommandsDerivation derivation = new AnyDayDerivation();
        var derive = RegistryScopeReconciliation.Derive(
            schedule,
            local2,
            remote,
            owners2,
            getScope,
            derivation);
        var commands = derive.ToArray();
        Assert.DoesNotContain(commands, command => command.Type == LessonEquationCommandType.Update);
        Assert.Contains(commands, command => command.Type == LessonEquationCommandType.Create);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScopedRowsCannotTakeTheMissingAudienceOccurrence(bool sameDay)
    {
        var local = new[]
        {
            Local(id: 0, hour: 9),
            Local(id: 1, hour: 11),
        };
        var saved = Remote(
            1,
            local[1].DateTime,
            "Second (B)");
        var owners = Assign(
            local,
            [saved],
            sameDay);
        Assert.Equal(expected: "B", actual: owners[saved.ViewUri]);
        var derivation = Derivation(sameDay);
        GetLessonEquationCommandsParams p = new(
            new ScheduleBuilder().Build(),
            [],
            [local[0]]);
        var deriveCommands = derivation.DeriveCommands(p);
        var first = deriveCommands.ToArray();
        Assert.Equal(expected: LessonEquationCommandType.Create, actual: Assert.Single(first).Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DuplicateLegacyRowsRemainAvailableForNormalReconciliation(bool sameDay)
    {
        var local = new[]
        {
            Local(id: 0, hour: 9),
        };
        var first = Remote(
            0,
            local[0].DateTime,
            "First");
        var duplicate = Remote(
            1,
            local[0].DateTime,
            "First");
        var owners = Assign(
            local,
            [first, duplicate],
            sameDay);
        Assert.Single(owners);
        var derivation = Derivation(sameDay);
        GetLessonEquationCommandsParams p = new(
            new ScheduleBuilder().Build(),
            [first, duplicate],
            local);
        var deriveCommands = derivation.DeriveCommands(p);
        var commands = deriveCommands.ToArray();
        Assert.Contains(commands, command => command.Type == LessonEquationCommandType.Delete);
    }

    [Fact]
    public void LegacyAssessmentCanUseUniqueSameDayAudienceWhenTimeChanged()
    {
        var local = new[]
        {
            Local(id: 0, hour: 9),
        };
        int id2 = 0;
        var date = local[0].DateTime.AddHours(-1);
        string topic = "Atestare 1";
        var assessment = Remote(
            id2,
            date,
            topic);
        string expected2 = "A";
        var assign = Assign(
            local,
            [assessment],
            true);
        var key = assessment.ViewUri;
        string actual2 = assign[key];
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void AssessmentWithTwoSameDayAudiencesIsReportedOnceAndHasNoGuessedOwner()
    {
        var local = new[]
        {
            Local(id: 0, hour: 9),
            Local(id: 1, hour: 11),
        };
        int id2 = 0;
        var date = local[0].DateTime.AddHours(-1);
        string topic = "Atestare 1";
        var assessment = Remote(
            id2,
            date,
            topic);
        var unresolved = new List<RemoteLessonInstance>();
        var owners = RegistryScopeReconciliation.Assign(
            new ScheduleBuilder().Build(),
            local,
            [assessment],
            ["A", "B"],
            Scope,
            new SameDayDerivation(),
            unresolved.Add);
        Assert.Empty(owners);
        var expected2 = assessment;
        var actual2 = Assert.Single(unresolved);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void SharedScopeMigrationCanRetainTheRowForItsRemainingAudience()
    {
        var local = new[]
        {
            Local(id: 1, hour: 11),
        };
        var saved = Remote(
            0,
            local[0].DateTime,
            "Topic (A, B)");
        var owners = RegistryScopeReconciliation.Assign(
            new ScheduleBuilder().Build(),
            local,
            [saved],
            ["A", "B", "A, B"],
            Scope,
            new AnyDayDerivation(),
            resolveScope: (_, scope) => scope == "A, B" ? "B" : scope);
        Assert.Equal(expected: "B", actual: owners[saved.ViewUri]);
    }

    private static Dictionary<Uri, string> Assign(
        LessonInstance[] local,
        RemoteLessonInstance[] remote,
        bool sameDay)
    {
        var schedule = new ScheduleBuilder().Build();
        var local2 = local;
        var remote2 = remote;
        IReadOnlyCollection<string> knownScopes = ["A", "B"];
        Func<LessonInstance, string> getScope = Scope;
        var derivation = Derivation(sameDay);
        var assign = RegistryScopeReconciliation.Assign(
            schedule,
            local2,
            remote2,
            knownScopes,
            getScope,
            derivation);
        return assign;
    }

    private static IEquationCommandsDerivation Derivation(bool sameDay) =>
        sameDay ? new SameDayDerivation() : new AnyDayDerivation();

    private static string Scope(LessonInstance lesson) => lesson.LessonId.Id == 0 ? "A" : "B";

    private static LessonInstance Local(int id, int hour)
    {
        return new()
        {
            LessonId = new WeeklyLessonId(id).AsAny(),
            DateTime = new(
                year: 2026,
                month: 10,
                day: 12,
                hour: hour,
                minute: 0,
                second: 0),
            Topic = "Topic",
            RegistryLessonType = LessonType.Lab,
            Attendance = null,
        };
    }

    private static RemoteLessonInstance Remote(
        int id,
        DateTime date,
        string topic)
    {
        string CalculateUriString()
        {
            string uriString = $"https://registry.test/view/{id}";
            return uriString;
        }

        string CalculateUriString2()
        {
            string uriString2 = $"https://registry.test/edit/{id}";
            return uriString2;
        }

        return new()
        {
            DateTime = date,
            Topic = topic,
            LessonType = LessonType.Lab,
            Attendance = [Attendance.NotPresent],
            ViewUri = new(CalculateUriString()),
            EditUri = new(CalculateUriString2()),
        };
    }
}
