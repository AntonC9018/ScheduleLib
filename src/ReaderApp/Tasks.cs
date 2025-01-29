using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ScheduleLib;
using ScheduleLib.Generation;

namespace ReaderApp;

public struct GeneratePdfForGroupsAndTeachersParams
{
    public required PdfLessonTextDisplayHandler.Services LessonTextDisplayServices;
    public required LessonTimeConfig LessonTimeConfig;
    public required TimeSlotDisplayHandler TimeSlotDisplay;
    public required DayNameProvider DayNameProvider;
    public required Schedule Schedule;
    public required string OutputPath;
}

public static class Tasks
{
    public static async Task GeneratePdfForGroupsAndTeachers(GeneratePdfForGroupsAndTeachersParams p)
    {
        var outputDirPath = p.OutputPath;
        Directory.CreateDirectory(outputDirPath);
        foreach (var filePath in Directory.EnumerateFiles(outputDirPath, "*.pdf", SearchOption.TopDirectoryOnly))
        {
            File.Delete(filePath);
        }

        QuestPDF.Settings.License = LicenseType.Community;

        var tasks = new List<Task>();
        {
            var textDisplayHandler = new PdfLessonTextDisplayHandler(p.LessonTextDisplayServices, new());
            for (int groupId = 0; groupId < p.Schedule.Groups.Length; groupId++)
            {
                int groupId1 = groupId;
                var t = Task.Run(() =>
                {
                    var groupName = p.Schedule.Groups[groupId1].Name;
                    var fileName = groupName + ".pdf";
                    GenerateWithFilter(fileName, textDisplayHandler, new()
                    {
                        GroupFilter = new()
                        {
                            GroupIds = [new(groupId1)],
                        },
                    });
                });
                tasks.Add(t);
            }
        }

        {
            var textDisplayHandler = new PdfLessonTextDisplayHandler(p.LessonTextDisplayServices, new()
            {
                PrintsTeacherName = false,
            });
            var sb = new StringBuilder();
            for (int teacherId = 0; teacherId < p.Schedule.Teachers.Length; teacherId++)
            {
                int teacherId1 = teacherId;

                var teacherName = p.Schedule.Teachers[teacherId1].PersonName;
                sb.Append(teacherName.ShortFirstName.Span.Shortened.Value);
                sb.Append('_');
                sb.Append(teacherName.LastName);
                sb.Append(".pdf");

                var fileName = sb.ToStringAndClear();

                var t = Task.Run(() =>
                {
                    GenerateWithFilter(fileName, textDisplayHandler, new()
                    {
                        TeacherFilter = new()
                        {
                            IncludeIds = [new(teacherId1)],
                        },
                    });
                });
                tasks.Add(t);
            }
        }
        await Task.WhenAll(tasks);

        void GenerateWithFilter(
            string name,
            PdfLessonTextDisplayHandler textDisplayHandler,
            in ScheduleFilter filter)
        {
            var filteredSchedule = p.Schedule.Filter(filter);
            if (filteredSchedule.IsEmpty)
            {
                return;
            }

            var generator = new GroupColumnScheduleTableDocument(filteredSchedule, new()
            {
                StringBuilder = new(),
                LessonTextDisplayHandler = textDisplayHandler,
                LessonTimeConfig = p.LessonTimeConfig,
                TimeSlotDisplay = p.TimeSlotDisplay,
                DayNameProvider = p.DayNameProvider,
            });

            var path = Path.Combine(outputDirPath, name);
            generator.GeneratePdf(path);
        }
    }

    public struct AllTeacherExcelParams
    {
        public required string OutputFilePath;
        public required DayNameProvider DayNameProvider;
        public required (DayOfWeek Day, TimeSlot TimeSlot) SeminarDate;
        public required StringBuilder StringBuilder;
        public required LessonTypeDisplayHandler LessonTypeDisplay;
        public required ParityDisplayHandler ParityDisplay;
        public required TimeSlotDisplayHandler TimeSlotDisplay;
        public required Schedule Schedule;
        public required LessonTimeConfig TimeConfig;
    }

    public static void GenerateAllTeacherExcel(AllTeacherExcelParams p)
    {
        using var stream = File.Open(p.OutputFilePath, FileMode.Create, FileAccess.ReadWrite);
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
            sheetData.Append(row);
            return row;
        }

        Cell NextCell()
        {
            var cell = new Cell();
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
                cell.SetStringValue("         Profesor\n  Ora");
                row.Append(cell);
            }

            var sb = p.StringBuilder;
            for (int i = 0; i < p.Schedule.Teachers.Length; i++)
            {
                LessonTextDisplayHelper.AppendTeacherName(new()
                {
                    InsertSpaceAfterShortName = true,
                    Output = sb,
                    Teacher = p.Schedule.Teachers[i],
                    LastNameFirst = true,
                    PreferLonger = true,
                });
                var teacherName = sb.ToStringAndClear();
                var cell = NextCell();
                cell.SetStringValue(teacherName);
                row.Append(cell);
            }
        }

#pragma warning disable CS8321 // Local function is declared but never used
        void Body()
#pragma warning restore CS8321 // Local function is declared but never used
        {
            var mappingByCell = MappingsCreationHelper.CreateCellMappings(
                p.Schedule.RegularLessons,
                l => l.Lesson.Teachers);
            int timeSlotCount = p.TimeConfig.TimeSlotCount;

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
                        var dayName = p.DayNameProvider.GetDayName(day);
                        var cell = NextCell();
                        cell.SetStringValue(dayName);
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
                        cell.SetSharedStringValue(id);
                        row.Append(cell);
                    }

                    bool isSeminarDate = day == p.SeminarDate.Day && timeSlot == p.SeminarDate.TimeSlot;

                    for (int teacherId = 0; teacherId < p.Schedule.Teachers.Length; teacherId++)
                    {
                        var cell = NextCell();
                        row.Append(cell);

                        if (isSeminarDate)
                        {
                            cell.SetSharedStringValue(seminarStringId);
                            continue;
                        }

                        var cellKey = rowKey.CellKey(new TeacherId(teacherId));
                        if (!mappingByCell.TryGetValue(cellKey, out var lessons))
                        {
                            continue;
                        }

                        var sb = p.StringBuilder;
                        for (int lessonIndex = 0; lessonIndex < lessons.Count; lessonIndex++)
                        {
                            if (lessonIndex != 0)
                            {
                                sb.AppendLine();
                            }

                            var lesson = lessons[lessonIndex];
                            var listBuilder = new ListStringBuilder(sb);
                            {
                                var course = p.Schedule.Get(lesson.Lesson.Course);
                                listBuilder.Append(course.Names[^1]);
                            }
                            if (p.LessonTypeDisplay.Get(lesson.Lesson.Type) is { } lessonTypeName)
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
                                    var group = p.Schedule.Get(groupId);
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
                                var parityName = p.ParityDisplay.Get(lesson.Date.Parity);
                                listBuilder.Append($"({parityName})");
                            }
                        }

                        Debug.Assert(sb.Length > 0);

                        {
                            var stringId = GetOrAddString(sb.ToStringAndClear());
                            cell.SetSharedStringValue(stringId);
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
                    var interval = p.TimeConfig.GetTimeSlotInterval(timeSlot);
                    interval = interval with
                    {
                        Duration = interval.Duration.Add(TimeSpan.FromMinutes(1)),
                    };
                    var timeSlotString = p.TimeSlotDisplay.IntervalDisplay(interval);
                    var ret = AddString(timeSlotString);
                    return ret;
                }
            }
        }


    }

    private static void SetSharedStringValue(this Cell cell, int stringId)
    {
        cell.DataType = CellValues.SharedString;
        cell.CellValue = new(stringId);
    }

    private static void SetStringValue(this Cell cell, string str)
    {
        cell.DataType = CellValues.String;
        cell.CellValue = new(str);
    }
}
