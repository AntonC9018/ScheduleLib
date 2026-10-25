using ScheduleLib.Builders;

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
        s.EnableLookupModule();
        var lookup = s.Lookup(null!);

        var t = s.Teacher("First Last");
        global::ScheduleLib.TeacherId? expected2 = t.Id;
        global::ScheduleLib.TeacherId? actual2 = lookup.Teacher(lastName: "Last");
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void LookupUpdatesAfterChangingLastName()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();
        var t = s.Teacher("First Last");
        t.LastName("Otherlast");
        var lookup = s.Lookup(null!);

        global::ScheduleLib.TeacherId? value = lookup.Teacher(lastName: "Last");
        Assert.Null(value);
        global::ScheduleLib.TeacherId? expected2 = t.Id;
        global::ScheduleLib.TeacherId? actual2 = lookup.Teacher(lastName: "Otherlast");
        Assert.Equal(expected: expected2, actual: actual2);
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
        global::ScheduleLib.ParserTests.Db db = _db;
        global::ScheduleLib.NameParts<global::Word> firstName = CreateSinglePartNameWord("I.");
        int expected = 1;
        db.Check(firstName, expected);
        global::ScheduleLib.ParserTests.Db db2 = _db;
        global::ScheduleLib.NameParts<global::Word> firstName2 = CreateSinglePartNameWord("F.");
        int expected2 = 0;
        db2.Check(firstName2, expected2);
        global::ScheduleLib.ParserTests.Db db3 = _db;
        global::ScheduleLib.NameParts<global::Word> firstName3 = CreateSinglePartNameWord("Fi.");
        int expected3 = 0;
        db3.Check(firstName3, expected3);
        global::ScheduleLib.ParserTests.Db db4 = _db;
        global::ScheduleLib.NameParts<global::Word> firstName4 = CreateSinglePartNameWord("Rs");
        int expected4 = -1;
        db4.Check(firstName4, expected4);
        global::ScheduleLib.ParserTests.Db db5 = _db;
        global::ScheduleLib.NameParts<global::Word> firstName5 = CreateSinglePartNameWord("Rst");
        int expected5 = 2;
        db5.Check(firstName5, expected5);
        global::ScheduleLib.ParserTests.Db db6 = _db;
        global::ScheduleLib.NameParts<global::Word> firstName6 = CreateSinglePartNameWord("R.");
        int expected6 = 2;
        db6.Check(firstName6, expected6);
        global::ScheduleLib.ParserTests.Db db7 = _db;
        global::ScheduleLib.NameParts<global::Word> firstName7 = CreateSinglePartNameWord("Unrelated");
        int expected7 = -1;
        db7.Check(firstName7, expected7);
    }

    [Fact]
    public void SearchByFullFirstName_WithShortFirstNameInDb()
    {
        global::ScheduleLib.ParserTests.Db db = _db;
        global::ScheduleLib.NameParts<global::Word> firstName = CreateSinglePartNameWord("Other");
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
        _teachers = [
            Create("Last", CreateSinglePartName(new()
            {
                Full = "First",
                Short = "F.",
            })),
            Create("Last", CreateSinglePartName(new()
            {
                Full = "Irst",
                Short = "I.",
            })),
            Create("Last", CreateSinglePartName(new()
            {
                Full = "Rst",
                Short = null,
            })),
            Create("Last", CreateSinglePartName(new()
            {
                Full = null,
                Short = "O.",
            })),
            Create("Unrelated", CreateSinglePartName(new()
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
        int i = TeacherLookupHelper.FindIndexOfBestMatch(
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

