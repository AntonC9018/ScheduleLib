using System.IO.Compression;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using OnlineRegistry.AttendanceExcel;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed partial class AttendanceWorkbookFileTests : IDisposable
{
    private readonly string _filePath = Path.Combine(
        Path.GetTempPath(),
        $"attendance-comments-{Guid.NewGuid():N}.xlsx");

    public void Dispose()
    {
        File.Delete(_filePath);
    }

    [Fact]
    public void OpensWorkbookWithNoteMissingVmlTextBoxAndKeepsCellValues()
    {
        WriteWorkbookWithNote(_filePath);
        RemoveVmlTextBoxes(_filePath);
        var originalBytes = File.ReadAllBytes(_filePath);

        Assert.ThrowsAny<InvalidOperationException>(() => new XLWorkbook(_filePath));
        using var workbook = AttendanceWorkbookFile.OpenWithoutComments(_filePath);

        var cell = workbook.Worksheet("Sheet").Cell("A1");
        Assert.Equal("Student", cell.GetString());
        Assert.False(cell.HasComment);
        Assert.Equal(originalBytes, File.ReadAllBytes(_filePath));
    }

    private static void WriteWorkbookWithNote(string filePath)
    {
        using var workbook = new XLWorkbook();
        var cell = workbook.AddWorksheet("Sheet").Cell("A1");
        cell.Value = "Student";
        cell.CreateComment().AddText("note");
        workbook.SaveAs(filePath);
    }

    /// <summary>Reproduces LibreOffice output, whose note shapes have no text box.</summary>
    private static void RemoveVmlTextBoxes(string filePath)
    {
        using var archive = ZipFile.Open(filePath, ZipArchiveMode.Update);
        var vmlEntries = archive.Entries
            .Where(e => e.FullName.EndsWith(".vml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.NotEmpty(vmlEntries);
        foreach (var entry in vmlEntries)
        {
            string vml;
            using (var reader = new StreamReader(entry.Open()))
            {
                vml = reader.ReadToEnd();
            }
            var name = entry.FullName;
            entry.Delete();
            using var writer = new StreamWriter(archive.CreateEntry(name).Open());
            writer.Write(VmlTextBox().Replace(vml, ""));
        }
    }

    [GeneratedRegex("<v:textbox[^>]*?/>|<v:textbox.*?</v:textbox>", RegexOptions.Singleline)]
    private static partial Regex VmlTextBox();
}
