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
    int currentTimeSlotOrdinal = 0;
    TimeSlot currentTimeSlot = default;

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
                    (currentTimeSlotOrdinal, currentTimeSlot) = TimeSlotCol();
                    break;
                }
                default:
                {
                    NormalCol();
                    break;
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

            (int Ordinal, TimeSlot TimeSlot) TimeSlotCol()
            {
                if (props?.VerticalMerge is not { } mergeStart)
                {
                    throw new NotSupportedException("Invalid format");
                }

                if (mergeStart.Val is not { } mergeStartVal
                    || mergeStartVal == MergedCellValues.Continue)
                {
                    return (currentTimeSlotOrdinal, currentTimeSlot);
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

                int newTimeSlotOrdinal;
                {
                    var numberParagraph = paragraphs.Current;
                    if (numberParagraph.InnerText is not { } numberText)
                    {
                        throw new NotSupportedException("The time slot must contain the ordinal first");
                    }

                    var num = NumberHelper.FromRoman(numberText);
                    if (num != currentTimeSlotOrdinal + 1)
                    {
                        throw new NotSupportedException("The time slot number must be in order");
                    }

                    newTimeSlotOrdinal = num;
                }

                if (!paragraphs.MoveNext())
                {
                    throw new NotSupportedException("");
                }

                var time = Time();

                if (paragraphs.MoveNext())
                {
                    throw new NotSupportedException("Extra paragraphs");
                }

                {
                    var timeStarts = timeConfig.Base.TimeSlotStarts;
                    var timeSlotIndex = currentTimeSlot.Index + 1;
                    if (timeStarts[timeSlotIndex] != time.Start)
                    {
                        throw new NotSupportedException("The time slots must follow each other");
                    }

                    var expectedEndTime = time.Start.Add(timeConfig.Base.LessonDuration);
                    if (expectedEndTime != time.End)
                    {
                        throw new NotSupportedException($"The lesson durations must all be equal to the default duration ({timeConfig.Base.LessonDuration} minutes)");
                    }

                    return (newTimeSlotOrdinal, new(timeSlotIndex));
                }

                (TimeOnly Start, TimeOnly End) Time()
                {
                    var timeParagraph = paragraphs.Current;
                    if (timeParagraph.InnerText is not { } timeText)
                    {
                        throw new NotSupportedException("The time slot must contain the time range second");
                    }

                    // HH:MM-HH:MM
                    var parser = new Parser(timeText);
                    parser.SkipWhitespace();
                    var startTime = ParseTime(ref parser);
                    if (parser.IsEmpty || parser.Current != '-')
                    {
                        throw new NotSupportedException("Expected '-' after start time");
                    }
                    parser.Move();
                    var endTime = ParseTime(ref parser);

                    parser.SkipWhitespace();

                    if (!parser.IsEmpty)
                    {
                        throw new NotSupportedException("Time range not consumed fully");
                    }

                    return (startTime, endTime);
                }

            }

            void NormalCol()
            {
                if (!ShouldAdd())
                {
                    return;
                }

                // 15:00 Baze de date(curs)
                // L.Novac    404/4
                // (optional) time override
                // name
                // (optional) (type,parity) or (parity) or (type)  -- A
                // (optional) second name + A
                // (optional) roman subgroup:    --  I:
                // professor name
                // cab (optional?)

                // First organize as just lines, it's easier
                var lines = cell.ChildElements
                    .OfType<Paragraph>()
                    .SelectMany(x => x.InnerText.Split("\n"));




                bool ShouldAdd()
                {
                    if (props?.VerticalMerge is not { } merge)
                    {
                        return true;
                    }
                    if (merge.Val is { } val
                        && val == MergedCellValues.Restart)
                    {
                        return true;
                    }
                    return false;
                }
            }
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
