using System.Diagnostics;
using App;
using App.Generation;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Table = DocumentFormat.OpenXml.Wordprocessing.Table;

var schedule = new ScheduleBuilder
{
    ValidationSettings = new()
    {
        SubGroup = SubGroupValidationMode.PossiblyIncreaseSubGroupCount,
    },
};

schedule.SetStudyYear(2024);
// TODO: Read the whole table once to find these first.
var timeConfig = LessonTimeConfig.CreateDefault();
var dayNameProvider = new DayNameProvider();
var dayNameToDay = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase);

const string fileName = "Orar_An_II Lic.docx";
var fullPath = Path.GetFullPath(fileName);
using var doc = WordprocessingDocument.Open(fullPath, isEditable: false);
if (doc.MainDocumentPart?.Document.Body is not { } bodyElement)
{
    return;
}

// Multirow cell has <w:vMerge w:val="restart"/> on the first cell, and an empty <w:vMerge /> on the next cells
// Multicolumn cell has <w:gridSpan w:val="2" /> where 2 indicates the column size
// May be combined
var tables = bodyElement.ChildElements
    .OfType<Table>()
    .ToArray();
int colOffset = 0;
foreach (var table in tables)
{
    var rows = table.ChildElements.OfType<TableRow>();
    int rowIndex = 0;
    using var rowEnumerator = rows.GetEnumerator();
    if (!rowEnumerator.MoveNext())
    {
        break;
    }

    IEnumerable<TableCell> Cells() => rowEnumerator.Current.ChildElements.OfType<TableCell>();

    int skippedCols = HeaderRow(colOffset);
    if (skippedCols != 2)
    {
        throw new NotSupportedException("Docs with only header 2 cols supported");
    }

    DayOfWeek currentDay = default;
    int currentTimeSlotIndex = 0;

    const int dayColumnIndex = 0;
    const int timeSlotColumnIndex = 1;

    rowIndex++;
    while (rowEnumerator.MoveNext())
    {
        int columnIndex = 0;
        foreach (var cell in Cells())
        {
            var props = cell.TableCellProperties;

            switch (columnIndex)
            {
                case dayColumnIndex:
                {
                    currentDay = DayOfWeekCol();
                    break;
                }
                case timeSlotColumnIndex:
                {
                    break;
                }
                default:
                {
                }
            }

            DayOfWeek DayOfWeekCol()
            {
                if (props?.VerticalMerge is not { } mergeStart)
                {
                    throw new NotSupportedException("Invalid format");
                }

                if (mergeStart.Val is not { } mergeStartVal
                    || mergeStartVal == MergedCellValues.Continue)
                {
                    return currentDay;
                }

                if (mergeStart.Val != MergedCellValues.Restart)
                {
                    throw new NotSupportedException($"Unsupported merge cell command: {mergeStart.Val}");
                }

                if (cell.InnerText is not { } dayNameText)
                {
                    throw new NotSupportedException("The day name column must include the day name");
                }

                // parse
                if (!dayNameToDay.TryGetValue(dayNameText, out var day))
                {
                    throw new NotSupportedException($"The day {dayNameText} is invalid");
                }

                return day;
            }

            int TimeSlotCol()
            {
                if (props?.VerticalMerge is not { } mergeStart)
                {
                    throw new NotSupportedException("Invalid format");
                }

                if (mergeStart.Val is not { } mergeStartVal
                    || mergeStartVal == MergedCellValues.Continue)
                {
                    return currentTimeSlotIndex;
                }

                if (mergeStart.Val != MergedCellValues.Restart)
                {
                    throw new NotSupportedException($"Unsupported merge cell command: {mergeStart.Val}");
                }

                // I
                // 8:00-9:30


                // Two paragraphs
                using var paragraphs = cell.ChildElements.OfType<Paragraph>().GetEnumerator();
                if (!paragraphs.MoveNext())
                {
                    throw new NotSupportedException("Invalid time slot cell");
                }

                int newCurrentSlot;
                {
                    var numberParagraph = paragraphs.Current;
                    if (numberParagraph.InnerText is not { } numberText)
                    {
                        throw new NotSupportedException("The time slot must contain the ordinal first");
                    }

                    var num = NumberHelper.FromRoman(numberText);
                    if (num != currentTimeSlotIndex + 1)
                    {
                        throw new NotSupportedException("The time slot number must be in order");
                    }

                    newCurrentSlot = num;
                }

                if (!paragraphs.MoveNext())
                {
                    throw new NotSupportedException("");
                }

            }

            var colSpan = props?.GridSpan?.Val ?? 1;

            switch (columnIndex)
            {
                case dayColumnIndex or timeSlotColumnIndex:
                {
                    if (colSpan != 0)
                    {
                        throw new NotSupportedException("The day and time slot columns must be one column in width");
                    }
                    break;
                }
            }

            columnIndex += colSpan;
        }
    }

    foreach (var (rowIndex, row) in rows.WithIndex())
    {
        var cells = row.ChildElements.OfType<TableCell>();
        // Read the header.
        if (rowIndex == 0)
        {
        }

        var props = row.TableRowProperties;

        foreach (var maybeCell in row.ChildElements)
        {
            if (maybeCell is not TableCell cell)
            {
                continue;
            }

            if (!cell.InnerText.Contains("Progr.JAVA"))
            {
                continue;
            }

            if (cell.TableCellProperties?.GridSpan is { } span)
            {
                Console.WriteLine(span);
            }
        }
    }

    GroupId GroupId(int colIndex)
    {
        return new(colOffset + colIndex);
    }

    int HeaderRow(int colOffset1)
    {
        int skippedCount = 0;
        int goodCellIndex = 0;
        using var cellEnumerator = Cells().GetEnumerator();
        if (cellEnumerator.MoveNext())
        {
            return 0;
        }
        while (true)
        {
            var text = cellEnumerator.Current.InnerText;
            if (text != "")
            {
                break;
            }
            skippedCount++;

            if (!cellEnumerator.MoveNext())
            {
                return skippedCount;
            }
        }
        while (true)
        {
            var text = cellEnumerator.Current.InnerText;
            var expectedId = goodCellIndex + colOffset1;
            var group = schedule.Group(text);
            Debug.Assert(expectedId == group.Id.Value);

            goodCellIndex++;
            if (!cellEnumerator.MoveNext())
            {
                return skippedCount;
            }
        }
    }

}
