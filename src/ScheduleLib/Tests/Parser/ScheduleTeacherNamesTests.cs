using System.Text;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.ParserTests;

using static Helper;

public sealed class ScheduleTeacherNamesTests
{
    [Fact]
    public void ShortFirstNameSameCharacterWorks()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("First Last");
        var newName = CreateSinglePartName(new()
        {
            Full = null,
            Short = "F.",
        });

        // Doesn't throw
        t.FirstName(newName);

        var expected = CreateSinglePartName(new()
        {
            Full = "First",
            Short = "F.",
        });
        Assert.Equal(expected: expected, actual: t.Model.Name.FirstName);
    }

    [Fact]
    public void ShortFirstNameDifferentCharacterFails()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("First Last");
        var nameBefore = t.Model.Name.FirstName;
        var newName = CreateSinglePartName(new()
        {
            Full = null,
            Short = "L.",
        });
        Assert.Throws<ArgumentException>(() => t.FirstName(newName));
        Assert.Equal(expected: nameBefore, actual: t.Model.Name.FirstName);
    }

    [Fact]
    public void ShortFirstNameResetUsingShortNameMethod()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("First Last");
        var name = CreateSinglePartNameWord("Fi.");
        // Doesn't throw
        t.ShortFirstName(name);

        Assert.Equal(expected: "Fi.", actual: t.Model.Name.FirstName[0].Short);
    }

    [Fact]
    public void InvalidShortFirstNameFailsValidation()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("First Last");
        var name = CreateSinglePartNameWord("La.");
        Assert.Throws<ArgumentException>(() => t.ShortFirstName(name));
        Assert.Null(t.Model.Name.FirstName[0].Short);
    }

    [Fact]
    public void FirstNameResetWorksWhenInitialsMatch()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("F. Last");

        t.FirstName(CreateSinglePartName(new()
        {
            Full = "First",
            Short = null,
        }));
        Assert.Equal(expected: "First", actual: t.Model.Name.FirstName[0].Full);

        t.FirstName(CreateSinglePartName(new()
        {
            Full = "Fist",
            Short = null,
        }));
        Assert.Equal(expected: "Fist", actual: t.Model.Name.FirstName[0].Full);
    }

    [Fact]
    public void FirstNameDoesntWorkWhenInitialsDontMatch()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("F. Last");

        t.FirstName(CreateSinglePartName(new()
        {
            Full = "First",
            Short = null,
        }));
        Assert.Equal(expected: "First", actual: t.Model.Name.FirstName[0].Full);

        Assert.Throws<ArgumentException>(() =>
            t.FirstName(CreateSinglePartName(new()
            {
                Full = "Irst",
                Short = null,
            })));
        Assert.Equal(expected: "First", actual: t.Model.Name.FirstName[0].Full);
    }

    [Fact]
    public void LookupWorksAfterAddingTeacher()
    {
        var s = new ScheduleBuilder();
        var lookup = s.EnableLookupModule();

        var t = s.Teacher("First Last");
        int[] expected = [t.Id.Id];
        Assert.Equal(expected: expected, actual: lookup.TeachersByLastName.Get("Last"));
    }

    [Fact]
    public void LookupUpdatesAfterChangingLastName()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();
        var t = s.Teacher("First Last");
        t.LastName("Otherlast");
        var lookup = s.LookupModule!;

        Assert.Empty(lookup.TeachersByLastName.Get("Last") ?? []);
        int[] expected = [t.Id.Id];
        Assert.Equal(expected: expected, actual: lookup.TeachersByLastName.Get("Otherlast"));
    }

    [Fact]
    public void BuiltLookupDoesNotMatchAFullFirstNameAgainstADisplayInitial()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();
        var other = s.Teacher("Mihai-Ana Popescu");
        var schedule = s.Build();
        var lookup = BuiltLookup(s, schedule);

        Assert.NotEqual(expected: other.Id, actual: lookup.Teacher("Maria-A.", "Popescu"));
    }

    [Fact]
    public void BuiltLookupPrefersTheTeacherWhoseFullFirstNameMatches()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();
        var other = s.Teacher("Mihai-Ana Popescu");
        var expected = s.Teacher("Maria-A. Popescu");
        Assert.NotEqual(expected: other.Id, actual: expected.Id);
        var schedule = s.Build();
        var lookup = BuiltLookup(s, schedule);

        Assert.Equal(expected: expected.Id, actual: lookup.Teacher("Maria-A.", "Popescu"));
    }

    [Fact]
    public void BuiltTeacherDisplaysInitialsOfFullFirstNames()
    {
        var s = new ScheduleBuilder();
        var teacher = s.Teacher("Mihai-Ana Popescu");
        var schedule = s.Build();
        var name = schedule.Get(teacher.Id).PersonName;

        var display = new StringBuilder();
        NameDisplayHelper.Append(new()
        {
            Output = display,
            Name = name,
            PreferLonger = false,
        });
        Assert.Equal(expected: "M-A. Popescu", actual: display.ToString());

        var fileName = new StringBuilder();
        TeacherNameHelper.AsFileName(fileName, name);
        Assert.Equal(expected: "M-A_Popescu", actual: fileName.ToString());
    }

    private static LookupFacade BuiltLookup(ScheduleBuilder s, Schedule schedule)
    {
        var courseNames = CourseNameUnifierConfig.Create(new CourseNameParserConfig(new()), []);
        var groupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = new(2025),
        });
        var scheduleLookup = ScheduleLookup.Create(schedule, courseNames);
        return new LookupFacade(
            schedule: schedule,
            lookup: scheduleLookup,
            remappings: s.Remappings,
            groupParseContext: groupParseContext);
    }
}

public sealed class TeacherFindIndexOfBestMatchTests : IClassFixture<Db>
{
    private readonly Db _db;

    public TeacherFindIndexOfBestMatchTests(Db db)
    {
        _db = db;
    }

    [Fact]
    public void FindBestMatch()
    {
        var db = _db;
        var firstName = CreateSinglePartNameWord("I.");
        int expected = 1;
        db.Check(firstName, expected);
        var db2 = _db;
        var firstName2 = CreateSinglePartNameWord("F.");
        int expected2 = 0;
        db2.Check(firstName2, expected2);
        var db3 = _db;
        var firstName3 = CreateSinglePartNameWord("Fi.");
        int expected3 = 0;
        db3.Check(firstName3, expected3);
        var db4 = _db;
        var firstName4 = CreateSinglePartNameWord("Rs");
        int expected4 = -1;
        db4.Check(firstName4, expected4);
        var db5 = _db;
        var firstName5 = CreateSinglePartNameWord("Rst");
        int expected5 = 2;
        db5.Check(firstName5, expected5);
        var db6 = _db;
        var firstName6 = CreateSinglePartNameWord("R.");
        int expected6 = 2;
        db6.Check(firstName6, expected6);
        var db7 = _db;
        var firstName7 = CreateSinglePartNameWord("Unrelated");
        int expected7 = -1;
        db7.Check(firstName7, expected7);
    }

    [Fact]
    public void SearchByFullFirstName_WithShortFirstNameInDb()
    {
        var db = _db;
        var firstName = CreateSinglePartNameWord("Other");
        int expected = 3;
        db.Check(firstName, expected);
    }
}

file static class Helper
{
    public static NameParts<OptionalNamePart> CreateSinglePartName(OptionalNamePart p)
    {
        var ret = default(NameParts<OptionalNamePart>);
        ret[0] = p;
        return ret;
    }

    public static NameParts<Word> CreateSinglePartNameWord(string name)
    {
        var ret = default(NameParts<Word>);
        ret[0] = new(name);
        ret[1] = Word.Empty;
        return ret;
    }
}

public sealed class Db
{
    private readonly int[] _ids;
    private readonly TeacherBuilderModel[] _teachers;

    public Db()
    {
        _teachers =
        [
            Create(
                "Last",
                CreateSinglePartName(new()
                {
                    Full = "First",
                    Short = "F.",
                })),
            Create(
                "Last",
                CreateSinglePartName(new()
                {
                    Full = "Irst",
                    Short = "I.",
                })),
            Create(
                "Last",
                CreateSinglePartName(new()
                {
                    Full = "Rst",
                    Short = null,
                })),
            Create(
                "Last",
                CreateSinglePartName(new()
                {
                    Full = null,
                    Short = "O.",
                })),
            Create(
                "Unrelated",
                CreateSinglePartName(new()
                {
                    Full = "First",
                    Short = "F.",
                })),
        ];

        _ids = _teachers.WhereSelectIndex(x => x.Name.LastName[0] == "Last")
            .ToArray();
    }

    public void Check(NameParts<Word> firstName, int expected)
    {
        int i = TeacherLookupHelper.FindIndexOfBestBuilderMatch(
            _teachers,
            _ids,
            firstName);
        Assert.Equal(expected: expected, actual: i);
    }

    private static TeacherBuilderModel Create(string lastName, NameParts<OptionalNamePart> name)
    {
        var l = new LastName();
        l[0] = lastName;

        return new()
        {
            Name = new()
            {
                LastName = l,
                FirstName = name,
            },
        };
    }
}
