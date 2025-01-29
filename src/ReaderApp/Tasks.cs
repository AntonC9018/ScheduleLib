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
        sheets.AppendChild(sheet);

        {
            var mergeCells = worksheet.Elements<MergeCells>().FirstOrDefault();
            if (mergeCells is null)
            {
                mergeCells = new MergeCells();
                worksheet.InsertAfter(mergeCells, sheetData);
            }

            uint timeSlotCount = (uint) p.TimeConfig.TimeSlotCount;
            uint initialRowIndex = 1;
            for (uint dayIndex = 0; dayIndex < 6; dayIndex++)
            {
                var merge = new MergeCell();
                uint rowIndexStart = initialRowIndex + dayIndex * timeSlotCount;
                uint rowIndexEnd = rowIndexStart + timeSlotCount - 1;
                merge.Reference = GetCellRange(new()
                {
                    Start = new(ColIndex: 0, RowIndex: rowIndexStart),
                    EndInclusive = new(ColIndex: 0, RowIndex: rowIndexEnd),
                    StringBuilder = p.StringBuilder,
                });
                mergeCells.AppendChild(merge);
            }
        }

        uint rotatedTextFormatId;
        {
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();
            using var stylesheet = StylesheetBuilder.CreateWithDefaults(stylesPart);

            rotatedTextFormatId = stylesheet.CellFormat(x =>
            {
                x.Alignment = new()
                {
                    TextRotation = 90,
                    Horizontal = HorizontalAlignmentValues.Center,
                    Vertical = VerticalAlignmentValues.Center,
                };
                x.ApplyAlignment = true;
            });
        }

        var strings = new Dictionary<string, int>();


        TopHeader();
        Body();

        var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
        var sharedStringTable = new SharedStringTable();
        sharedStringPart.SharedStringTable = sharedStringTable;

        foreach (var s in strings.OrderBy(x => x.Value))
        {
            var item = new SharedStringItem(new Text(s.Key));
            sharedStringTable.AppendChild(item);
        }
        return;

        Row NextRow()
        {
            var row = new Row();
            sheetData.AppendChild(row);
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
            row.AppendChild(NextCell());

            {
                var cell = NextCell();
                cell.SetStringValue("         Profesor\n  Ora");
                row.AppendChild(cell);
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
                row.AppendChild(cell);
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
                        cell.StyleIndex = rotatedTextFormatId;
                        row.AppendChild(cell);
                    }
                    else
                    {
                        var cell = NextCell();
                        cell.StyleIndex = rotatedTextFormatId;
                        row.AppendChild(cell);
                    }

                    {
                        var id = firstTimeSlotStringId + timeSlotIndex;
                        var cell = NextCell();
                        cell.SetSharedStringValue(id);
                        row.AppendChild(cell);
                    }

                    bool isSeminarDate = day == p.SeminarDate.Day && timeSlot == p.SeminarDate.TimeSlot;

                    for (int teacherId = 0; teacherId < p.Schedule.Teachers.Length; teacherId++)
                    {
                        var cell = NextCell();
                        row.AppendChild(cell);

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

    public record struct CellPosition(uint ColIndex, uint RowIndex);

    public struct AppendCellReferenceParams
    {
        public required StringBuilder StringBuilder;
        public required CellPosition Position;
    }

    private static void AppendCellReference(AppendCellReferenceParams p)
    {
        Span<char> stack = stackalloc char[8];
        int stackPos = 0;

        uint remaining = p.Position.ColIndex + 1;
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
            p.StringBuilder.Append(stack[j]);
        }

        p.StringBuilder.Append(p.Position.RowIndex + 1);
    }

    private static StringValue GetCellReference(AppendCellReferenceParams p)
    {
        Debug.Assert(p.StringBuilder.Length == 0);
        AppendCellReference(p);
        var ret = p.StringBuilder.ToStringAndClear();
        return new StringValue(ret);
    }

    public struct AppendCellRangeParams
    {
        public required StringBuilder StringBuilder;
        public required CellPosition Start;
        public required CellPosition EndInclusive;
    }

    private static void AppendCellRange(AppendCellRangeParams p)
    {
        AppendCellReference(new()
        {
            StringBuilder = p.StringBuilder,
            Position = p.Start,
        });
        p.StringBuilder.Append(':');
        AppendCellReference(new()
        {
            StringBuilder = p.StringBuilder,
            Position = p.EndInclusive,
        });
    }

    private static StringValue GetCellRange(AppendCellRangeParams p)
    {
        Debug.Assert(p.StringBuilder.Length == 0);
        AppendCellRange(p);
        var ret = p.StringBuilder.ToStringAndClear();
        return new StringValue(ret);
    }

    public abstract class CountedAdderBase<T, TItem>
        where T : OpenXmlCompositeElement, new()
        where TItem : OpenXmlElement, new()
    {
        public T Element { get; } = new();
        private uint _index = 0;

        public void AssertHasDefault()
        {
            Debug.Assert(_index != 0);
        }

        public bool HasDefault()
        {
            return _index != 0;
        }

        public (uint Id, TItem Item) Next()
        {
            var ret = new TItem();
            Element.Append(ret);
            var id = _index;
            _index++;
            return (id, ret);
        }

        public virtual TItem AppendDefault()
        {
            (_, var it) = Next();
            return it;
        }
    }

    public sealed class CountedFonts : CountedAdderBase<Fonts, Font>
    {
    }

    public sealed class CountedFills : CountedAdderBase<Fills, Fill>
    {
        public override Fill AppendDefault()
        {
            var fill = base.AppendDefault();
            fill.PatternFill = new()
            {
                PatternType = PatternValues.None,
            };
            return fill;
        }
    }

    public sealed class CountedBorders : CountedAdderBase<Borders, Border>
    {
    }

    public sealed class CountedCellFormats : CountedAdderBase<CellFormats, CellFormat>
    {
    }

    public sealed class StylesheetBuilder : IDisposable
    {
        private readonly Stylesheet _stylesheet;
        private readonly CountedFonts _fonts;
        private readonly CountedFills _fills;
        private readonly CountedBorders _borders;
        private readonly CountedCellFormats _cellFormats;

        public StylesheetBuilder(
            Stylesheet stylesheet,
            CountedFonts fonts,
            CountedFills fills,
            CountedBorders borders,
            CountedCellFormats cellFormats)
        {
            _stylesheet = stylesheet;
            _fonts = fonts;
            _fills = fills;
            _borders = borders;
            _cellFormats = cellFormats;
        }

        public uint Font(Action<Font> configure)
        {
            var (id, font) = _fonts.Next();
            configure(font);
            return id;
        }

        public uint Fill(Action<Fill> configure)
        {
            var (id, fill) = _fills.Next();
            configure(fill);
            return id;
        }

        public uint Border(Action<Border> configure)
        {
            var (id, border) = _borders.Next();
            configure(border);
            return id;
        }

        public uint CellFormat(Action<CellFormat> configure)
        {
            var (id, cellFormat) = _cellFormats.Next();
            configure(cellFormat);
            return id;
        }

        public void Dispose()
        {
            if (!_fonts.HasDefault())
            {
                _fonts.AppendDefault();
            }
            if (!_fills.HasDefault())
            {
                _fills.AppendDefault();
            }
            if (!_borders.HasDefault())
            {
                _borders.AppendDefault();
            }
            if (!_cellFormats.HasDefault())
            {
                _cellFormats.AppendDefault();
            }
            _stylesheet.Save();
        }

        public static StylesheetBuilder CreateWithDefaults(WorkbookStylesPart stylesPart)
        {
            var stylesheet = new Stylesheet();
            stylesPart.Stylesheet = stylesheet;

            var fonts = new CountedFonts();
            stylesheet.AppendChild(fonts.Element);
            fonts.AppendDefault();

            var fills = new CountedFills();
            stylesheet.AppendChild(fills.Element);
            fills.AppendDefault();

            var borders = new CountedBorders();
            stylesheet.AppendChild(borders.Element);
            borders.AppendDefault();

            var cellFormats = new CountedCellFormats();
            stylesheet.AppendChild(cellFormats.Element);
            cellFormats.AppendDefault();

            var ret = new StylesheetBuilder(
                stylesheet,
                fonts,
                fills,
                borders,
                cellFormats);
            return ret;
        }
    }
}
