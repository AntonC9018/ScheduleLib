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
        Assert.True(missing.Length == 0 && extra.Length == 0,
            "Missing:\n" + string.Join("\n---\n", missing) + "\nUnexpected:\n" + string.Join("\n---\n", extra));
        Assert.Equal(expected.Length, actual.Count);
    }

    [Fact]
    public void PartialBorderSeparatesSharedLectureFromTwoLabs()
    {
        var actual = PdfTableReader.RecoverPartialBorders([new(0, 0, 200, 100)], [new(true, 100, 50, 100)]);
        Assert.Equal(new PdfBox[] { new(0, 0, 200, 50), new(0, 50, 100, 100), new(100, 50, 200, 100) }, actual);
    }

    [Theory]
    [InlineData("Progr.C++ (lab,I-imp,II-par))\nA.Curmanscii 143/4", "Progr.C++ (lab, I-imp, II-par)\nA.Curmanscii 143/4")]
    [InlineData("Design audio și efecte\nvizuale (curs)", "Design audio și efecte vizuale (curs)")]
    [InlineData("Intel.artif.(lab,)", "Intel.artif.(lab)")]
    public void NormalizesPdfWrappingAndPunctuation(string text, string expected) => Assert.Equal(expected, PdfTextNormalizer.Normalize(text));

    [Fact]
    public void ParityRepairIsScopedAndIdempotent()
    {
        var cell = new PdfScheduleCell("test", 1, ["I2502(ru)"], "Vineri", "11:30", "Baze de date(lab)\nCr.Ulmanu 218/4a");
        var repaired = PdfTextNormalizer.RepairMissingParity(cell);
        Assert.Equal("Baze de date(lab, imp)\nCr.Ulmanu 218/4a", repaired.Text);
        Assert.Equal(repaired, PdfTextNormalizer.RepairMissingParity(repaired));
        var monday = cell with { Day = "Luni" };
        Assert.Equal(monday, PdfTextNormalizer.RepairMissingParity(monday));
    }
}
