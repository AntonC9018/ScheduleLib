using System.Text.Json;
using ScheduleLib.Import.Pdf;

public sealed class PdfImportTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void SourcePdfsMatchReviewedCellContentAndMembership(int year)
    {
        var source = $"orar{year}.pdf";
        using var stream = File.OpenRead(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            source));
        var actual = PdfScheduleImporter.Read(stream, source, PdfParityRepairs.None);

        string CalculateJson()
        {
            string path = Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "expected-cells.json");
            string json = File.ReadAllText(path);
            return json;
        }

        var expected = JsonSerializer.Deserialize<PdfScheduleCell[]>(
                CalculateJson(),
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                })!
            .Where(c => c.Source == source)
            .ToArray();

        // Compare semantics, independent of the order in which a PDF library discovers cells.
        static string Key(PdfScheduleCell c) =>
            $"{c.Page}|{string.Join(',', c.Groups)}|{c.Day}|{c.Start}|{c.Alternative}|{c.Text}";

        var missing = expected.Select(Key)
            .Except(actual.Select(Key))
            .ToArray();
        var extra = actual.Select(Key)
            .Except(expected.Select(Key))
            .ToArray();

        bool CalculateConditionResult()
        {
            if (missing.Length != 0)
            {
                return false;
            }

            if (extra.Length == 0)
            {
                return true;
            }

            return false;
        }

        bool conditionResult = CalculateConditionResult();


        Assert.True(
            conditionResult,
            $"Missing:\n{string.Join("\n---\n", missing)}\nUnexpected:\n{string.Join("\n---\n", extra)}");
        Assert.Equal(expected: expected.Length, actual: actual.Count);
    }

    [Fact]
    public void PartialBorderSeparatesSharedLectureFromTwoLabs()
    {
        var actual = PdfTableReader.RecoverPartialBorders(
            [
                new(
                    Left: 0,
                    Top: 0,
                    Right: 200,
                    Bottom: 100),
            ],
            [
                new(
                    true,
                    Position: 100,
                    Start: 50,
                    End: 100),
            ]);
        Assert.Equal(
            expected: new PdfBox[]
            {
                new(
                    Left: 0,
                    Top: 0,
                    Right: 200,
                    Bottom: 50),
                new(
                    Left: 0,
                    Top: 50,
                    Right: 100,
                    Bottom: 100),
                new(
                    Left: 100,
                    Top: 50,
                    Right: 200,
                    Bottom: 100),
            },
            actual: actual);
    }

    [Theory]
    [InlineData("Progr.C++ (lab,I-imp,II-par))\nA.Curmanscii 143/4",
        "Progr.C++ (lab, I-imp, II-par)\nA.Curmanscii 143/4")]
    [InlineData("Design audio și efecte\nvizuale (curs)", "Design audio și efecte vizuale (curs)")]
    [InlineData("Intel.artif.(lab,)", "Intel.artif.(lab)")]
    public void NormalizesPdfWrappingAndPunctuation(string text, string expected)
    {
        string expected2 = expected;
        string actual2 = PdfTextNormalizer.Normalize(text);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void ParityRepairIsScopedAndIdempotent()
    {
        var repairs = new PdfParityRepairs(
            Reference: "clash-fixes.md",
            Rules:
            [
                new(
                    Group: "I2502(ru)",
                    Day: "Vineri",
                    Start: "11:30",
                    Course: "Baze de date",
                    Teacher: "Cr.Ulmanu",
                    Parity: "imp"),
            ]);
        var cell = new PdfScheduleCell(
            Source: "test",
            1,
            ["I2502(ru)"],
            Day: "Vineri",
            Start: "11:30",
            Text: "Baze de date(lab)\nCr.Ulmanu 218/4a");

        var repaired = PdfTextNormalizer.RepairMissingParity(cell, repairs);
        Assert.Equal(expected: "Baze de date(lab, imp)\nCr.Ulmanu 218/4a", actual: repaired.Text);
        Assert.Equal(expected: "Missing imp marker; documented in clash-fixes.md", actual: repaired.Repair);
        Assert.Equal(expected: repaired, actual: PdfTextNormalizer.RepairMissingParity(repaired, repairs));

        var monday = cell with
        {
            Day = "Luni",
        };
        Assert.Equal(expected: monday, actual: PdfTextNormalizer.RepairMissingParity(monday, repairs));
        Assert.Equal(expected: cell, actual: PdfTextNormalizer.RepairMissingParity(cell, PdfParityRepairs.None));
    }

    [Theory]
    [InlineData("Baze de date(lab)", "Baze de date(lab, imp)")]
    [InlineData("Baze de date(ro)(lab)", "Baze de date(ro)(lab, imp)")]
    [InlineData("Baze de date", "Baze de date (imp)")]
    public void ParityRepairMarksOnlyTheLastParenthesizedNote(string firstLine, string expected)
    {
        var repairs = new PdfParityRepairs(
            Reference: "clash-fixes.md",
            Rules:
            [
                new(
                    Group: "I2502(ru)",
                    Day: "Vineri",
                    Start: "11:30",
                    Course: "Baze de date",
                    Teacher: "Cr.Ulmanu",
                    Parity: "imp"),
            ]);
        var cell = new PdfScheduleCell(
            Source: "test",
            1,
            ["I2502(ru)"],
            Day: "Vineri",
            Start: "11:30",
            Text: $"{firstLine}\nCr.Ulmanu 218/4a");

        var repaired = PdfTextNormalizer.RepairMissingParity(cell, repairs);
        Assert.Equal(expected: $"{expected}\nCr.Ulmanu 218/4a", actual: repaired.Text);
    }

    [Theory]
    [InlineData("2025-09-01", 0)]
    [InlineData("2026-09-01", 3)]
    [InlineData("2027-02-02", 0)]
    public void ParityRepairsApplyOnlyToTheirVerifiedPeriod(string periodStart, int expectedRules)
    {
        var repairs = ScheduleLib.ScheduleDefaults.Config.PdfParityRepairsForPeriod(DateOnly.Parse(periodStart));
        Assert.Equal(expected: expectedRules, actual: repairs.Rules.Count);
    }
}
