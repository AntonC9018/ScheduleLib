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
        global::ScheduleLib.OnlineRegistry.Attendance[] expected2 = new[] { Attendance.NotPresent, Attendance.None };
        global::ScheduleLib.OnlineRegistry.Attendance[] actual2 = helper.RemapToHtml([Attendance.NotPresent]);
        Assert.Equal(expected: expected2, actual: actual2);
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
        global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey()
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey key = new(groups, partition, course, LessonType.Lab);
            return key;
        }

        var names = list.StudentNames(CalculateKey());
        Assert.Equal(expected: 2, actual: names.Count);
        global::ScheduleLib.Parsing.Name CalculateName()
        {
            global::ScheduleLib.Parsing.Name name2 = NameHelper.Parse("Synthetic Alpha");
            return name2;
        }

        Assert.Equal(expected: 0, actual: names.NameToIndex(CalculateName()).Value);
        global::ScheduleLib.Parsing.Name CalculateName2()
        {
            global::ScheduleLib.Parsing.Name name3 = NameHelper.Parse("Synthetic Beta");
            return name3;
        }

        Assert.Equal(expected: 1, actual: names.NameToIndex(CalculateName2()).Value);
        global::ScheduleLib.OnlineRegistry.Attendance[] expected2 = new[] { Attendance.NotPresent, Attendance.None };
        global::ScheduleLib.OnlineRegistry.Attendance[] actual2 = Day(2);
        Assert.Equal(expected: expected2, actual: actual2);
        global::ScheduleLib.OnlineRegistry.Attendance[] expected3 = new[] { Attendance.None, Attendance.Present };
        global::ScheduleLib.OnlineRegistry.Attendance[] actual3 = Day(9);
        Assert.Equal(expected: expected3, actual: actual3);
        global::ScheduleLib.OnlineRegistry.Attendance[] collection = Day(16);
        global::System.Collections.Generic.IEnumerable<global::ScheduleLib.OnlineRegistry.Attendance> collection2 = collection;
        global::System.Action<global::ScheduleLib.OnlineRegistry.Attendance> action = value => Assert.Equal(expected: Attendance.None, actual: value);
        Assert.All(collection2, action);
        global::System.DateTime CalculateDateTime()
        {
            global::System.DateTime dateTime = new DateTime(year: 2026, month: 9, day: 2);
            return dateTime;
        }

        var inactive = list.Get(new AttendanceLookupKey(new FoundGroups
        {
            Value = groups,
            IsWildcard = true
        },
                    partition, course, LessonType.Lab, 0, CalculateDateTime())
        { ActiveGroups = new LessonGroups { second } });
        Assert.All(inactive.AsArray(), value => Assert.Equal(expected: Attendance.None, actual: value));

        Attendance[] Day(int day)
        {
            global::System.DateTime CalculateDateTime()
            {
                global::System.DateTime dateTime = new DateTime(year: 2026, month: 9, day: day);
                return dateTime;
            }

            return list.Get(new(new FoundGroups
            {
                Value = groups,
                IsWildcard = true
            },
                            partition, course, LessonType.Lab, 0, CalculateDateTime()))
                .AsArray()
                .ToArray();
        }

        void Add(GroupId parent, string name, int day, Attendance attendance)
        {
            global::ScheduleLib.OnlineRegistry.StudentsLookupKey CalculateKey()
            {
                global::ScheduleLib.OnlineRegistry.StudentsLookupKey key = new(new LessonGroups { parent }, partition, course, LessonType.Lab);
                return key;
            }

            var parentList = builder.List(CalculateKey());
            parentList.AddAttendanceDates([new AttendanceDate {
            Year = 2026,
            Month = 9,
            Day = day
            }]);
            global::ScheduleLib.Parsing.Name CalculateName()
            {
                global::ScheduleLib.Parsing.Name name2 = NameHelper.Parse(name);
                return name2;
            }

            parentList.Student(CalculateName())
                .Day(attendance);
        }
    }
}
