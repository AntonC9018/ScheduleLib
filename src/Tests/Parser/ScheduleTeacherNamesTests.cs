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
        Assert.Equal(expected, t.Model.Name.FirstName);
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
        Assert.Equal(nameBefore, t.Model.Name.FirstName);
    }

    [Fact]
    public void ShortFirstNameResetUsingShortNameMethod()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("First Last");
        var name = CreateSinglePartNameWord("Fi.");
        // Doesn't throw
        t.ShortFirstName(name);

        Assert.Equal("Fi.", t.Model.Name.FirstName[0].Short);
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
        Assert.Equal("First", t.Model.Name.FirstName[0].Full);

        t.FirstName(CreateSinglePartName(new()
        {
            Full = "Fist",
            Short = null,
        }));
        Assert.Equal("Fist", t.Model.Name.FirstName[0].Full);
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
        Assert.Equal("First", t.Model.Name.FirstName[0].Full);

        Assert.Throws<ArgumentException>(() =>
            t.FirstName(CreateSinglePartName(new()
            {
                Full = "Irst",
                Short = null,
            })));
        Assert.Equal("First", t.Model.Name.FirstName[0].Full);
    }

    [Fact]
    public void LookupWorksAfterAddingTeacher()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();
        var lookup = s.Lookup(null!);

        var t = s.Teacher("First Last");
        Assert.Equal(t.Id, lookup.Teacher(lastName: "Last"));
    }

    [Fact]
    public void LookupUpdatesAfterChangingLastName()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();
        var t = s.Teacher("First Last");
        t.LastName("Otherlast");
        var lookup = s.Lookup(null!);

        Assert.Null(lookup.Teacher(lastName: "Last"));
        Assert.Equal(t.Id, lookup.Teacher(lastName: "Otherlast"));
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
        _db.Check(CreateSinglePartNameWord("I."), 1);
        _db.Check(CreateSinglePartNameWord("F."), 0);
        _db.Check(CreateSinglePartNameWord("Fi."), 0);
        _db.Check(CreateSinglePartNameWord("Rs"), -1);
        _db.Check(CreateSinglePartNameWord("Rst"), 2);
        _db.Check(CreateSinglePartNameWord("R."), 2);
        _db.Check(CreateSinglePartNameWord("Unrelated"), -1);
    }

    [Fact]
    public void SearchByFullFirstName_WithShortFirstNameInDb()
    {
        _db.Check(CreateSinglePartNameWord("Other"), 3);
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

        _ids = _teachers.WhereSelectIndex(x => x.Name.LastName[0] == "Last").ToArray();
    }

    public void Check(NameParts<Word> firstName, int expected)
    {
        int i = TeacherLookupHelper.FindIndexOfBestMatch(
            _teachers,
            _ids,
            firstName);
        Assert.Equal(expected, i);
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

