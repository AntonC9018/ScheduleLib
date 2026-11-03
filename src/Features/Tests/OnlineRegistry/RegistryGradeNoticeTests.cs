using System.Text.Json;
using ScheduleLib.OnlineRegistry;
using ScheduleLib.Parsing;

namespace ScheduleLib.Tests.OnlineRegistry;

public sealed class RegistryGradeNoticeTests
{
    /// <summary>Each factory pins one event's category and exact message text.</summary>
    public static TheoryData<Func<RegistryGradeNotice>, RegistryGradeNoticeKind, string> Factories
    {
        get
        {
            return new()
            {
                {
                    () => RegistryGradeNotice.MoodleNameUnparsed("Ion Pop"), RegistryGradeNoticeKind.Omitted,
                    "Moodle name could not be parsed: Ion Pop"
                },
                {
                    () => RegistryGradeNotice.MoodleGradeMissing("Ion Pop"), RegistryGradeNoticeKind.Omitted,
                    "Moodle student has no grade: Ion Pop"
                },
                {
                    () => RegistryGradeNotice.MoodleGradeUnsupported("Ion Pop"), RegistryGradeNoticeKind.Unsupported,
                    "Moodle grade is not a finite supported number: Ion Pop"
                },
                {
                    () => RegistryGradeNotice.MoodleAttemptSuperseded(NameFrom(lastName: "Pop", firstName: "Ion")),
                    RegistryGradeNoticeKind.Omitted, "Earlier attempt replaced by the last graded attempt: Pop Ion"
                },
                {
                    RegistryGradeNotice.MoodlePathUnparsed, RegistryGradeNoticeKind.Unsupported,
                    "Moodle course/test path could not be parsed."
                },
                {
                    RegistryGradeNotice.MoodleCourseUnresolved, RegistryGradeNoticeKind.Unsupported,
                    "Moodle course name does not resolve in the configured schedule."
                },
                {
                    () => RegistryGradeNotice.RegistryGroupUnsupported(EvaluationUri),
                    RegistryGradeNoticeKind.Unsupported,
                    "Wildcard or empty registry group is unsupported: https://registry.test/groups/ia-2501"
                },
                {
                    () => RegistryGradeNotice.RegistryTestMissing(7, EvaluationUri),
                    RegistryGradeNoticeKind.Unsupported,
                    "Testarea 7 is missing for https://registry.test/groups/ia-2501"
                },
                {
                    () => RegistryGradeNotice.RegistryStudentExpelled("Pop Ion exmatr"),
                    RegistryGradeNoticeKind.Omitted, "Expelled registry student: Pop Ion exmatr"
                },
                {
                    () => RegistryGradeNotice.RegistryNameUnparsed("???"), RegistryGradeNoticeKind.Unsupported,
                    "Registry name could not be parsed: ???"
                },
                {
                    () => RegistryGradeNotice.RegistryNameTrailingTextUnsupported("Pop Ion"),
                    RegistryGradeNoticeKind.Unsupported, "Registry name contains unsupported trailing text: Pop Ion"
                },
                {
                    () => RegistryGradeNotice.RegistryStudentUnmatched(NameFrom(lastName: "Pop", firstName: "Ion")),
                    RegistryGradeNoticeKind.Unmatched, "Registry student has no matching graded Moodle attempt: Pop Ion"
                },
                {
                    () => RegistryGradeNotice.NoGradesToSubmit("https://registry.test/tests/7"),
                    RegistryGradeNoticeKind.Omitted, "No mapped grades to submit for https://registry.test/tests/7"
                },
                {
                    RegistryGradeNotice.NoTestFormsPlanned, RegistryGradeNoticeKind.Unmatched,
                    "No registry test forms with supported mapped grades were planned."
                },
                {
                    () => RegistryGradeNotice.MoodleStudentUnmatched(NameFrom(lastName: "Pop", firstName: "Ion"), 3),
                    RegistryGradeNoticeKind.Unmatched, "Moodle student was not matched in registry: Pop Ion (3)"
                },
            };
        }
    }

    [Theory]
    [MemberData(nameof(Factories))]
    public void FactoriesOwnTheCategoryAndMessage(
        Func<RegistryGradeNotice> create,
        RegistryGradeNoticeKind kind,
        string detail)
    {
        var notice = create();
        Assert.Equal(expected: kind, actual: notice.Kind);
        Assert.Equal(expected: detail, actual: notice.Detail);
    }

    [Fact]
    public void UnknownKindMembersHaveNoWireName()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ((RegistryGradeNoticeKind) 100).ToWire());
    }

    [Fact]
    public void NoticeJsonKeepsTheWireCategoryNames()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var notice = RegistryGradeNotice.RegistryStudentExpelled("Pop Ion exmatr");
        string json = JsonSerializer.Serialize(notice, options);
        Assert.Equal(
            expected:
            """{"kind":"omitted","detail":"Expelled registry student: Pop Ion exmatr"}""",
            actual: json);
    }

    [Theory]
    [InlineData(RegistryGradeNoticeKind.Omitted, "\"omitted\"")]
    [InlineData(RegistryGradeNoticeKind.Unsupported, "\"unsupported\"")]
    [InlineData(RegistryGradeNoticeKind.Unmatched, "\"unmatched\"")]
    public void KindJsonKeepsTheWireName(RegistryGradeNoticeKind kind, string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        Assert.Equal(
            expected: json,
            actual: JsonSerializer.Serialize(kind, options));
    }

    [Fact]
    public void KindJsonWritesDictionaryKeysAsWireNames()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
        var byKind = new Dictionary<RegistryGradeNoticeKind, int>
        {
            [RegistryGradeNoticeKind.Unmatched] = 2,
        };
        string json = JsonSerializer.Serialize(byKind, options);
        Assert.Equal(
            expected: """{"unmatched":2}""",
            actual: json);
    }

    private static readonly Uri EvaluationUri = new("https://registry.test/groups/ia-2501");

    private static Name NameFrom(string lastName, string firstName)
    {
        NameFields fields = default;
        fields.LastName[0] = lastName;
        fields.FirstName[0] = firstName;
        return new(fields);
    }
}
