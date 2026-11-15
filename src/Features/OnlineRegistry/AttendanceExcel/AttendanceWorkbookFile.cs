using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;

namespace OnlineRegistry.AttendanceExcel;

/// <summary>
/// Opens attendance workbooks for reading without their cell notes.
/// </summary>
/// <remarks>
/// Attendance parsing never reads notes, but ClosedXML fails to load notes whose VML
/// shape has no <c>v:textbox</c> element, which LibreOffice omits
/// (https://github.com/ClosedXML/ClosedXML/issues/1772).
/// The notes are removed from an in-memory copy; the source file is not modified.
/// </remarks>
public static class AttendanceWorkbookFile
{
    public static XLWorkbook OpenWithoutComments(string filePath)
    {
        using var file = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite);
        var copy = new MemoryStream();
        file.CopyTo(copy);
        RemoveComments(copy);
        copy.Position = 0;
        return new XLWorkbook(copy);
    }

    private static void RemoveComments(Stream package)
    {
        using var document = SpreadsheetDocument.Open(package, isEditable: true);
        var workbookPart = document.WorkbookPart;
        if (workbookPart is null)
        {
            return;
        }
        foreach (var worksheetPart in workbookPart.WorksheetParts)
        {
            if (worksheetPart.WorksheetCommentsPart is { } commentsPart)
            {
                worksheetPart.DeletePart(commentsPart);
            }
        }
    }
}
