using ScheduleLib;
using ScheduleLib.Builders;

namespace App.Tests;

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

        Assert.Equal("Fi.", t.Model.Name.FirstName.A.Short);
    }

    [Fact]
    public void InvalidShortFirstNameFailsValidation()
    {
        var s = new ScheduleBuilder();
        var t = s.Teacher("First Last");
        var name = CreateSinglePartNameWord("La.");
        Assert.Throws<ArgumentException>(() => t.ShortFirstName(name));
        Assert.Null(t.Model.Name.FirstName.A.Short);
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
        Assert.Equal("First", t.Model.Name.FirstName.A.Full);

        t.FirstName(CreateSinglePartName(new()
        {
            Full = "Fist",
            Short = null,
        }));
        Assert.Equal("Fist", t.Model.Name.FirstName.A.Full);
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
        Assert.Equal("First", t.Model.Name.FirstName.A.Full);

        Assert.Throws<ArgumentException>(() =>
            t.FirstName(CreateSinglePartName(new()
            {
                Full = "Irst",
                Short = null,
            })));
        Assert.Equal("First", t.Model.Name.FirstName.A.Full);
    }

    [Fact]
    public void LookupWorksAfterAddingTeacher()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();

        var t = s.Teacher("First Last");
        Assert.Equal(t.Id, s.Lookup().Teacher(lastName: "Last"));
    }

    [Fact]
    public void LookupUpdatesAfterChangingLastName()
    {
        var s = new ScheduleBuilder();
        s.EnableLookupModule();
        var t = s.Teacher("First Last");
        t.LastName("Otherlast");

        Assert.Null(s.Lookup().Teacher(lastName: "Last"));
        Assert.Equal(t.Id, s.Lookup().Teacher(lastName: "Otherlast"));
    }

    private static FirstNameParts<OptionalFirstNamePart> CreateSinglePartName(OptionalFirstNamePart p)
    {
        var ret = default(FirstNameParts<OptionalFirstNamePart>);
        ret.A = p;
        return ret;
    }

    private static FirstNameParts<Word> CreateSinglePartNameWord(string name)
    {
        var ret = default(FirstNameParts<Word>);
        ret.A = new(name);
        return ret;
    }
}
