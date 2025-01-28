using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DocumentFormat.OpenXml;
using ScheduleLib.Generation;
using ScheduleLib.Parsing.WordDoc;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using ReaderApp;
using ScheduleLib;
using ScheduleLib.Builders;

var dayNameProvider = new DayNameProvider();
var context = DocParseContext.Create(new()
{
    DayNameProvider = dayNameProvider,
    CourseNameParserConfig = new(new()
    {
        ProgrammingLanguages = ["Java", "C++", "C#"],
        IgnoredFullWords = ["p/u", "pentru"],
        IgnoredShortenedWords = ["Opț"],
        MinUsefulWordLength = 3,
    }),
});

context.Schedule.ConfigureRemappings(remap =>
{
    var teach = remap.TeacherLastNameRemappings;
    teach.Add("Curmanschi", "Curmanschii");
    teach.Add("Băț", "Beț");
    teach.Add("Spincean", "Sprîncean");
});

{
    // Register the teachers from the list.
    const string fileName = @"data\Cadre didactice DI 2024-2025.xlsx";
    using var excel = SpreadsheetDocument.Open(fileName, isEditable: false, new()
    {
        AutoSave = false,
        CompatibilityLevel = CompatibilityLevel.Version_2_20,
    });

    ExcelTeacherListParser.AddTeachersFromExcel(new()
    {
        Excel = excel,
        Schedule = context.Schedule,
    });
}
{
    context.Schedule.SetStudyYear(2024);

    const string dirName = @"data\2024_sem2";
    foreach (var filePath in Directory.EnumerateFiles(dirName, "*.docx", SearchOption.TopDirectoryOnly))
    {
        using var document = WordprocessingDocument.Open(filePath, isEditable: false);
        WordScheduleParser.ParseToSchedule(new()
        {
            Context = context,
            Document = document,
        });
    }
}

var schedule = context.BuildSchedule();
CancellationToken cancellationToken = default;
_ = cancellationToken;

#if true

const string outputFile = "all_teachers_orar.xlsx";
string outputFileFullPath = Path.GetFullPath(outputFile);
ExcelStuff(outputFileFullPath);

ExplorerHelper.OpenFolderAndSelectFile(outputFileFullPath);

void ExcelStuff(string outputFile)
{
    var timeConfig = new DefaultLessonTimeConfig(context.TimeConfig);

    (
        DayNameProvider DayNameProvider,
        (DayOfWeek Day, TimeSlot TimeSlot) SeminarDate,
        StringBuilder StringBuilder,
        LessonTypeDisplayHandler LessonTypeDisplay,
        ParityDisplayHandler ParityDisplay,
        TimeSlotDisplayHandler TimeSlotDisplay)

        t = (

        DayNameProvider: new DayNameProvider(),
        SeminarDate: (DayOfWeek.Wednesday, timeConfig.T15_00),
        StringBuilder: new(),
        LessonTypeDisplay: new(),
        ParityDisplay: new(),
        TimeSlotDisplay: new());

    using var stream = File.Open(outputFile, FileMode.Create, FileAccess.ReadWrite);
    using var excel = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true);
    var workbookPart = excel.AddWorkbookPart();

    var workbook = new Workbook();
    workbookPart.Workbook = workbook;

    var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
    var sheetData = new SheetData();
    var worksheet = new Worksheet(sheetData);
    worksheetPart.Worksheet = worksheet;

    var sheets = workbook.AppendChild(new Sheets());
    var sheet = new Sheet
    {
        Id = workbookPart.GetIdOfPart(worksheetPart),
        SheetId = 1,
        Name = "main",
    };
    sheets.Append(sheet);

    var strings = new Dictionary<string, int>();
    uint rowIndex = 0;
    uint colIndex = 0;

    TopHeader();
    Body();

    var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
    var sharedStringTable = new SharedStringTable();
    sharedStringPart.SharedStringTable = sharedStringTable;

    foreach (var s in strings.OrderBy(x => x.Value))
    {
        var item = new SharedStringItem(new Text(s.Key));
        sharedStringTable.Append(item);
    }
    return;

    Row NextRow()
    {
        var row = new Row();
        // row.RowIndex = rowIndex;
        rowIndex++;
        sheetData.Append(row);
        colIndex = 0;
        return row;
    }

    Cell NextCell()
    {
        var sb = t.StringBuilder;
        Debug.Assert(sb.Length == 0);

        Span<char> stack = stackalloc char[8];
        int stackPos = 0;

        uint remaining = colIndex + 1;
        while (true)
        {
            const uint base_ = 'Z' - 'A' + 1;
            byte remainder = (byte)((remaining - 1) % base_);
            byte letter = (byte)('A' + remainder);
            char ch = (char) letter;
            stack[stackPos] = ch;
            stackPos++;

            remaining -= remainder;
            remaining /= base_;
            if (remaining == 0)
            {
                break;
            }
        }

        for (int j = stackPos - 1; j >= 0; j--)
        {
            sb.Append(stack[j]);
        }

        sb.Append(rowIndex);

        var cell = new Cell();
        var cellRef = sb.ToString();
        // cell.CellReference = cellRef;
        sb.Clear();
        colIndex++;

        return cell;
    }

    int GetOrAddString(string s)
    {
        ref var ret = ref CollectionsMarshal.GetValueRefOrAddDefault(strings, s, out var exists);
        if (!exists)
        {
            ret = strings.Count - 1;
        }
        return ret;
    }
    int AddString(string s)
    {
        var index = strings.Count;
        strings.Add(s, index);
        return index;
    }

    // top header
    void TopHeader()
    {
        var row = NextRow();

        // Empty corner
        row.Append(NextCell());

        {
            var cell = NextCell();
            cell.DataType = CellValues.String;
            cell.CellValue = new("         Profesor\n  Ora");
            row.Append(cell);
        }

        var sb = t.StringBuilder;
        for (int i = 0; i < schedule.Teachers.Length; i++)
        {
            LessonTextDisplayHelper.AppendTeacherName(new()
            {
                InsertSpaceAfterShortName = true,
                Output = sb,
                Teacher = schedule.Teachers[i],
                LastNameFirst = true,
                PreferLonger = true,
            });
            var teacherName = sb.ToStringAndClear();
            var cell = NextCell();
            cell.DataType = CellValues.String;
            cell.CellValue = new(teacherName);
            row.Append(cell);
        }
    }

#pragma warning disable CS8321 // Local function is declared but never used
    void Body()
#pragma warning restore CS8321 // Local function is declared but never used
    {
        var mappingByCell = MappingsCreationHelper.CreateCellMappings(
            schedule.RegularLessons,
            l => l.Lesson.Teachers);
        int timeSlotCount = timeConfig.Base.TimeSlotCount;

        var seminarStringId = AddString("Seminarul DI");
        int firstTimeSlotStringId = AddTimeSlotStrings();

        for (int dayIndex = 0; dayIndex < 6; dayIndex++)
        {
            var day = DayOfWeek.Monday + dayIndex;

            for (int timeSlotIndex = 0; timeSlotIndex < timeSlotCount; timeSlotIndex++)
            {
                var timeSlot = new TimeSlot(timeSlotIndex);
                var rowKey = new RowKey
                {
                    TimeSlot = timeSlot,
                    DayOfWeek = day,
                };

                var row = NextRow();

                if (timeSlotIndex == 0)
                {
                    var dayName = t.DayNameProvider.GetDayName(day);
                    var cell = NextCell();
                    cell.CellValue = new(dayName);
                    cell.DataType = CellValues.String;
                    row.Append(cell);
                }
                else
                {
                    // TODO: Should be merged. But this is done in another component.
                    var cell = NextCell();
                    row.Append(cell);
                }

                {
                    var id = firstTimeSlotStringId + timeSlotIndex;
                    var cell = NextCell();
                    cell.CellValue = new(id);
                    cell.DataType = CellValues.SharedString;
                    row.Append(cell);
                }

                bool isSeminarDate = day == t.SeminarDate.Day && timeSlot == t.SeminarDate.TimeSlot;

                for (int teacherId = 0; teacherId < schedule.Teachers.Length; teacherId++)
                {
                    var cell = NextCell();
                    row.Append(cell);

                    if (isSeminarDate)
                    {
                        cell.CellValue = new(seminarStringId);
                        cell.DataType = CellValues.SharedString;
                        continue;
                    }

                    var cellKey = rowKey.CellKey(new TeacherId(teacherId));
                    if (!mappingByCell.TryGetValue(cellKey, out var lessons))
                    {
                        continue;
                    }

                    var sb = t.StringBuilder;
                    for (int lessonIndex = 0; lessonIndex < lessons.Count; lessonIndex++)
                    {
                        if (lessonIndex != 0)
                        {
                            sb.AppendLine();
                        }

                        var lesson = lessons[lessonIndex];
                        var listBuilder = new ListStringBuilder(sb);
                        {
                            var course = schedule.Get(lesson.Lesson.Course);
                            listBuilder.Append(course.Names[^1]);
                        }
                        if (t.LessonTypeDisplay.Get(lesson.Lesson.Type) is { } lessonTypeName)
                        {
                            listBuilder.Append($"({lessonTypeName})");
                        }
                        if (lesson.Lesson.Room != RoomId.Invalid)
                        {
                            listBuilder.Append($"{lesson.Lesson.Room.Id}");
                        }

                        {
                            var groups = lesson.Lesson.Groups;
                            if (groups.Count > 0)
                            {
                                listBuilder.MaybeAppendSeparator();
                            }

                            var commaListBuilder = new ListStringBuilder(sb, ',');
                            foreach (var groupId in lesson.Lesson.Groups)
                            {
                                var group = schedule.Get(groupId);
                                commaListBuilder.MaybeAppendSeparator();
                                LessonTextDisplayHelper.AppendGroupNameWithLanguage(sb, group);
                            }
                        }
                        if (lesson.Lesson.SubGroup != SubGroup.All)
                        {
                            sb.Append($"-{lesson.Lesson.SubGroup.Value}");
                        }
                        if (lesson.Date.Parity != Parity.EveryWeek)
                        {
                            var parityName = t.ParityDisplay.Get(lesson.Date.Parity);
                            listBuilder.Append($"({parityName})");
                        }
                    }

                    Debug.Assert(sb.Length > 0);

                    {
                        var stringId = GetOrAddString(sb.ToStringAndClear());
                        cell.DataType = CellValues.SharedString;
                        cell.CellValue = new(stringId);
                    }
                }
            }
        }

        int AddTimeSlotStrings()
        {
            int firstId = Add(0);
            for (int i = 1; i < timeSlotCount; i++)
            {
                Add(i);
            }
            return firstId;

            int Add(int timeSlotIndex)
            {
                var timeSlot = new TimeSlot(timeSlotIndex);
                var interval = timeConfig.Base.GetTimeSlotInterval(timeSlot);
                interval = interval with
                {
                    Duration = interval.Duration.Add(TimeSpan.FromMinutes(1)),
                };
                var timeSlotString = t.TimeSlotDisplay.IntervalDisplay(interval);
                var ret = AddString(timeSlotString);
                return ret;
            }
        }
    }


}
#else
{
    await Tasks.GeneratePdfForGroupsAndTeachers(new()
    {
        LessonTextDisplayServices = new()
        {
            ParityDisplay = new(),
            LessonTypeDisplay = new(),
            SubGroupNumberDisplay = new(),
        },
        Schedule = schedule,
        LessonTimeConfig = context.TimeConfig,
        TimeSlotDisplay = new(),
        DayNameProvider = dayNameProvider,
        OutputPath = "output",
    });
}
#endif

