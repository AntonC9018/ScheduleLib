using Desktop.MainWindow;
using ScheduleLib;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;

namespace Desktop.Tests;

public sealed class AddUserTests
{
    [Fact]
    public void FirstAndLastSingleLetterMatch()
    {
        DoScoreFunctionTest(new(
            Input: "C A",
            Person: "Curmanschii Anton",
            FullyMatched: new(NameField.First, NameField.Last)));
    }

    [Fact]
    public void FirstSingleLetterMatch()
    {
        DoScoreFunctionTest(new(
            Input: "C",
            Person: "Curmanschii Anton",
            FullyMatched: new(NameField.Last)));
    }

    [Fact]
    public void FirstAndLastSame_SameSingleLetter_OneWordMatch()
    {
        DoScoreFunctionTest(new(
            Input: "A",
            Person: "Anton Antonov",
            FullyMatched: new(NameField.Last, NameField.First)));
    }

    [Fact]
    public void LastMatches_FirstUnmatches_SingleLetter()
    {
        DoScoreFunctionTest(new(
            Input: "C X",
            Person: "Curmanschii Anton",
            FullyMatched: new(NameField.Last),
            UnmatchedCount: 1));
    }

    [Fact]
    public void FirstMatches_SingleLetter()
    {
        DoScoreFunctionTest(new(
            Input: "A",
            Person: "Curmanschii Anton",
            FullyMatched: new(NameField.First)));
    }

    [Fact]
    public void FirstMatches_LastUnmatches_SingleLetters()
    {
        DoScoreFunctionTest(new(
            Input: "A X",
            Person: "Curmanschii Anton",
            FullyMatched: new(NameField.First),
            UnmatchedCount: 1));
    }

    [Fact]
    public void DoubleNameNotMatches_WhenPersonHasSingleName()
    {
        DoScoreFunctionTest(new(
            Input: "A-C",
            Person: "Curmanschii Anton",
            FullyMatched: new(),
            UnmatchedCount: 1,
            ExpectedScorePositive: false));
    }

    [Fact]
    public void EmptyNoMatch()
    {
        DoScoreFunctionTest(new(
            Input: "",
            Person: "Curmanschii Anton",
            ExpectedScorePositive: false));
    }

    [Fact]
    public void ShortLastNameOfPerson_MatchesLongerQuery()
    {
        DoScoreFunctionTest(new(
            Input: "Curm",
            Person: "C. Anton",
            PartiallyMatched: new(NameField.Last)));
    }

    [Fact]
    public void ShortenedQueryMatches_ShortenedLastNameInPerson_ThatIsDifferent_LastNameLonger()
    {
        DoScoreFunctionTest(new(
            Input: "C.",
            Person: "Curm. Anton",
            FullyMatched: new(NameField.Last)));
    }

    [Fact]
    public void ShortenedQueryMatches_ShortenedLastNameInPerson_ThatIsDifferent_InputLonger()
    {
        DoScoreFunctionTest(new(
            Input: "Curm.",
            Person: "C. Anton",
            PartiallyMatched: new(NameField.Last)));
    }

    [Fact]
    public void ShortenedFirstNameMatchesFirstName()
    {
        DoScoreFunctionTest(new(
            Input: "A.",
            Person: "Curm Anton",
            FullyMatched: new(NameField.First)));
    }

    [Fact]
    public void LastNameMatchCheck()
    {
        DoScoreFunctionTest(new(
            Input: "Titu",
            Person: "Capcelea Titu",
            FullyMatched: new(NameField.First)));
    }

    [Fact]
    public void SearchedLastNameLonger()
    {
        DoScoreFunctionTest(new(
            Input: "Capceleaa",
            Person: "Capcelea Titu",
            PartiallyMatched: new(NameField.Last)));
    }

    public sealed record class TestDataRecord(
        string Input,
        string Person,
        EnumBitArray<NameField> FullyMatched = default,
        EnumBitArray<NameField> PartiallyMatched = default,
        int UnmatchedCount = 0,
        bool ExpectedScorePositive = true);

    private void DoScoreFunctionTest(TestDataRecord data)
    {
        var score = GetScore(data.Input, data.Person);
        Assert.Equal(data.ExpectedScorePositive, score.AsInt() > 0);

        var expectedScore = new MatchScore(
            FullMatch: data.FullyMatched,
            PartialMatch: data.PartiallyMatched,
            UnmatchedCount: data.UnmatchedCount);
        Assert.Equal(expectedScore, score);
    }

    private static MatchScore GetScore(string input, string person)
    {
        var name = NameHelper.Parse(person);

        using var nameParser = new NameParser();
        var output = new List<NameParts<string?>>();
        nameParser.Load(input.AsMemory());
        AddUserViewModel.Parse(nameParser, output);

        var ret = MatchScore.Create(name, output);
        return ret;
    }
}
