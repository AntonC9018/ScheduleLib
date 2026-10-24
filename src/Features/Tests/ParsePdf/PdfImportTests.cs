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
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", source));
        var actual = PdfScheduleImporter.Read(stream, source);
        var expected = JsonSerializer.Deserialize<PdfScheduleCell[]>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "expected-cells.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!.Where(c => c.Source == source).ToArray();
        // Compare semantics, independent of the order in which a PDF library discovers cells.
        static string Key(PdfScheduleCell c) => $"{c.Page}|{string.Join(',', c.Groups)}|{c.Day}|{c.Start}|{c.Alternative}|{c.Text}";
        var missing = expected.Select(Key).Except(actual.Select(Key)).ToArray();
        var extra = actual.Select(Key).Except(expected.Select(Key)).ToArray();
        bool conditionResult;
        if (missing.Length == 0)
        {
            conditionResult = extra.Length == 0;
        }
        else
        {
            conditionResult = false;
        }

        Assert.True(conditionResult,
            (string) $"Missing:\n{string.Join("\n---\n", missing)}\nUnexpected:\n{string.Join("\n---\n", extra)}");
        Assert.Equal(expected: expected.Length, actual: actual.Count);
    }

    [Fact]
    public void PartialBorderSeparatesSharedLectureFromTwoLabs()
    {
        var actual = PdfTableReader.RecoverPartialBorders([new(Left: 0, Top: 0, Right: 200, Bottom: 100)], [new(true, Position: 100, Start: 50, End: 100)]);
        Assert.Equal(expected: new PdfBox[] { new(Left: 0, Top: 0, Right: 200, Bottom: 50), new(Left: 0, Top: 50, Right: 100, Bottom: 100), new(Left: 100, Top: 50, Right: 200, Bottom: 100) }, actual: actual);
    }

    [Theory]
    [InlineData("Progr.C++ (lab,I-imp,II-par))\nA.Curmanscii 143/4", "Progr.C++ (lab, I-imp, II-par)\nA.Curmanscii 143/4")]
    [InlineData("Design audio și efecte\nvizuale (curs)", "Design audio și efecte vizuale (curs)")]
    [InlineData("Intel.artif.(lab,)", "Intel.artif.(lab)")]
    public void NormalizesPdfWrappingAndPunctuation(string text, string expected) => Assert.Equal(expected: expected, actual: PdfTextNormalizer.Normalize(text));

    [Fact]
    public void ParityRepairIsScopedAndIdempotent()
    {
        var cell = new PdfScheduleCell(Source: "test", 1, ["I2502(ru)"], Day: "Vineri", Start: "11:30", Text: "Baze de date(lab)\nCr.Ulmanu 218/4a");
        var repaired = PdfTextNormalizer.RepairMissingParity(cell);
        Assert.Equal(expected: "Baze de date(lab, imp)\nCr.Ulmanu 218/4a", actual: repaired.Text);
        Assert.Equal(expected: repaired, actual: PdfTextNormalizer.RepairMissingParity(repaired));
        var monday = cell with { Day = "Luni" };
        Assert.Equal(expected: monday, actual: PdfTextNormalizer.RepairMissingParity(monday));
    }
}
