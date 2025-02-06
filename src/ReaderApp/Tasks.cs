using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Web;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Io;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Configuration;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using ReaderApp.ExcelBuilder;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using Column = DocumentFormat.OpenXml.Spreadsheet.Column;
using Columns = DocumentFormat.OpenXml.Spreadsheet.Columns;
using Font = DocumentFormat.OpenXml.Spreadsheet.Font;
using Group = ScheduleLib.Group;
using HorizontalAlignmentValues = DocumentFormat.OpenXml.Spreadsheet.HorizontalAlignmentValues;
using IDocument = AngleSharp.Dom.IDocument;
using NotSupportedException = System.NotSupportedException;
using Table = DocumentFormat.OpenXml.Spreadsheet.Table;
using VerticalAlignmentValues = DocumentFormat.OpenXml.Spreadsheet.VerticalAlignmentValues;

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
                if (!teacherName.ShortFirstName.Span.Value.IsEmpty)
                {
                    sb.Append(teacherName.ShortFirstName.Span.Shortened.Value);
                    sb.Append('_');
                }
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

    public static void GenerateAllTeacherExcel(AllTeacherExcelParams p)
    {
        using var stream = File.Open(p.OutputFilePath, FileMode.Create, FileAccess.ReadWrite);
        using var excel = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true);

        var workbookPart = excel.AddWorkbookPart();
        var workbook = new Workbook();
        var sheetData = new SheetData();
        var worksheet = new Worksheet(sheetData);
        var strings = StringTableBuilder.Create(workbookPart);
        var styles = ConfigureStylesheet(workbookPart);
        var cells = new CellsBuilder(sheetData);

        InitExcelBasics();

        ConfigureFrozenViews();
        ConfigureWidths();
        ConfigureMerges();

        TopHeader();
        Body();

        return;

        void InitExcelBasics()
        {
            workbookPart.Workbook = workbook;

            var worksheetPart = workbookPart.AddNewPart<WorksheetPart>();
            worksheetPart.Worksheet = worksheet;

            var sheets = workbook.AppendChild(new Sheets());
            var sheet = new Sheet
            {
                Id = workbookPart.GetIdOfPart(worksheetPart),
                SheetId = 1,
                Name = "main",
            };
            sheets.AppendChild(sheet);
        }

        void ConfigureFrozenViews()
        {
            var sheetViews = new SheetViews();
            worksheet.InsertAt(sheetViews, 0);

            var sheetView = new SheetView
            {
                WorkbookViewId = 0,
            };
            sheetViews.AppendChild(sheetView);

            var pane = new Pane
            {
                VerticalSplit = 1,
                HorizontalSplit = 2,
                TopLeftCell = ExcelHelper.GetCellReference(new()
                {
                    Position = new(ColIndex: 2, RowIndex: 1),
                    StringBuilder = p.StringBuilder,
                }),
                ActivePane = PaneValues.BottomRight,
                State = PaneStateValues.Frozen,
            };
            sheetView.AppendChild(pane);
        }

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
                Width = FromPixels(90),
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
                merge.Reference = ExcelHelper.GetCellRange(new()
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
                cell.SetStringValue($"{new Spaces(6)}Profesor\n{new Spaces(3)}Ora");
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

                    var option = UsefulStylesheetEnumsHelper.OddEdgeFromIndex(
                        oddnessIndex: dayIndex,
                        edgenessIndex: timeSlotIndex,
                        height: timeSlotCount);

                    {
                        var cell = cells.NextCell();
                        if (timeSlotIndex == 0)
                        {
                            var dayName = p.DayNameProvider.GetDayName(day);
                            var caps = dayName.ToUpper(CultureInfo.CurrentCulture);
                            cell.SetStringValue(caps);
                        }
                        var odd = option.GetOddness();
                        cell.SetStyle(styles.Day.Get(odd));
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

                            var styleId = styles.Seminar.Get(option.GetEdge());
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

                            var str = sb.ToStringAndClear();
                            var stringId = strings.GetOrAddString(str);
                            cell.SetSharedStringValue(stringId);
                        }
                    }
                }
            }
        }

        SharedStringItemId AddTimeSlotStrings()
        {
            var firstId = Add(0);
            var timeSlotCount = p.TimeConfig.TimeSlotCount;
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
                    AppendGroup(
                        listBuilder,
                        lesson,
                        appendSubgroup: true);
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
                    bool subgroupsDiffer = diff.SubGroup;
                    AppendGroup(listBuilder, l0, appendSubgroup: !subgroupsDiffer);
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
                            AppendGroup(
                                commaList,
                                lesson,
                                appendSubgroup: !diff.SubGroup);
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
            void AppendGroup(
                ListStringBuilder b,
                RegularLesson lesson,
                bool appendSubgroup)
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

                if (appendSubgroup
                    && lesson.Lesson.SubGroup != SubGroup.All)
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

    private readonly struct StyleIds
    {
        public required StyleIds<Oddness, CellFormatId> Day { get; init; }
        public required CellFormatId HeaderTitle { get; init; }
        public required CellFormatId Teacher { get; init; }
        public required StyleIds<OddEdge, CellFormatId> Lesson { get; init; }
        public required StyleIds<Edge, CellFormatId> Seminar { get; init; }
        public required StyleIds<OddEdge, CellFormatId> TimeSlot { get; init; }
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
        var cellBorderIds = stylesheet.Borders<Edge>((x, edge) =>
        {
            var topStyle = edge == Edge.Top ? Thick() : Thin();
            var bottomStyle = edge == Edge.Bottom ? Thick() : Thin();
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
        BorderId BorderIdByEdge(Edge edge)
        {
            return cellBorderIds.Get(edge);
        }

        var dayStyleIds = stylesheet.CellFormats<Oddness>((x, odd) =>
        {
            x.Alignment = new()
            {
                TextRotation = 90,
                Horizontal = HorizontalAlignmentValues.Center,
                Vertical = VerticalAlignmentValues.Center,
            };
            x.SetFont(dayFontId);
            x.SetBorder(thickBordersId);
            if (odd == Oddness.Odd)
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
        var lessonStyleIds = stylesheet.CellFormats<OddEdge>((x, edge) =>
        {
            x.CenterAndWrap();
            x.SetFont(cellFontId);
            x.SetBorder(BorderIdByEdge(edge.GetEdge()));
            if (edge.IsOdd())
            {
                x.SetFill(oddFillId);
            }
        });
        var seminarStyleIds = stylesheet.CellFormats<Edge>((x, edge) =>
        {
            x.CenterAndWrap();
            x.SetFont(cellFontId);
            x.SetFill(greenFillId);
            x.SetBorder(BorderIdByEdge(edge));
        });
        var timeSlotStyleIds = stylesheet.CellFormats<OddEdge>((x, edge) =>
        {
            x.CenterAndWrap();
            x.SetFont(timeSlotFontId);
            x.SetBorder(BorderIdByEdge(edge.GetEdge()));
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

    public struct AddLessonsToOnlineRegistryParams()
    {
        public required CancellationToken CancellationToken;
        /// <summary>
        /// Will be initialized from the default source if not provided.
        /// </summary>
        public Credentials? Credentials = null;
        /// <summary>
        /// Will be initialized to the default config if not provided.
        /// </summary>
        public JsonSerializerOptions? JsonOptions;
        /// <summary>
        /// Will be initialized to the default values if not provided.
        /// </summary>
        public NamesConfig Names = default;

        public required Session Session;
        public required Schedule Schedule;
        public required ILogger Logger;
        public required CourseFinder CourseFinder;
        public required GroupParseContext GroupParseContext;
        public required LookupModule LookupModule;
        public required IAllScheduledDateProvider DateProvider;
        public required LessonTimeConfig TimeConfig;
    }

    public sealed class CourseFinder
    {
        public required CourseNameUnifierModule Impl { get; init; }
        public required LookupModule LookupModule { get; init; }

        public CourseId? Find(string name)
        {
            var ret = Impl.Find(new()
            {
                Lookup = LookupModule,
                CourseName = name,
                ParseOptions = new()
                {
                    IgnorePunctuation = true,
                },
            });
            return ret;
        }
    }

    public interface ILogger
    {
        void CourseNotFound(string courseName);
        void GroupNotFound(string groupName);
        void LessonWithoutName();

        // May want to pull this out.
        void CustomLessonType(ReadOnlySpan<char> ch);
    }

    public struct NamesConfigSource()
    {
        public string TokensFile = "tokens.json";
        public string TokenCookieName = "ForDecanat";
        public string RegistryBaseUrl = "http://crd.usm.md/studregistry/";
        public string RegistryLoginPath = "Account/Login";
        public string LessonsPath = "LessonAttendance";

        public readonly NamesConfig Build()
        {
            var reg = new Uri(RegistryBaseUrl);
            var login = new Uri(reg, RegistryLoginPath);
            var lessons = new Uri(reg, LessonsPath);
            return new()
            {
                TokensFile = TokensFile,
                TokenCookieName = TokenCookieName,
                LoginUrl = login,
                LessonsUrl = lessons,
                BaseUrl = reg,
            };
        }
    }

    public struct NamesConfig
    {
        public static readonly NamesConfig Default = new NamesConfigSource().Build();

        public required string TokensFile;
        public required string TokenCookieName;
        public required Uri BaseUrl;
        public required Uri LoginUrl;
        public required Uri LessonsUrl;

        public readonly bool IsInitialized => LoginUrl != null;
    }

    public static JsonSerializerOptions DefaultJsonOptions = new()
    {
        IndentSize = 4,
        WriteIndented = true,
    };

    public static async Task AddLessonsToOnlineRegistry(AddLessonsToOnlineRegistryParams p)
    {
        p.Credentials ??= GetCredentials();
        if (!p.Names.IsInitialized)
        {
            p.Names = NamesConfig.Default;
        }

        var cookieProvider = new MemoryCookieProvider();
        var cookieContainer = cookieProvider.Container;

        using var handler = new HttpClientHandler();
        handler.CookieContainer = cookieContainer;
        handler.UseCookies = true;
        handler.AllowAutoRedirect = false;

        using var httpClient = new HttpClient(handler);

        var config = Configuration.Default;
        config = config.WithDefaultLoader();
        config = config.With<ICookieProvider>(_ => cookieProvider);

        using var browsingContext = BrowsingContext.New(config);

        await InitializeToken();

        var courseLinks = await QueryCourseLinks();
        foreach (var courseLink in courseLinks)
        {
            var groupsUrl = courseLink.Url;
            var groups = await QueryGroupLinksOfCourse(groupsUrl);
            foreach (var group in groups)
            {
                var lessons = MatchLessons(new()
                {
                    Lookup = p.LookupModule.LessonsByCourse,
                    Schedule = p.Schedule,
                    CourseId = courseLink.CourseId,
                    GroupId = group.GroupId,
                    SubGroup = group.SubGroup,
                });

                var existingLessonInstances = await QueryLessonInstances(group.Uri);
                var orderedLessonInstances = existingLessonInstances
                    .OrderBy(x => x.DateTime)
                    .ToArray();
                _ = orderedLessonInstances;

                // Figure out the exact dates that the lessons will occur.
                var times = GetDateTimesOfScheduledLessons(new()
                {
                    Lessons = lessons,
                    Schedule = p.Schedule,
                    DateProvider = p.DateProvider,
                    TimeConfig = p.TimeConfig,
                }).OrderBy(x => x.DateTime);
            }
        }

        return;

        async Task<IEnumerable<LessonInstanceLink>> QueryLessonInstances(Uri groupUri)
        {
            var doc = await GetHtml(groupUri);
            return Ret();

            IEnumerable<LessonInstanceLink> Ret()
            {
                const string rowPath = """main > div:nth-of-type(3) > table > tbody > tr""";
                var rows = doc.QuerySelectorAll(rowPath).Skip(1);
                foreach (var row in rows)
                {
                    var cells = row.Children;
                    // NOTE: these are going to throw an invalid cast if anything is weird with the nodes.
                    var first = ProcessFirst();
                    var editUri = ProcessEdit();
                    yield return new()
                    {
                        EditUri = editUri,
                        DateTime = first.DateTime,
                        LessonType = first.LessonType,
                    };
                    continue;

                    (LessonType LessonType, DateTime DateTime) ProcessFirst()
                    {
                        var dateTimeAndTypeCell = (IHtmlTableDataCellElement) cells[0];
                        var children = dateTimeAndTypeCell.ChildNodes;

                        LessonType lessonType;
                        {
                            var typeNode = children[^1];
                            var typeText = typeNode.TextContent;
                            lessonType = ParseLessonType(typeText, p.Logger);
                        }

                        DateTime dateTime;
                        {
                            var anchor = children.OfType<IHtmlAnchorElement>().First();
                            var dateTimeText = anchor.Text;
                            var span = dateTimeText.AsSpan();
                            const string format = "dd.MM.yyyy HH.mm";
                            bool success = DateTime.TryParseExact(
                                format: format,
                                s: span,
                                provider: null,
                                style: DateTimeStyles.AssumeLocal,
                                result: out dateTime);
                            if (!success)
                            {
                                throw new NotSupportedException("The date time didn't parse properly");
                            }
                        }

                        return (lessonType, dateTime);
                    }
                    Uri ProcessEdit()
                    {
                        var editCell = (IHtmlTableDataCellElement) cells[2];
                        var editAnchor = (IHtmlAnchorElement) editCell.Children[0];
                        var ret = new Uri(editAnchor.Href);
                        return ret;
                    }
                }
            }
        }

        async Task<IEnumerable<GroupLink>> QueryGroupLinksOfCourse(Uri courseUrl)
        {
            var doc = await GetHtml(courseUrl);
            // here, the format is different
            // DJ2302ru(II)
            // DJ2301
            // IA2401fr
            return Ret();

            IEnumerable<GroupLink> Ret()
            {
                const string path = """form[name="lesson"] > div.row:nth-of-type(2) > div.col:nth-of-type(1) > div.row > a:nth-of-type(1)""";
                var anchors = doc.QuerySelectorAll(path);
                foreach (var el in anchors)
                {
                    var anchor = (IHtmlAnchorElement) el;
                    var url = anchor.Href;
                    var groupName = anchor.Text;
                    var groupForSearch = ParseGroupFromOnlineRegistry(p.GroupParseContext, groupName);
                    var groupId = FindGroupMatch(p.Schedule, groupForSearch);
                    if (groupId == GroupId.Invalid)
                    {
                        p.Logger.GroupNotFound(groupName);
                        continue;
                    }

                    var uri = new Uri(url);
                    var subgroup = new SubGroup(groupForSearch.SubGroupName.ToString());
                    yield return new()
                    {
                        Uri = uri,
                        GroupId = groupId,
                        SubGroup = subgroup,
                    };
                }
            }
        }

        async Task<IEnumerable<CourseLink>> QueryCourseLinks()
        {
            var doc = await GetHtml(p.Names.LessonsUrl);
            var ret = Ret();
            return ret;

            IEnumerable<CourseLink> Ret()
            {
                var anchors = doc.QuerySelectorAll($"#nav-{SemString()} > div > span:nth-of-type(2) > a");
                foreach (var el in anchors)
                {
                    var anchor = (IHtmlAnchorElement) el;
                    var url = anchor.Href;
                    var courseName = anchor.Text;
                    if (courseName.Length == 0)
                    {
                        p.Logger.LessonWithoutName();
                        continue;
                    }
                    if (p.CourseFinder.Find(courseName) is not { } courseId)
                    {
                        p.Logger.CourseNotFound(courseName);
                        continue;
                    }
                    yield return new(courseId, new(url));
                }
            }

            string SemString()
            {
                return p.Session switch
                {
                    Session.Ses1 => "1",
                    Session.Ses2 => "2",
                    _ => throw new InvalidOperationException("??"),
                };
            }
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task<IDocument> GetHtml(Uri uri)
        {
            bool failedOnce = false;
            while (true)
            {
                using var req = await httpClient.GetAsync(uri, cancellationToken: p.CancellationToken);
                // if invalid token
                if (req.StatusCode
                    is HttpStatusCode.Unauthorized
                    or HttpStatusCode.Redirect)
                {
                    if (failedOnce)
                    {
                        throw new InvalidOperationException("Failed to use the password to log in once.");
                    }

                    await QueryTokenAndSave();
                    failedOnce = true;
                    continue;
                }
                await using var res = await req.Content.ReadAsStreamAsync(p.CancellationToken);
                var document = await browsingContext.OpenAsync(r => r.Content(res).Address(uri));
                return document;
            }
        }

        async Task InitializeToken()
        {
            if (await MaybeSetCookieFromFile())
            {
                return;
            }
            await QueryTokenAndSave();
        }

        async Task<bool> MaybeSetCookieFromFile()
        {
            var token = await LoadToken();
            if (token is null)
            {
                return false;
            }
            if (token.Expired)
            {
                return false;
            }
            cookieContainer.Add(p.Names.BaseUrl, token);
            return true;
        }

        async ValueTask<Cookie?> LoadToken()
        {
            if (!File.Exists(p.Names.TokensFile))
            {
                return null;
            }

            await using var stream = File.OpenRead(p.Names.TokensFile);
            if (stream.Length == 0)
            {
                return null;
            }

            JsonDocument cookies;
            try
            {
                cookies = await JsonDocument.ParseAsync(
                    stream,
                    cancellationToken: p.CancellationToken);
                if (cookies == null)
                {
                    return null;
                }
            }
            catch (JsonException)
            {
                stream.Close();
                File.Delete(p.Names.TokensFile);
                return null;
            }

            using var cookies_ = cookies;

            var root = cookies.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }
            if (!root.TryGetProperty(p.Credentials.Login, out var token))
            {
                return null;
            }

            try
            {
                var cookie = token.Deserialize<TokenCookieModel>();
                if (cookie is null)
                {
                    return null;
                }
                return cookie.ToObject(p.Names.TokenCookieName);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        [SuppressMessage("ReSharper", "AccessToDisposedClosure")]
        async Task QueryTokenAndSave()
        {
            var success = await LogIn(httpClient);
            if (!success)
            {
                throw new InvalidOperationException("Login failed.");
            }

            var cookies = cookieContainer.GetCookies(p.Names.LoginUrl);
            if (cookies[p.Names.TokenCookieName] is not { } token)
            {
                throw new InvalidOperationException("Token cookie not found.");
            }

            await using var stream = File.Open(p.Names.TokensFile, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            if (await TryUpdateExisting())
            {
                return;
            }
            await CreateNew();
            return;

            async ValueTask<bool> TryUpdateExisting()
            {
                if (stream.Length == 0)
                {
                    return false;
                }

                JsonNode? document;
                try
                {
                    document = await JsonNode.ParseAsync(
                        stream,
                        cancellationToken: p.CancellationToken);
                }
                catch (JsonException)
                {
                    return false;
                }

                if (document is not JsonObject jobj)
                {
                    return false;
                }
                return await Save(jobj);
            }

            async Task<bool> CreateNew()
            {
                var root = new JsonObject();
                return await Save(root);
            }

            async Task<bool> Save(JsonObject root)
            {
                stream.Seek(0, SeekOrigin.Begin);
                var tokenModel = TokenCookieModel.FromObject(token);
                root[p.Credentials.Login] = JsonSerializer.SerializeToNode(tokenModel);
                await JsonSerializer.SerializeAsync(
                    stream,
                    root,
                    options: p.JsonOptions ?? DefaultJsonOptions,
                    cancellationToken: p.CancellationToken);
                stream.SetLength(stream.Position);
                return true;
            }
        }

        async Task<bool> LogIn(HttpClient client)
        {
            Uri uri;
            {
                var b = new UriBuilder(p.Names.LoginUrl);
                b.Port = -1;
                var parameters = HttpUtility.ParseQueryString("");
                parameters.Add("UserLogin", p.Credentials.Login);
                parameters.Add("UserPassword", p.Credentials.Password);
                b.Query = parameters.ToString();
                uri = b.Uri;
            }

            var response = await client.PostAsync(
                uri,
                content: null,
                cancellationToken: p.CancellationToken);
            bool success = response.StatusCode == HttpStatusCode.Redirect;
            return success;
        }
    }

    public static Credentials GetCredentials()
    {
        var b = new ConfigurationBuilder();
        b.AddUserSecrets<Program>();
        var config = b.Build();
        var ret = config.GetRequiredSection("Registry").Get<Credentials>();
        if (ret == null)
        {
            throw new InvalidOperationException("Credentials not found.");
        }
        if (ret.Login == null)
        {
            throw new InvalidOperationException("Login not found.");
        }
        if (ret.Password == null)
        {
            throw new InvalidOperationException("Password not found.");
        }
        return ret;
    }

    static GroupForSearch ParseGroupFromOnlineRegistry(GroupParseContext context, string s)
    {
        var mainParser = new Parser(s);
        mainParser.SkipWhitespace();

        var bparser = mainParser.BufferedView();

        var label = ParseLabel(ref bparser);
        var year = ParseYear(ref bparser);
        var groupNumber = ParseGroupNumber(ref bparser);
        var nameWithoutFR = mainParser.PeekSpanUntilPosition(bparser.Position);
        _ = nameWithoutFR;

        mainParser.MoveTo(bparser.Position);

        var languageOrFR = ParseLanguageOrFR(ref mainParser);
        var subGroup = ParseSubGroup(ref mainParser);

        mainParser.SkipWhitespace();
        if (!mainParser.IsEmpty)
        {
            throw new NotSupportedException("Group name not parsed fully.");
        }

        var grade = context.DetermineGrade((int) year);

        return new()
        {
            Grade = grade,
            FacultyName = label,
            GroupNumber = (int) groupNumber,
            // Don't have precedents for master yet.
            QualificationType = QualificationType.Licenta,
            AttendanceMode = languageOrFR.FR ? AttendanceMode.FrecventaRedusa : AttendanceMode.Zi,
            SubGroupName = subGroup,
        };

        static ReadOnlyMemory<char> ParseLabel(ref Parser parser)
        {
            if (!parser.CanPeekCount(2))
            {
                JustThrow("group label");
            }
            var ret = parser.PeekSource(2);
            parser.Move(2);
            return ret;
        }

        static uint ParseYear(ref Parser parser)
        {
            var yearResult = parser.ConsumePositiveInt(GroupHelper.YearLen);
            if (yearResult.Status != ConsumeIntStatus.Ok)
            {
                JustThrow("year");
            }
            return yearResult.Value;
        }

        static uint ParseGroupNumber(ref Parser parser)
        {
            var numberResult = parser.ConsumePositiveInt(GroupHelper.GroupNumberLen);
            if (numberResult.Status != ConsumeIntStatus.Ok)
            {
                JustThrow("group number");
            }
            return numberResult.Value;
        }

        static LanguageOrFR ParseLanguageOrFR(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            var skipResult = bparser.Skip(new SkipUntilOpenParenOrWhiteSpace());
            if (!skipResult.SkippedAny)
            {
                return default;
            }

            var languageOrFRName = parser.PeekSpanUntilPosition(bparser.Position);
            var ret = DetermineIfLabelIsLanguageOrFR(languageOrFRName);
            parser.MoveTo(bparser.Position);
            return ret;
        }

        static LanguageOrFR DetermineIfLabelIsLanguageOrFR(ReadOnlySpan<char> languageOrFRName)
        {
            LanguageOrFR ret = default;
            if (languageOrFRName.Equals("fr", StringComparison.OrdinalIgnoreCase))
            {
                ret.FR = true;
                return ret;
            }

            var maybeLang = LanguageHelper.ParseName(languageOrFRName);
            if (maybeLang is not { } lang)
            {
                JustThrow("language");
            }
            ret.Language = lang;
            return ret;
        }

        static ReadOnlyMemory<char> ParseSubGroup(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                return ReadOnlyMemory<char>.Empty;
            }

            // Possible if we're at a whitespace.
            if (parser.Current != '(')
            {
                return ReadOnlyMemory<char>.Empty;
            }

            parser.Move();
            var bparser = parser.BufferedView();
            var skipResult = bparser.SkipUntil([')']);
            if (skipResult.EndOfInput)
            {
                JustThrow("subgroup number");
            }

            var ret = parser.SourceUntilExclusive(bparser);
            parser.MovePast(bparser.Position);

            return ret;
        }

        [DoesNotReturn]
        static void JustThrow(string part)
        {
            throw new NotSupportedException($"Bad {part}");
        }
    }

    private struct SkipUntilOpenParenOrWhiteSpace : IShouldSkip
    {
        public bool ShouldSkip(char c)
        {
            if (c == '(')
            {
                return false;
            }
            if (char.IsWhiteSpace(c))
            {
                return false;
            }
            return true;
        }
    }

    private static GroupId FindGroupMatch(Schedule schedule, in GroupForSearch g)
    {
        var groups = schedule.Groups;
        for (int i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            if (IsMatch(group, g))
            {
                return new(i);
            }
        }
        return GroupId.Invalid;
    }

    private static bool IsMatch(Group a, in GroupForSearch b)
    {
        bool facultyMatches = a.Faculty.Name.AsSpan().Equals(
            b.FacultyName.Span,
            StringComparison.OrdinalIgnoreCase);
        if (!facultyMatches)
        {
            return false;
        }

        if (a.GroupNumber != b.GroupNumber)
        {
            return false;
        }

        if (a.AttendanceMode != b.AttendanceMode)
        {
            return false;
        }

        if (a.QualificationType != b.QualificationType)
        {
            return false;
        }

        if (a.Grade != b.Grade)
        {
            return false;
        }

        return true;
    }

    private static IEnumerable<RegularLessonId> MatchLessons(LessonMatchParams p)
    {
        var lessonsOfCourse = p.Lookup[p.CourseId];
        foreach (var lessonId in lessonsOfCourse)
        {
            var lesson = p.Schedule.Get(lessonId);
            if (!lesson.Lesson.Groups.Contains(p.GroupId))
            {
                continue;
            }
            if (lesson.Lesson.SubGroup != p.SubGroup)
            {
                continue;
            }

            yield return lessonId;
        }
    }

    // Intentionally duplicated, because the strings are actually different.
    private static LessonType ParseLessonType(
        string s,
        ILogger logger)
    {
        var parser = new Parser(s);
        parser.SkipWhitespace();
        if (parser.IsEmpty)
        {
            return LessonType.Unspecified;
        }
        var bparser = parser.BufferedView();
        _ = parser.SkipNotWhitespace();
        var lessonTypeSpan = parser.PeekSpanUntilPosition(bparser.Position);
        var lessonType = Get(lessonTypeSpan);
        if (lessonType == LessonType.Custom)
        {
            logger.CustomLessonType(lessonTypeSpan);
        }

        parser.MoveTo(bparser.Position);

        parser.SkipWhitespace();
        if (!parser.IsEmpty)
        {
            throw new NotSupportedException("Lesson type not parsed fully.");
        }
        return lessonType;

        LessonType Get(ReadOnlySpan<char> str)
        {
            static bool Equal(
                ReadOnlySpan<char> str,
                string literal)
            {
                return str.Equals(
                    literal.AsSpan(),
                    StringComparison.Ordinal);
            }

            if (Equal(str, "laborator"))
            {
                return LessonType.Lab;
            }
            if (Equal(str, "curs"))
            {
                return LessonType.Curs;
            }
            if (Equal(str, "seminar"))
            {
                return LessonType.Seminar;
            }
            return LessonType.Custom;
        }
    }

    private readonly record struct LessonInstance
    {
        public required RegularLessonId LessonId { get; init; }
        public required DateTime DateTime { get; init; }
        // TODO: add topics
    }

    private readonly struct GetDateTimesOfScheduledLessonsParams
    {
        public required IEnumerable<RegularLessonId> Lessons { get; init; }
        public required Schedule Schedule { get; init; }
        public required LessonTimeConfig TimeConfig { get; init; }
        public required IAllScheduledDateProvider DateProvider { get; init; }
    }

    private static IEnumerable<LessonInstance> GetDateTimesOfScheduledLessons(
        GetDateTimesOfScheduledLessonsParams p)
    {
        foreach (var lessonId in p.Lessons)
        {
            var lesson = p.Schedule.Get(lessonId);
            var lessonDate = lesson.Date;

            var timeSlot = lessonDate.TimeSlot;
            var startTime = p.TimeConfig.GetTimeSlotInterval(timeSlot).Start;

            var dates = p.DateProvider.Dates(new()
            {
                Day = lessonDate.DayOfWeek,
                Parity = lessonDate.Parity,
            });
            foreach (var date in dates)
            {
                var dateTime = new DateTime(
                    date: date,
                    time: startTime);
                yield return new()
                {
                    LessonId = lessonId,
                    DateTime = dateTime,
                };
            }
        }
    }

    public struct ParseStudyWeekWordDocParams
    {
        public required string InputPath;
    }

    public static IEnumerable<StudyWeek> ParseStudyWeekWordDoc(ParseStudyWeekWordDocParams p)
    {
        using var stream = File.OpenRead(p.InputPath);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);
        if (word.MainDocumentPart?.Document is not { } document)
        {
            throw new InvalidOperationException("No document found.");
        }
        if (document.Body is not { } body)
        {
            throw new InvalidOperationException("No body found.");
        }

        var table = body.Descendants<Table>().First();
        var rows = table.ChildElements.OfType<TableRow>();
        using var rowEnumerator = rows.GetEnumerator();

        DateOnly? previousWeekEnd = null;

        VerifyHeader();
        while (rowEnumerator.MoveNext())
        {
            var cells = GetDataCells(rowEnumerator.Current);
            var weekInterval = Week();
            var isOdd = IsOdd();
            previousWeekEnd = weekInterval.End;
            yield return new()
            {
                MondayDate = weekInterval.Start,
                IsOddWeek = isOdd,
            };
            continue;

            WeekInterval Week()
            {
                var weekText = cells.Week.InnerText;
                var parser = new Parser(weekText);
                var ret = ParseWeekInterval(ref parser);
                parser.SkipWhitespace();
                if (!parser.IsEmpty)
                {
                    throw new NotSupportedException("Invalid date range format.");
                }

                {
                    int dayCount = ret.End.DayNumber - ret.Start.DayNumber + 1;
                    if (dayCount != 6)
                    {
                        throw new NotSupportedException("The date range must have 6 days in total");
                    }
                }

                if (previousWeekEnd is { } prevEnd)
                {
                    var dayDiff = ret.Start.DayNumber - prevEnd.DayNumber;
                    if (dayDiff < 0)
                    {
                        throw new NotSupportedException("The week intervals must be in order.");
                    }
                }

                if (ret.Start.DayOfWeek != DayOfWeek.Monday)
                {
                    throw new NotSupportedException("The week interval must start on a Monday.");
                }

                return ret;
            }

            bool IsOdd()
            {
                var parityText = cells.Parity.InnerText;
                var parser = new Parser(parityText);
                var ret = ParseIsOdd(ref parser);
                parser.SkipWhitespace();
                if (!parser.IsEmpty)
                {
                    throw new NotSupportedException("Invalid parity format.");
                }
                return ret;
            }
        }

        yield break;


        void VerifyHeader()
        {
            bool hasHeader = rowEnumerator.MoveNext();
            if (!hasHeader)
            {
                throw new NotSupportedException("No header found.");
            }

            // Check has 3 columns
            // Săptămâna
            // ...
            // Paritatea

            var header = rowEnumerator.Current;
            var headerCells = header.ChildElements.OfType<TableCell>();
            using var cellEnumerator = headerCells.GetEnumerator();

            bool CompareHeader(string expectedText)
            {
                var weekHeaderText = cellEnumerator.Current.InnerText.AsSpan().Trim();
                return IgnoreDiacriticsComparer.Instance.Equals(weekHeaderText, expectedText.AsSpan());
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (!moved)
                {
                    throw new NotSupportedException("No row for the week string");
                }

                if (!CompareHeader("Saptamana"))
                {
                    throw new NotSupportedException("Week header should be first. It had unexpected name.");
                }
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (!moved)
                {
                    throw new NotSupportedException("An insignificant column must follow after the week column.");
                }
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (!moved)
                {
                    throw new NotSupportedException("No row for the parity string");
                }

                if (!CompareHeader("Paritatea"))
                {
                    throw new NotSupportedException("Week header should be first. It had unexpected name.");
                }
            }

            {
                bool moved = cellEnumerator.MoveNext();
                if (moved)
                {
                    throw new NotSupportedException("Must only have 3 columns");
                }
            }
        }

        static (Cell Week, Cell Parity) GetDataCells(TableRow dataRow)
        {
            var cells = dataRow.ChildElements.OfType<Cell>();
            using var cellsEnumerator = cells.GetEnumerator();

            var weekCell = NextCell(cellsEnumerator);
            _ = NextCell(cellsEnumerator);
            var parityCell = NextCell(cellsEnumerator);
            NoNextCell(cellsEnumerator);
            return (
                Week: weekCell,
                Parity: parityCell);
        }

        static Cell NextCell(IEnumerator<Cell> cells)
        {
            bool moved = cells.MoveNext();
            if (!moved)
            {
                throw new NotSupportedException("Expected a cell??");
            }
            return cells.Current;
        }
        static void NoNextCell(IEnumerator<Cell> cells)
        {
            bool moved = cells.MoveNext();
            if (moved)
            {
                throw new NotSupportedException("Expected no more cells.");
            }
        }
    }

    private static readonly ImmutableArray<string> MonthNames = GetMonthNames();

    private static ImmutableArray<string> GetMonthNames()
    {
        // ReSharper disable once CollectionNeverUpdated.Local
        var ret = ImmutableArray.CreateBuilder<string>();
        ret.Capacity = (int) Month.Count;
        ret.Count = (int) Month.Count;
        Set(Month.January, "ianuarie");
        Set(Month.February, "februarie");
        Set(Month.March, "martie");
        Set(Month.April, "aprilie");
        Set(Month.May, "mai");
        Set(Month.June, "iunie");
        Set(Month.July, "iulie");
        Set(Month.August, "august");
        Set(Month.September, "septembrie");
        Set(Month.October, "octombrie");
        Set(Month.November, "noiembrie");
        Set(Month.December, "decembrie");
        Debug.Assert(ret.All(x => x != null));
        return ret.MoveToImmutable();

        void Set(Month m, string name)
        {
            ret[(int) (m - 1)] = name;
        }
    }


    private static Month? ParseMonth(ReadOnlySpan<char> name)
    {
        for (int i = 0; i < MonthNames.Length; i++)
        {
            var monthName = MonthNames[i];
            if (monthName.AsSpan().Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                var month = (Month)(i + 1);
                return month;
            }
        }
        return null;
    }

    private record struct WeekInterval(DateOnly Start, DateOnly End);

    private readonly struct SkipPunctuationOrWhite : IShouldSkip
    {
        public bool ShouldSkip(char c)
        {
            if (char.IsPunctuation(c))
            {
                return true;
            }
            if (char.IsWhiteSpace(c))
            {
                return true;
            }
            return false;
        }
    }

    private static WeekInterval ParseWeekInterval(ref Parser parser)
    {
        parser.SkipWhitespace();

        var dayStart = ParseDayNumber(ref parser);
        parser.SkipWhitespace();
        var monthStart = ParseMonth1(ref parser);
        parser.Skip(new SkipPunctuationOrWhite());

        var dayEnd = ParseDayNumber(ref parser);
        parser.SkipWhitespace();
        var monthEnd = ParseMonth1(ref parser);
        parser.SkipWhitespace();

        var year = ParseYear(ref parser);
        var startDate = CreateDate(dayStart, monthStart);
        var endDate = CreateDate(dayEnd, monthEnd);
        return new()
        {
            Start = startDate,
            End = endDate,
        };

        DateOnly CreateDate(uint day, Month month)
        {
            return new DateOnly(
                year: year,
                month: (int) month,
                day: (int) day);
        }

        uint ParseDayNumber(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            var skipResult = bparser.SkipNumbers();
            if (!skipResult.SkippedAny)
            {
                throw new NotSupportedException("Day number expected");
            }
            var daySpan = parser.PeekSpanUntilPosition(bparser.Position);
            if (daySpan.Length > 2)
            {
                throw new NotSupportedException("Day number too long (max 2 numbers)");
            }
            if (!uint.TryParse(daySpan, out uint day))
            {
                Debug.Fail("This should never happen?");
                day = 0;
            }
            parser.MoveTo(bparser.Position);
            return day;
        }

        Month ParseMonth1(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            var skipResult = bparser.SkipLetters();
            if (!skipResult.SkippedAny)
            {
                throw new NotSupportedException("Month name expected");
            }
            var monthSpan = parser.PeekSpanUntilPosition(bparser.Position);
            if (ParseMonth(monthSpan) is not { } month)
            {
                throw new NotSupportedException("Month name not recognized");
            }
            return month;
        }

        int ParseYear(ref Parser parser)
        {
            var yearResult = parser.ConsumePositiveInt(4);
            if (yearResult.Status != ConsumeIntStatus.Ok)
            {
                throw new NotSupportedException("Year not parsed");
            }
            return (int) yearResult.Value;
        }
    }

    private static bool ParseIsOdd(ref Parser parser)
    {
        var bparser = parser.BufferedView();
        bparser.SkipNotWhitespace();
        var span = parser.PeekSpanUntilPosition(bparser.Position);

        static bool Equals1(ReadOnlySpan<char> span, string literal)
        {
            return span.Equals(literal.AsSpan(), StringComparison.CurrentCultureIgnoreCase);
        }

        if (Equals1(span, "Pară"))
        {
            parser.MoveTo(bparser.Position);
            return false;
        }
        if (Equals1(span, "Impară"))
        {
            parser.MoveTo(bparser.Position);
            return true;
        }
        throw new NotSupportedException("Parity not recognized");
    }
}

public enum Month
{
    January = 1,
    February = 2,
    March = 3,
    April = 4,
    May = 5,
    June = 6,
    July = 7,
    August = 8,
    September = 9,
    October = 10,
    November = 11,
    December = 12,
    Count = 12,
}

public enum Option
{
    AllTeachersExcel,
    PerGroupAndPerTeacherPdfs,
    CreateLessonsInRegistry,
}

public sealed class Credentials
{
    public required string Login { get; set; }
    public required string Password { get; set; }
}

public sealed class TokenCookieModel
{
    public required string Value { get; set; }
    public DateTime ExpireTime { get; set; }

    public Cookie ToObject(string name)
    {
        var ret = new Cookie(name, Value)
        {
            Expires = ExpireTime,
        };
        return ret;
    }

    public static TokenCookieModel FromObject(Cookie cookie)
    {
        var expireTime = cookie.Expires;
        return new()
        {
            Value = cookie.Value,
            ExpireTime = expireTime,
        };
    }
}

public enum Session
{
    Ses1,
    Ses2,
}

internal readonly record struct CourseLink(CourseId CourseId, Uri Url);
internal readonly record struct GroupLink
{
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required Uri Uri { get; init; }
}

internal readonly record struct LessonInstanceLink
{
    public required DateTime DateTime { get; init; }
    public required LessonType LessonType { get; init; }
    public required Uri EditUri { get; init; }
}

internal readonly record struct LessonMatchParams
{
    public required CourseId CourseId { get; init; }
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required LessonsByCourseMap Lookup { get; init; }
    public required Schedule Schedule { get; init; }
}

internal record struct LanguageOrFR
{
    public Language? Language;
    public bool FR;
    public readonly bool IsLanguage => Language is not null;
}

internal struct GroupForSearch
{
    public required AttendanceMode AttendanceMode;
    public required Grade Grade;
    public required int GroupNumber;
    public required ReadOnlyMemory<char> FacultyName;
    public required QualificationType QualificationType;
    public required ReadOnlyMemory<char> SubGroupName;
}

public record struct StudyWeek(DateOnly MondayDate, bool IsOddWeek);


public interface IAllScheduledDateProvider
{

    public readonly struct Params
    {
        public required Parity Parity { get; init; }
        public required DayOfWeek Day { get; init; }
    }
    IEnumerable<DateOnly> Dates(Params p);
}

public sealed class ManualAllScheduledDateProvider
    : IAllScheduledDateProvider
{
    public required StudyWeek[] StudyWeeks { private get; init; }

    public IEnumerable<DateOnly> Dates(IAllScheduledDateProvider.Params p)
    {
        foreach (var week in StudyWeeks)
        {
            if (!IsParityMatch())
            {
                continue;
            }
            const int weekdayCount = 7;
            var offset = (p.Day - DayOfWeek.Monday + weekdayCount) % weekdayCount;
            var ret = week.MondayDate.AddDays(offset);
            yield return ret;

            continue;

            bool IsParityMatch()
            {
                switch (p.Parity)
                {
                    case Parity.EveryWeek:
                    {
                        return true;
                    }
                    case Parity.OddWeek:
                    {
                        return week.IsOddWeek;
                    }
                    case Parity.EvenWeek:
                    {
                        return !week.IsOddWeek;
                    }
                    default:
                    {
                        Debug.Fail("Impossible value of parity");
                        return false;
                    }
                }
            }
        }
    }
}
