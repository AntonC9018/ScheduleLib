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

        var thickBordersId = stylesheet.Border(x =>
        {
            x.AllSides(BorderStyleValues.Thick);
        });
        var mediumBorderThickSidesId = stylesheet.Border(x =>
        {
            x.TopBorder = new()
            {
                Style = BorderStyleValues.Medium,
            };
            x.BottomBorder = new()
            {
                Style = BorderStyleValues.Medium,
            };
            x.LeftBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
            x.RightBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
        });
        var mediumBorderThickSidesAndTop = stylesheet.Border(x =>
        {
            x.TopBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
            x.BottomBorder = new()
            {
                Style = BorderStyleValues.Medium,
            };
            x.LeftBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
            x.RightBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
        });
        var mediumBorderThickSidesAndBottom = stylesheet.Border(x =>
        {
            x.TopBorder = new()
            {
                Style = BorderStyleValues.Medium,
            };
            x.BottomBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
            x.LeftBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
            x.RightBorder = new()
            {
                Style = BorderStyleValues.Thick,
            };
        });
        BorderId BorderIdByEdge(Edgeness edge)
        {
            return edge switch
            {
                Edgeness.Top => mediumBorderThickSidesAndTop,
                Edgeness.Bottom => mediumBorderThickSidesAndBottom,
                Edgeness.Neither => mediumBorderThickSidesId,
                _ => throw new ArgumentOutOfRangeException(nameof(edge)),
            };
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

        using var strings = StringTableBuilder.Create(workbookPart);
        var cells = new CellsBuilder(sheetData);

        TopHeader();
        Body();

        return;

        // top header
        void TopHeader()
        {
            _ = cells.NextRow();

            _ = cells.NextCell();

            {
                var cell = cells.NextCell();
                cell.SetStringValue("         Profesor\n  Ora");
                cell.SetStyle(teacherStyleId);
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
                cell.SetStyle(teacherStyleId);
            }
        }

        void Body()
        {
            var mappingByCell = MappingsCreationHelper.CreateCellMappings(
                p.Schedule.RegularLessons,
                l => l.Lesson.Teachers);
            int timeSlotCount = p.TimeConfig.TimeSlotCount;

            var seminarStringId = strings.AddString("Seminarul DI");
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

                    _ = cells.NextRow();

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
                        cell.SetStyle(dayStyleIds.Get(option.IsOdd()));
                    }

                    {
                        var id = firstTimeSlotStringId + timeSlotIndex;
                        var cell = cells.NextCell();
                        cell.SetSharedStringValue(id);
                        cell.SetStyle(timeSlotStyleIds.Get(option));
                    }

                    bool isSeminarDate = day == p.SeminarDate.Day && timeSlot == p.SeminarDate.TimeSlot;

                    for (int teacherId = 0; teacherId < p.Schedule.Teachers.Length; teacherId++)
                    {
                        var cell = cells.NextCell();

                        if (isSeminarDate)
                        {
                            cell.SetSharedStringValue(seminarStringId);

                            var styleId = seminarStyleIds.Get(option.GetEdgeness());
                            cell.SetStyle(styleId);

                            continue;
                        }

                        cell.SetStyle(lessonStyleIds.Get(option));

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
                            var stringId = strings.GetOrAddString(sb.ToStringAndClear());
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
                    var ret = strings.AddString(timeSlotString);
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

    public readonly struct StringTableBuilder(SharedStringTable table) : IDisposable
    {
        private readonly Dictionary<string, int> _strings = new();
        private readonly SharedStringTable _table = table;

        public static StringTableBuilder Create(WorkbookPart workbookPart)
        {
            var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
            var sharedStringTable = new SharedStringTable();
            sharedStringPart.SharedStringTable = sharedStringTable;
            return new(sharedStringTable);
        }

        public int GetOrAddString(string s)
        {
            ref var ret = ref CollectionsMarshal.GetValueRefOrAddDefault(_strings, s, out var exists);
            if (!exists)
            {
                ret = _strings.Count - 1;
            }
            return ret;
        }

        public int AddString(string s)
        {
            var index = _strings.Count;
            _strings.Add(s, index);
            return index;
        }

        public void Dispose()
        {
            foreach (var s in _strings.OrderBy(x => x.Value))
            {
                var text = new Text(s.Key);
                var item = new SharedStringItem(text);
                _table.AppendChild(item);
            }
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
}

public readonly record struct FontId(uint Value);
public readonly record struct FillId(uint Value);
public readonly record struct BorderId(uint Value);
public readonly record struct CellFormatId(uint Value);

