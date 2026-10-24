using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class AttendanceCompositionTests
{
    [Fact]
    public void ExplicitStudentAliasesMapSpellingVariantsAndLeaveUnknownStudentsUnchanged()
    {
        var names = NamesInDb.Create([NameHelper.Parse("Synthetic Alpha")]);
        var unknown = new List<Name>();
        var helper = StudentNameRemapHelper.Create([new HtmlStudent("Synthetic Alphaa", false), new HtmlStudent("Unlisted Beta", false)],
            names, unknown, new Dictionary<string, string> { ["Synthetic Alphaa"] = "Synthetic Alpha" });
        Assert.Single(unknown);
        Assert.Equal(expected: new[] { Attendance.NotPresent, Attendance.None }, actual: helper.RemapToHtml([Attendance.NotPresent]));
    }

    [Fact]
    public void SharedAudienceCombinesParentSheetsByDateAndPreservesMissingDates()
    {
        var builder = new AllStudentAttendanceListBuilder();
        var course = new CourseId(0);
        var first = new GroupId(0);
        var second = new GroupId(1);
        var partition = new GroupPartitionKey(SubGroup.All, Specialization.DezvoltareaAplicatiilor);
        Add(first, "Synthetic Alpha", 2, Attendance.NotPresent);
        Add(second, "Synthetic Beta", 9, Attendance.Present);
        var list = builder.Build(Attendance.None);
        var groups = new LessonGroups { first, second };
        var names = list.StudentNames(new(groups, partition, course, LessonType.Lab));
        Assert.Equal(expected: 2, actual: names.Count);
        Assert.Equal(expected: 0, actual: names.NameToIndex(NameHelper.Parse("Synthetic Alpha")).Value);
        Assert.Equal(expected: 1, actual: names.NameToIndex(NameHelper.Parse("Synthetic Beta")).Value);
        Assert.Equal(expected: new[] { Attendance.NotPresent, Attendance.None }, actual: Day(2));
        Assert.Equal(expected: new[] { Attendance.None, Attendance.Present }, actual: Day(9));
        Assert.All(Day(16), value => Assert.Equal(expected: Attendance.None, actual: value));
        var inactive = list.Get(new AttendanceLookupKey(new FoundGroups {
            Value = groups,
            IsWildcard = true
        },
            partition, course, LessonType.Lab, 0, new DateTime(year: 2026, month: 9, day: 2)) { ActiveGroups = new LessonGroups { second } });
        Assert.All(inactive.AsArray(), value => Assert.Equal(expected: Attendance.None, actual: value));

        Attendance[] Day(int day)
        {
            return list.Get(new(new FoundGroups {
                Value = groups,
                IsWildcard = true
            },
                partition, course, LessonType.Lab, 0, new DateTime(year: 2026, month: 9, day: day))).AsArray().ToArray();
        }

        void Add(GroupId parent, string name, int day, Attendance attendance)
        {
            var parentList = builder.List(new(new LessonGroups { parent }, partition, course, LessonType.Lab));
            parentList.AddAttendanceDates([new AttendanceDate {
            Year = 2026,
            Month = 9,
            Day = day
            }]);
            parentList.Student(NameHelper.Parse(name)).Day(attendance);
        }
    }
}
