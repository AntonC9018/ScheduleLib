using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using RunProperties = DocumentFormat.OpenXml.Drawing.RunProperties;

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

        var strings = StringTableBuilder.Create(workbookPart);

        ConfigureWidths();
        ConfigureMerges();

        var styles = ConfigureStylesheet(workbookPart);

        var cells = new CellsBuilder(sheetData);
        TopHeader();
        Body();
        return;

        void ConfigureWidths()
        {
            var columns = new Columns();

            // Order matters for these.
            worksheet.InsertBefore(
                newChild: columns,
                referenceChild: sheetData);

            double FromPixels(int px)
            {
                const double c = 8.43 / 64.0;
                return px * c;
            }
            var dayColumn = new Column
            {
                Min = 1,
                Max = 1,
                Width = FromPixels(30),
                CustomWidth = true,
            };
            columns.AppendChild(dayColumn);

            var timeSlotColumn = new Column
            {
                Min = 2,
                Max = 2,
                Width = FromPixels(85),
                CustomWidth = true,
            };
            columns.AppendChild(timeSlotColumn);

            var teacherColumns = new Column
            {
                Min = 3,
                Max = (uint)(3 + p.Schedule.Teachers.Length),
                Width = FromPixels(100),
                CustomWidth = true,
            };
            columns.AppendChild(teacherColumns);
        }

        void ConfigureMerges()
        {
            var mergeCells = worksheet.Elements<MergeCells>().FirstOrDefault();
            if (mergeCells is null)
            {
                mergeCells = new MergeCells();
                worksheet.InsertAfter(
                    newChild: mergeCells,
                    referenceChild: sheetData);
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

        void TopHeader()
        {
            var row = cells.NextRow();
            // row.Height = 28;
            // row.CustomHeight = true;
            _ = row;

            _ = cells.NextCell();

            {
                var cell = cells.NextCell();
                // Excel strips spaces without this.
                // Width should be 12
                cell.SetStringValue($"{Spaces(7)}Profesor\n{Spaces(3)}Ora");
                cell.SetStyle(styles.HeaderTitle);
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
                var cell = cells.NextCell();
                cell.SetStringValue(teacherName);
                cell.SetStyle(styles.Teacher);
            }
        }

        void Body()
        {
            var mappingByCell = MappingsCreationHelper.CreateCellMappings(
                p.Schedule.RegularLessons,
                l => l.Lesson.Teachers);
            int timeSlotCount = p.TimeConfig.TimeSlotCount;

            var seminarStringId = strings.AddString("Seminarul DI");
            var firstTimeSlotStringId = AddTimeSlotStrings();

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

                    var row = cells.NextRow();
                    row.Height = 60;
                    row.CustomHeight = true;

                    var option = EdgeOptionFromIndex(
                        oddnessIndex: dayIndex,
                        edgenessIndex: timeSlotIndex,
                        height: timeSlotCount);

                    {
                        var cell = cells.NextCell();
                        if (timeSlotIndex == 0)
                        {
                            var dayName = p.DayNameProvider.GetDayName(day);
                            cell.SetStringValue(dayName);
                        }
                        cell.SetStyle(styles.Day.Get(option.IsOdd()));
                    }

                    {
                        var id = new SharedStringItemId(firstTimeSlotStringId.Value + timeSlotIndex);
                        var cell = cells.NextCell();
                        cell.SetSharedStringValue(id);
                        cell.SetStyle(styles.TimeSlot.Get(option));
                    }

                    bool isSeminarDate = day == p.SeminarDate.Day && timeSlot == p.SeminarDate.TimeSlot;

                    for (int teacherId = 0; teacherId < p.Schedule.Teachers.Length; teacherId++)
                    {
                        var cell = cells.NextCell();

                        if (isSeminarDate)
                        {
                            cell.SetSharedStringValue(seminarStringId);

                            var styleId = styles.Seminar.Get(option.GetEdgeness());
                            cell.SetStyle(styleId);

                            continue;
                        }

                        cell.SetStyle(styles.Lesson.Get(option));

                        var cellKey = rowKey.CellKey(new TeacherId(teacherId));
                        if (!mappingByCell.TryGetValue(cellKey, out var lessons))
                        {
                            continue;
                        }

                        {
                            var sb = p.StringBuilder;
                            FormatLessons(sb, lessons);
                            Debug.Assert(sb.Length > 0);

                            var stringId = strings.GetOrAddString(sb.ToStringAndClear());
                            cell.SetSharedStringValue(stringId);
                        }

                    }
                }
            }

            SharedStringItemId AddTimeSlotStrings()
            {
                var firstId = Add(0);
                for (int i = 1; i < timeSlotCount; i++)
                {
                    Add(i);
                }
                return firstId;

                SharedStringItemId Add(int timeSlotIndex)
                {
                    var timeSlot = new TimeSlot(timeSlotIndex);
                    var interval = p.TimeConfig.GetTimeSlotInterval(timeSlot);
                    interval = interval with
                    {
                        Duration = interval.Duration.Add(TimeSpan.FromMinutes(1)),
                    };
                    var timeSlotString = p.TimeSlotDisplay.IntervalDisplay(interval);
                    var ret = strings.AddString(timeSlotString);
                    return ret;
                }
            }
        }

        void FormatLessons(StringBuilder sb, List<RegularLesson> lessons)
        {
            if (TryUniteLessonsBasedOnParity())
            {
                return;
            }

            if (TryUniteLessonBasedOnEqualityOfAllButGroup())
            {
                return;
            }

            for (int lessonIndex = 0; lessonIndex < lessons.Count; lessonIndex++)
            {
                if (lessonIndex != 0)
                {
                    sb.AppendLine();
                }
                var lesson = lessons[lessonIndex];
                PrintLesson(lesson);
            }

            bool TryUniteLessonBasedOnEqualityOfAllButGroup()
            {
                if (lessons.Count == 1)
                {
                    return false;
                }
                for (int index = 0; index < lessons.Count - 1; index++)
                {
                    var l0 = lessons[index];
                    var l1 = lessons[index + 1];

                    var diffMask = new RegularLessonModelDiffMask
                    {
                        LessonType = true,
                        Course = true,
                        Room = true,
                    };
                    var diff = LessonBuilderHelper.Diff(l0, l1, diffMask);
                    if (diff.TheyDiffer)
                    {
                        return false;
                    }
                }

                bool metOdd = false;
                bool metEven = false;
                for (int i = 0; i < lessons.Count; i++)
                {
                    var lesson = lessons[i];
                    switch (lesson.Date.Parity)
                    {
                        case Parity.EvenWeek:
                        {
                            metEven = true;
                            break;
                        }
                        case Parity.OddWeek:
                        {
                            metOdd = true;
                            break;
                        }
                        case Parity.EveryWeek:
                        {
                            metEven = true;
                            metOdd = true;
                            break;
                        }
                        default:
                        {
                            Debug.Fail("??");
                            throw new InvalidOperationException("??");
                        }
                    }
                }
                Debug.Assert(!(metEven == false && metOdd == false));

                bool shouldPrintParity = metEven != metOdd;
                PrintLesson(
                    lessons[0],
                    printParity: shouldPrintParity,
                    printGroup: false);

                return true;
            }

            void PrintLesson(
                RegularLesson lesson,
                bool printParity = true,
                bool printGroup = true)
            {
                var listBuilder = new ListStringBuilder(sb);

                AppendCourse(listBuilder, lesson);
                AppendLessonTypeName(listBuilder, lesson);
                AppendRoom(listBuilder, lesson);

                if (printGroup)
                {
                    AppendGroup(listBuilder, lesson);
                }

                if (printParity)
                {
                    if (lesson.Date.Parity != Parity.EveryWeek)
                    {
                        var parityName = GetParityName(lesson);
                        listBuilder.Append($"({parityName})");
                    }
                }
            }

            bool TryUniteLessonsBasedOnParity()
            {
                if (lessons.Count != 2)
                {
                    return false;
                }

                var l0 = lessons[0];
                var l1 = lessons[1];
                var checkGroups = l0.Lesson.Groups.IsSingleGroup
                    && l0.Lesson.Groups.IsSingleGroup;
                var diff = LessonBuilderHelper.Diff(l0, l1, new()
                {
                    Course = true,
                    Parity = true,
                    LessonType = true,
                    OneGroup = checkGroups,
                    SubGroup = true,
                    Room = true,
                });
                if (!diff.Parity)
                {
                    return false;
                }
                if (diff.Course)
                {
                    return false;
                }

                var listBuilder = new ListStringBuilder(sb);
                AppendCourse(listBuilder, l0);

                if (!diff.LessonType)
                {
                    AppendLessonTypeName(listBuilder, l0);
                }
                if (!diff.Room)
                {
                    AppendRoom(listBuilder, l0);
                }
                if (!diff.OneGroup)
                {
                    AppendGroup(listBuilder, l0);
                }

                bool AllWillAppendSomething()
                {
                    for (int i = 0; i < lessons.Count; i++)
                    {
                        if (!WillAppendSomething())
                        {
                            return false;
                        }
                        bool WillAppendSomething()
                        {
                            var l = lessons[i];
                            if (diff.OneGroup && WillAppendGroup(l))
                            {
                                return true;
                            }
                            if (diff.LessonType && WillAppendLessonTypeName(l))
                            {
                                return true;
                            }
                            if (diff.Room && WillAppendRoom(l))
                            {
                                return true;
                            }
                            return false;
                        }
                    }
                    return true;
                }

                if (AllWillAppendSomething())
                {
                    for (int i = 0; i < lessons.Count; i++)
                    {
                        sb.AppendLine();

                        var lesson = lessons[i];
                        var parityName = GetParityName(lesson);
                        sb.Append($"{parityName}: ");

                        var commaList = new ListStringBuilder(sb, ',');
                        if (diff.LessonType)
                        {
                            AppendLessonTypeName(commaList, lesson);
                        }
                        if (diff.OneGroup)
                        {
                            AppendGroup(commaList, lesson);
                        }
                        if (diff.Room)
                        {
                            AppendRoom(commaList, lesson);
                        }
                    }
                }

                return true;
            }

            void AppendCourse(ListStringBuilder b, RegularLesson lesson)
            {
                var course = p.Schedule.Get(lesson.Lesson.Course);
                b.Append(course.Names[^1]);
            }
            bool WillAppendLessonTypeName(RegularLesson lesson)
            {
                return p.LessonTypeDisplay.Get(lesson.Lesson.Type) is not null;
            }
            void AppendLessonTypeName(ListStringBuilder b, RegularLesson lesson)
            {
                if (p.LessonTypeDisplay.Get(lesson.Lesson.Type) is { } lessonTypeName)
                {
                    b.Append($"({lessonTypeName})");
                }
            }
            bool WillAppendRoom(RegularLesson lesson)
            {
                return lesson.Lesson.Room != RoomId.Invalid;
            }
            void AppendRoom(ListStringBuilder b, RegularLesson lesson)
            {
                if (lesson.Lesson.Room != RoomId.Invalid)
                {
                    b.Append($"{lesson.Lesson.Room.Id}");
                }
            }
            bool WillAppendGroup(RegularLesson lesson)
            {
                var groups = lesson.Lesson.Groups;
                return groups.IsSingleGroup;
            }
            void AppendGroup(ListStringBuilder b, RegularLesson lesson)
            {
                var groups = lesson.Lesson.Groups;
                if (!groups.IsSingleGroup)
                {
                    return;
                }

                // b.MaybeAppendSeparator();

                var group = p.Schedule.Get(groups.Group0);
                // LessonTextDisplayHelper.AppendGroupNameWithLanguage(b.StringBuilder, group);
                b.Append(group.Name);

                if (lesson.Lesson.SubGroup != SubGroup.All)
                {
                    b.StringBuilder.Append($"-{lesson.Lesson.SubGroup.Value}");
                }
            }
            string GetParityName(RegularLesson l)
            {
                var parityName = p.ParityDisplay.Get(l.Date.Parity);
                return parityName!;
            }
        }
    }

    private static void SetSharedStringValue(this Cell cell, SharedStringItemId stringId)
    {
        cell.DataType = CellValues.SharedString;
        cell.CellValue = new(stringId.Value);
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

    public static void SetFont(this CellFormat format, FontId fontId)
    {
        format.FontId = new(fontId.Value);
    }

    public static void SetFill(this CellFormat format, FillId fillId)
    {
        format.FillId = new(fillId.Value);
    }

    public static void SetBorder(this CellFormat format, BorderId borderId)
    {
        format.BorderId = new(borderId.Value);
    }

    public static void SetStyle(this Cell cell, CellFormatId formatId)
    {
        cell.StyleIndex = new(formatId.Value);
    }

    public readonly struct StylesheetBuilder : IDisposable
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

        public FontId Font(Action<Font> configure)
        {
            var (id, font) = _fonts.Next();
            configure(font);
            return new(id);
        }

        public FillId Fill(Action<Fill> configure)
        {
            var (id, fill) = _fills.Next();
            configure(fill);
            return new(id);
        }

        public BorderId Border(Action<Border> configure)
        {
            var (id, border) = _borders.Next();
            configure(border);
            return new(id);
        }

        public CellFormatId CellFormat(Action<CellFormat> configure)
        {
            var (id, cellFormat) = _cellFormats.Next();
            configure(cellFormat);

            if (cellFormat.Alignment != null && cellFormat.ApplyAlignment == null)
            {
                cellFormat.ApplyAlignment = true;
            }
            if (cellFormat.FontId != null || cellFormat.ApplyFont == null)
            {
                cellFormat.ApplyFont = true;
            }
            if (cellFormat.FillId != null && cellFormat.ApplyFill == null)
            {
                cellFormat.ApplyFill = true;
            }
            if (cellFormat.BorderId != null && cellFormat.ApplyBorder == null)
            {
                cellFormat.ApplyBorder = true;
            }

            return new(id);
        }

        public void Dispose()
        {
            Save();
        }

        public void Save()
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

        public static StylesheetBuilder CreateWithDefaults(WorkbookPart workbookPart)
        {
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();

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

    public record struct SharedStringItemId(int Value);

    public struct StringTableBuilder(SharedStringTable table)
    {
        private readonly Dictionary<string, SharedStringItemId> _strings = new();
        private readonly SharedStringTable _table = table;
        private int otherCount;

        public static StringTableBuilder Create(WorkbookPart workbookPart)
        {
            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sharedStringTable = new SharedStringTable();
            sharedStringPart.SharedStringTable = sharedStringTable;
            return new(sharedStringTable);
        }

        public SharedStringItemId NextId => new(_strings.Count + otherCount);

        private void NewString(string value)
        {
            var text = new Text(value);
            var item = new SharedStringItem(text);
            _table.AppendChild(item);
        }

        public SharedStringItemId GetOrAddString(string s)
        {
            var id = NextId;
            ref var ret = ref CollectionsMarshal.GetValueRefOrAddDefault(_strings, s, out var exists);
            if (!exists)
            {
                NewString(s);
                ret = id;
            }
            return ret;
        }

        public SharedStringItemId AddString(string s)
        {
            var id = NextId;
            _strings.Add(s, id);
            NewString(s);
            return id;
        }

        public SharedStringItemId AddOther(SharedStringItem item)
        {
            var id = NextId;
            otherCount++;
            _table.AddChild(item);
            return id;
        }
    }

    private static void CenterAndWrap(this CellFormat style)
    {
        style.Alignment = new()
        {
            Horizontal = HorizontalAlignmentValues.Center,
            Vertical = VerticalAlignmentValues.Center,
            WrapText = true,
        };
    }

    private static void AllSides(this Border border, BorderStyleValues style)
    {
        border.LeftBorder = new()
        {
            Style = style,
        };
        border.RightBorder = new()
        {
            Style = style,
        };
        border.TopBorder = new()
        {
            Style = style,
        };
        border.BottomBorder = new()
        {
            Style = style,
        };
    }

    private readonly struct OddOrEvenStyleId
    {
        public readonly uint Odd;
        public readonly uint Even;

        public OddOrEvenStyleId(uint odd, uint even)
        {
            Odd = odd;
            Even = even;
        }

        public delegate void ConfigureDelegate(CellFormat x, bool isOdd);

        public static OddOrEvenStyleId Create(
            StylesheetBuilder builder,
            ConfigureDelegate configure)
        {
            var odd = builder.CellFormat(x =>
            {
                configure(x, isOdd: true);
            });
            var even = builder.CellFormat(x =>
            {
                configure(x, isOdd: false);
            });
            return new(odd.Value, even.Value);
        }

        public CellFormatId Get(bool isOdd) => new(isOdd ? Odd : Even);
    }

    // First half are for odd = false
    // second for odd = true.
    // Then it's top, bottom, or neither
    public enum EdgeOption
    {
        Even_Top = 0,
        Even_Bottom = 1,
        Even_Neither = 2,
        Odd_Top = 3,
        Odd_Bottom = 4,
        Odd_Neither = 5,
        Count,
    }

    public static bool IsOdd(this EdgeOption opt) => opt >= EdgeOption.Odd_Top;
    public static Edgeness GetEdgeness(this EdgeOption opt) => (Edgeness) ((int) opt % 3);

    public enum Edgeness
    {
        Top,
        Bottom,
        Neither,
        Count,
    }

    private static EdgeOption EdgeOptionFromIndex(
        int oddnessIndex,
        int edgenessIndex,
        int height)
    {
        bool odd = oddnessIndex % 2 == 1;
        bool top = edgenessIndex == 0;
        bool bottom = edgenessIndex == height - 1;
        bool neither = !top && !bottom;

        int val = 0;
        if (neither)
        {
            val += (int) Edgeness.Neither;
        }
        else if (bottom)
        {
            val += (int) Edgeness.Bottom;
        }

        if (odd)
        {
            val += (int) Edgeness.Count;
        }

        return (EdgeOption) val;
    }

    private readonly struct EdgesAndOddnessStyleIds
    {
        private readonly uint _first;

        public EdgesAndOddnessStyleIds(uint first) => _first = first;
        public readonly CellFormatId Get(EdgeOption option) => new(_first + (uint) option);

        public static EdgesAndOddnessStyleIds Create(
            StylesheetBuilder builder,
            Action<CellFormat, EdgeOption> configure)
        {
            var first = builder.CellFormat(x =>
            {
                configure(x, (EdgeOption) 0);
            });
            for (uint i = 1; i < (uint) EdgeOption.Count; i++)
            {
                builder.CellFormat(x =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    configure(x, (EdgeOption) i);
                });
            }
            return new(first.Value);
        }
    }

    private readonly struct EdgesStyleIds
    {
        private readonly uint _first;
        public EdgesStyleIds(uint first) => _first = first;
        public readonly CellFormatId Get(Edgeness edge) => new(_first + (uint) edge);

        public static EdgesStyleIds Create(
            StylesheetBuilder builder,
            Action<CellFormat, Edgeness> configure)
        {
            var first = builder.CellFormat(x =>
            {
                configure(x, (Edgeness) 0);
            });
            for (uint i = 1; i < (int) Edgeness.Count; i++)
            {
                builder.CellFormat(x =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    configure(x, (Edgeness) i);
                });
            }
            return new(first.Value);
        }
    }

    private readonly struct EdgesBorderIds
    {
        private readonly uint _first;
        public EdgesBorderIds(uint first) => _first = first;
        public readonly BorderId Get(Edgeness edge) => new(_first + (uint) edge);

        public static EdgesBorderIds Create(
            StylesheetBuilder builder,
            Action<Border, Edgeness> configure)
        {
            var first = builder.Border(x =>
            {
                configure(x, (Edgeness) 0);
            });
            for (uint i = 1; i < (int) Edgeness.Count; i++)
            {
                builder.Border(x =>
                {
                    // ReSharper disable once AccessToModifiedClosure
                    configure(x, (Edgeness) i);
                });
            }
            return new(first.Value);
        }
    }

    private struct CellsBuilder
    {
        private readonly SheetData _sheetData;
        private Row? _row;

        public CellsBuilder(SheetData sheetData)
        {
            _sheetData = sheetData;
            _row = null;
        }

        public Row NextRow()
        {
            _row = new Row();
            _sheetData.AppendChild(_row);
            return _row;
        }

        public Cell NextCell()
        {
            var cell = new Cell();
            Debug.Assert(_row != null);
            _row.AppendChild(cell);
            return cell;
        }
    }

    private readonly struct StyleIds
    {
        public required OddOrEvenStyleId Day { get; init; }
        public required CellFormatId HeaderTitle { get; init; }
        public required CellFormatId Teacher { get; init; }
        public required EdgesAndOddnessStyleIds Lesson { get; init; }
        public required EdgesStyleIds Seminar { get; init; }
        public required EdgesAndOddnessStyleIds TimeSlot { get; init; }
    }

    private static StyleIds ConfigureStylesheet(WorkbookPart workbookPart)
    {
        using var stylesheet = StylesheetBuilder.CreateWithDefaults(workbookPart);

        void DefaultFont(Font x)
        {
            x.FontName = new()
            {
                Val = "Helvetica Neue",
            };
        }

        var cellFontId = stylesheet.Font(x =>
        {
            DefaultFont(x);
            x.FontSize = new()
            {
                Val = 9,
            };
        });
        var teacherFontId = stylesheet.Font(x =>
        {
            DefaultFont(x);
            x.FontSize = new()
            {
                Val = 9,
            };
            x.Bold = new()
            {
                Val = true,
            };
        });
        var timeSlotFontId = stylesheet.Font(x =>
        {
            DefaultFont(x);
            x.FontSize = new()
            {
                Val = 10,
            };
            x.Bold = new()
            {
                Val = true,
            };
        });
        var dayFontId = stylesheet.Font(x =>
        {
            DefaultFont(x);
            x.FontSize = new()
            {
                Val = 11,
            };
            x.Bold = new()
            {
                Val = true,
            };
        });
        const int autoColor = 64;

        {
            // The style with this id is some dotted grid pattern.
            // ???
            var wtf = stylesheet.Fill(_ => {});
            _ = wtf;
        }

        var greenFillId = stylesheet.Fill(x =>
        {
            x.PatternFill = new()
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new()
                {
                    Rgb = "92D050",
                },
                BackgroundColor = new()
                {
                    Indexed = autoColor,
                },
            };
        });
        var oddFillId = stylesheet.Fill(x =>
        {
            x.PatternFill = new()
            {
                PatternType = PatternValues.Solid,
                ForegroundColor = new()
                {
                    Rgb = "EFEFEF",
                },
                BackgroundColor = new()
                {
                    Indexed = autoColor,
                },
            };
        });

        static BorderStyleValues Thick() => BorderStyleValues.Medium;
        static BorderStyleValues Thin() => BorderStyleValues.Thin;
        var thickBordersId = stylesheet.Border(x =>
        {
            x.AllSides(Thick());
        });
        var cellBorderIds = EdgesBorderIds.Create(stylesheet, (x, edge) =>
        {
            var topStyle = edge == Edgeness.Top ? Thick() : Thin();
            var bottomStyle = edge == Edgeness.Bottom ? Thick() : Thin();
            x.TopBorder = new()
            {
                Style = topStyle,
            };
            x.BottomBorder = new()
            {
                Style = bottomStyle,
            };
            x.LeftBorder = new()
            {
                Style = Thick(),
            };
            x.RightBorder = new()
            {
                Style = Thick(),
            };
        });
        BorderId BorderIdByEdge(Edgeness edge)
        {
            return cellBorderIds.Get(edge);
        }

        var dayStyleIds = OddOrEvenStyleId.Create(stylesheet, (x, isOdd) =>
        {
            x.Alignment = new()
            {
                TextRotation = 90,
                Horizontal = HorizontalAlignmentValues.Center,
                Vertical = VerticalAlignmentValues.Center,
            };
            x.SetFont(dayFontId);
            x.SetBorder(thickBordersId);
            if (isOdd)
            {
                x.SetFill(oddFillId);
            }
        });
        var teacherStyleId = stylesheet.CellFormat(x =>
        {
            x.CenterAndWrap();
            x.SetFont(teacherFontId);
            x.SetBorder(thickBordersId);
        });
        var lessonStyleIds = EdgesAndOddnessStyleIds.Create(stylesheet, (x, edge) =>
        {
            x.CenterAndWrap();
            x.SetFont(cellFontId);
            x.SetBorder(BorderIdByEdge(edge.GetEdgeness()));
            if (edge.IsOdd())
            {
                x.SetFill(oddFillId);
            }
        });
        var seminarStyleIds = EdgesStyleIds.Create(stylesheet, (x, edge) =>
        {
            x.CenterAndWrap();
            x.SetFont(cellFontId);
            x.SetFill(greenFillId);
            x.SetBorder(BorderIdByEdge(edge));
        });
        var timeSlotStyleIds = EdgesAndOddnessStyleIds.Create(stylesheet, (x, edge) =>
        {
            x.CenterAndWrap();
            x.SetFont(timeSlotFontId);
            x.SetBorder(BorderIdByEdge(edge.GetEdgeness()));
            if (edge.IsOdd())
            {
                x.SetFill(oddFillId);
            }
        });
        var headerTitleStyleId = stylesheet.CellFormat(x =>
        {
            x.Alignment = new()
            {
                Vertical = VerticalAlignmentValues.Center,
                Horizontal = HorizontalAlignmentValues.Left,
                WrapText = true,
            };
            x.SetFont(teacherFontId);
            x.SetBorder(thickBordersId);
        });

        return new()
        {
            Day = dayStyleIds,
            Teacher = teacherStyleId,
            HeaderTitle = headerTitleStyleId,
            Lesson = lessonStyleIds,
            Seminar = seminarStyleIds,
            TimeSlot = timeSlotStyleIds,
        };
    }

    private static Spaces Spaces(int count) => new(count);
}

public readonly record struct FontId(uint Value);
public readonly record struct FillId(uint Value);
public readonly record struct BorderId(uint Value);
public readonly record struct CellFormatId(uint Value);


public readonly struct Spaces : ISpanFormattable
{
    private readonly int _count;
    public Spaces(int count) => _count = count;

    public string ToString(string? format, IFormatProvider? formatProvider)
    {
        _ = format;
        _ = formatProvider;
        return new string(NonBreakingSpace, _count);
    }

    private const char NonBreakingSpace = '\u00A0';

    public bool TryFormat(
        Span<char> destination,
        out int charsWritten,
        ReadOnlySpan<char> format,
        IFormatProvider? provider)
    {
        _ = provider;
        _ = format;

        if (destination.Length < _count)
        {
            charsWritten = 0;
            return false;
        }
        charsWritten = _count;
        for (int i = 0; i < _count; i++)
        {
            destination[i] = NonBreakingSpace;
        }
        return true;
    }
}
