using ScheduleLib.Parsing;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class StudentNameParserTests
{
    [Fact]
    public void DiscardsPatronymic()
    {
        var name = StudentNameParser.Parse("Cainar Anna Serghei");

        Assert.Equal(expected: default, actual: name.Patronymic);
        Assert.Equal(expected: "Cainar Anna", actual: name.ToString(NameToStringParams.IncludeEverything));
    }

    [Fact]
    public void MatchesNamesWithDifferentPatronymics()
    {
        var names = NamesInDb.Create([StudentNameParser.Parse("Cainar Anna Seghei")]);

        var index = names.NameToIndex(StudentNameParser.Parse("CAINAR ANNA Serghei"));

        Assert.Equal(expected: 0, actual: index.Value);
    }
}

public sealed class AmbiguousRegistryStudentTests
{
    [Theory]
    [InlineData("Popescu Ion Vasile", "Popescu Ion Petru")]
    [InlineData("Popescu Ion Petru", "Popescu Ion Vasile")]
    public void RejectsRegistryStudentsDifferingOnlyByPatronymic(string first, string second)
    {
        var attendance = NamesInDb.Create([StudentNameParser.Parse("Popescu Ion Vasile")]);
        HtmlStudent[] roster = [new(first, IsExpelled: false), new(second, IsExpelled: false)];

        var error = Assert.Throws<AmbiguousRegistryStudentException>(
            () => StudentNameRemapHelper.Create(roster, attendance, []));

        Assert.Equal(expected: first, actual: error.FirstStudent);
        Assert.Equal(expected: second, actual: error.SecondStudent);
    }
}
