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
        string input = "C A";
        string person = "Curmanschii Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.First, NameField.Last);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void FirstSingleLetterMatch()
    {
        string input = "C";
        string person = "Curmanschii Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.Last);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void FirstAndLastSame_SameSingleLetter_OneWordMatch()
    {
        string input = "A";
        string person = "Anton Antonov";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.Last, NameField.First);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void LastMatches_FirstUnmatches_SingleLetter()
    {
        string input = "C X";
        string person = "Curmanschii Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.Last);
        int unmatchedCount = 1;
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched, UnmatchedCount: unmatchedCount);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void FirstMatches_SingleLetter()
    {
        string input = "A";
        string person = "Curmanschii Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.First);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void FirstMatches_LastUnmatches_SingleLetters()
    {
        string input = "A X";
        string person = "Curmanschii Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.First);
        int unmatchedCount = 1;
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched, UnmatchedCount: unmatchedCount);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void DoubleNameNotMatches_WhenPersonHasSingleName()
    {
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: "A-C",
            Person: "Curmanschii Anton",
            FullyMatched: new(),
            UnmatchedCount: 1,
            ExpectedScorePositive: false);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void EmptyNoMatch()
    {
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: "",
            Person: "Curmanschii Anton",
            ExpectedScorePositive: false);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void ShortLastNameOfPerson_MatchesLongerQuery()
    {
        string input = "Curm";
        string person = "C. Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> partiallyMatched = new(NameField.Last);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, PartiallyMatched: partiallyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void ShortenedQueryMatches_ShortenedLastNameInPerson_ThatIsDifferent_LastNameLonger()
    {
        string input = "C.";
        string person = "Curm. Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.Last);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void ShortenedQueryMatches_ShortenedLastNameInPerson_ThatIsDifferent_InputLonger()
    {
        string input = "Curm.";
        string person = "C. Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> partiallyMatched = new(NameField.Last);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, PartiallyMatched: partiallyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void ShortenedFirstNameMatchesFirstName()
    {
        string input = "A.";
        string person = "Curm Anton";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.First);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void LastNameMatchCheck()
    {
        string input = "Titu";
        string person = "Capcelea Titu";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> fullyMatched = new(NameField.First);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, FullyMatched: fullyMatched);
        DoScoreFunctionTest(data);
    }

    [Fact]
    public void SearchedLastNameLonger()
    {
        string input = "Capceleaa";
        string person = "Capcelea Titu";
        global::ScheduleLib.Helper.EnumBitArray<global::ScheduleLib.Parsing.NameField> partiallyMatched = new(NameField.Last);
        global::Desktop.Tests.AddUserTests.TestDataRecord data = new(
            Input: input, Person: person, PartiallyMatched: partiallyMatched);
        DoScoreFunctionTest(data);
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
        var score = GetScore(input: data.Input, person: data.Person);
        Assert.Equal(expected: data.ExpectedScorePositive, actual: score.AsInt() > 0);

        var expectedScore = new MatchScore(
            FullMatch: data.FullyMatched,
            PartialMatch: data.PartiallyMatched,
            UnmatchedCount: data.UnmatchedCount);
        Assert.Equal(expected: expectedScore, actual: score);
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
