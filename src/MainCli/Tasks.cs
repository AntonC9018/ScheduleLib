using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using AngleSharp.Html.Dom;
using AngleSharp.Dom;
using ClosedXML.Excel;
using ConvertDocToDocx;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Drive.v3;
using Google.Apis.Services;
using Google.Apis.Util.Store;
using Microsoft.Extensions.Configuration;
using OpenHolidays;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using MainCli.ExcelBuilder;
using QuizModels;
using ScheduleLib.OnlineRegistry;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Generation.TeacherCute;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Excel;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.GroupParser;
using ScheduleLib.Parsing.Moodle;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.Scraping.Common;
using SpreadCheetah;
using Column = DocumentFormat.OpenXml.Spreadsheet.Column;
using Columns = DocumentFormat.OpenXml.Spreadsheet.Columns;
using Font = DocumentFormat.OpenXml.Spreadsheet.Font;
using Group = ScheduleLib.Group;
using HorizontalAlignmentValues = DocumentFormat.OpenXml.Spreadsheet.HorizontalAlignmentValues;
using VerticalAlignmentValues = DocumentFormat.OpenXml.Spreadsheet.VerticalAlignmentValues;

namespace MainCli;

public struct GeneratePdfForGroupsAndTeachersParams()
{
    public required PdfLessonTextDisplayHandler.Services LessonTextDisplayServices;
    public required LessonTimeConfig LessonTimeConfig;
    public required TimeSlotDisplayHandler TimeSlotDisplay;
    public required DayNameProvider DayNameProvider;
    public required Schedule Schedule;
    public required string OutputPath;
}

public struct AllTeacherExcelParams()
{
    public required string OutputFilePath;
    public required DayNameProvider DayNameProvider;
    public required (DayOfWeek Day, TimeSlot TimeSlot) SeminarDate;
    public required StringBuilder StringBuilder;
    public required LessonTypeDisplayHandler LessonTypeDisplay;
    public required ParityDisplayHandler ParityDisplay;
    public required TimeSlotDisplayHandler TimeSlotDisplay;
    public required FilteredSchedule Schedule;
    public required LessonTimeConfig TimeConfig;
}


public struct ParseStudyWeekWordDocParams
{
    public required string InputPath;
    public required HolidayPeriod[] Holidays;
}

public static class Tasks
{
    public static async Task GeneratePdfForGroupsAndTeachers(GeneratePdfForGroupsAndTeachersParams p)
    {
        Directory.CreateDirectory(p.OutputPath);

        QuestPDF.Settings.License = LicenseType.Community;

        var tasks = new List<Task>();
        {
            var textDisplayHandler = new PdfLessonTextDisplayHandler(
                p.LessonTextDisplayServices,
                new()
                {
                });

            foreach (var g in p.Schedule.EnumerateGroups())
            {
                // Enumerating all lessons twice - fix
                var subgroups = new HashSet<SubGroup>();
                foreach (var l in p.Schedule.EnumerateLessons())
                {
                    ref var lesson = ref l.Item.Lesson;
                    if (lesson.Groups.Contains(g.Id))
                    {
                        subgroups.Add(lesson.SubGroup);
                    }
                }

                foreach (var subgroup in subgroups)
                {
                    var t = Task.Run(() =>
                    {
                        var groupName = g.Item.Name;
                        var sb = new StringBuilder();
                        sb.Append(groupName);
                        if (subgroup != SubGroup.All)
                        {
                            sb.Append($"_{subgroup.Value}");
                        }
                        sb.Append(".pdf");
                        var fileName = sb.ToString();

                        var groupFilter = new GroupFilter
                        {
                            OneOfGroupIds = [g.Id],
                        };
                        if (subgroup != SubGroup.All)
                        {
                            groupFilter.SubGroups = [subgroup, SubGroup.All];
                        }

                        GenerateWithFilter(fileName, textDisplayHandler, new()
                        {
                            GroupFilter = groupFilter,
                        });
                    });
                    tasks.Add(t);
                }
            }
        }

        {
            var textDisplayHandler = new PdfLessonTextDisplayHandler(p.LessonTextDisplayServices, new()
            {
                PrintsTeacherName = false,
                PrintsGroupNames = true,
            });
            var sb = new StringBuilder();
            for (int teacherId = 0; teacherId < p.Schedule.Teachers.Length; teacherId++)
            {
                int teacherId1 = teacherId;

                var teacherName = p.Schedule.Teachers[teacherId1].PersonName;
                TeacherNameHelper.AsFileName(sb, teacherName);
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
            var filteredSchedule = p.Schedule.Filter(
                filter.WithLatestPeriod(p.Schedule));
            if (filteredSchedule.IsEmpty)
            {
                return;
            }

            var generator = new Generator(new()
            {
                StringBuilder = new(),
                DayNameProvider = p.DayNameProvider,
                LessonTextDisplayHandler = textDisplayHandler,
                LessonTimeConfig = p.LessonTimeConfig,
                TimeSlotDisplay = p.TimeSlotDisplay,
            }, filteredSchedule);

            var path = Path.Combine(p.OutputPath, name);
            generator.GeneratePdf(path);
        }
    }

    public static void GenerateAllTeacherExcel(AllTeacherExcelParams p)
    {
        {
            var dir = Path.GetDirectoryName(p.OutputFilePath)!;
            Directory.CreateDirectory(dir);
        }
        using var stream = File.Open(p.OutputFilePath, FileMode.Create, FileAccess.ReadWrite);
        using var excel = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true);

        var teachers = p.Schedule.Teachers
            .OrderBy(id =>
            {
                var teacher = p.Schedule.Source.Get(id);
                return teacher.PersonName;
            }, PersonNameLastFirstAlphabeticComparer.Instance)
            .ToArray();

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
                TopLeftCell = ExcelRangeHelper.GetCellReference(new()
                {
                    Position = new(Col: 2, Row: 1),
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
                Max = (uint)(3 + teachers.Length),
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
                merge.Reference = ExcelRangeHelper.GetCellRange(new()
                {
                    Start = new(Col: 0, Row: rowIndexStart),
                    EndInclusive = new(Col: 0, Row: rowIndexEnd),
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
            foreach (var id in teachers)
            {
                NameDisplayHelper.Append(new()
                {
                    InsertSpaceAfterShortName = true,
                    Output = sb,
                    Name = p.Schedule.Source.Get(id).PersonName,
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
                p.Schedule.Lessons,
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
                    var rowKey = new DefaultRowKey
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

                    foreach (var teacherId in teachers)
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

                        var cellKey = rowKey.DefaultCellKey(teacherId);
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

                        var commaList = new ListStringBuilder(sb, ",");
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
                var course = p.Schedule.Source.Get(lesson.Lesson.Course);
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

                var group = p.Schedule.Source.Get(groups.Group0);
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

    public static ManualAllScheduledDateProvider CreateDateProviderFromWeekParityExcel(
        ParseStudyWeekWordDocParams p)
    {
        using var stream = File.OpenRead(p.InputPath);
        using var word = WordprocessingDocument.Open(stream, isEditable: false);
        var studyWeeks = ParityExcelParser.Parse(word).ToArray();
        var ret = new ManualAllScheduledDateProvider(
            studyWeeks: studyWeeks,
            holidays: p.Holidays);
        return ret;
    }

    public static Credentials GetRegistryCredentials(
        IConfiguration configuration,
        bool allowUserInput)
    {
        var ret = configuration.MaybeGetCredentials(RegistryScraping.CredentialsConfigKey);
        if (ret != null)
        {
            return ret;
        }
        if (!allowUserInput)
        {
            throw new InvalidOperationException("Credentials not found.");
        }

        Console.WriteLine("No 'Registry' key specified in user secrets.");
        Console.WriteLine("https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets?view=aspnetcore-9.0&tabs=windows#secret-manager");
        Console.WriteLine("You may input it manually for this session only:");

        Console.Write("Login: ");
        var login = Console.ReadLine() ?? throw new InvalidOperationException();

        Console.Write("Password: ");
        using var password = ReadPassword();

        ret = new()
        {
            Login = login,
            Password = password.ToString() ?? throw Unreachable(),
        };
        return ret;
    }

    private static SecureString ReadPassword()
    {
        var pwd = new SecureString();
        while (true)
        {
            ConsoleKeyInfo i = Console.ReadKey(intercept: true);
            if (i.Key == ConsoleKey.Enter)
            {
                break;
            }

            if (i.Key == ConsoleKey.Backspace)
            {
                if (pwd.Length == 0)
                {
                    continue;
                }

                pwd.RemoveAt(pwd.Length - 1);
                Console.Write("\b \b");
                continue;
            }

            // the key pressed does not correspond to a printable character, e.g. F1, Pause-Break, etc
            if (i.KeyChar != '\u0000')
            {
                pwd.AppendChar(i.KeyChar);
                Console.Write("*");
                continue;
            }
        }
        return pwd;
    }

    public static void OptionallyEnrichContextWithTeacherFullNames(
        ScheduleBuilder schedule,
        string filePath)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        using var excel = SpreadsheetDocument.Open(filePath, isEditable: false, new()
        {
            AutoSave = false,
            CompatibilityLevel = CompatibilityLevel.Version_2_20,
        });

        ExcelTeacherListParser.AddTeachersFromExcel(new()
        {
            Excel = excel,
            Schedule = schedule,
        });
    }

    public static string GetDirectoryHash(
        string srcFullPath,
        string searchPattern = "*",
        bool hashPaths = true,
        bool hashContents = true)
    {
        Debug.Assert(srcFullPath == Path.GetFullPath(srcFullPath));

        var filePaths = Directory.GetFiles(
                srcFullPath,
                searchPattern: searchPattern,
                SearchOption.AllDirectories)
            .OrderBy(p => p)
            .ToArray();

        const int MaxPathBytes = 4096;
        const int BufferSize = 8192;
        using var pathBuffer = new RentedBuffer<byte>(MaxPathBytes);
        using var readBuffer = new RentedBuffer<byte>(BufferSize);
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.MD5);

        foreach (var filePath in filePaths)
        {
            if (hashPaths)
            {
                var relativePath = filePath.AsSpan(srcFullPath.Length + 1);
                int byteCount = Encoding.UTF8.GetBytes(relativePath, pathBuffer.Span);
                hasher.AppendData(pathBuffer.Span[.. byteCount]);
            }

            if (hashContents)
            {
                using var fs = File.OpenRead(filePath);
                int read;
                while ((read = fs.Read(readBuffer.Span)) > 0)
                {
                    hasher.AppendData(readBuffer.Span[.. read]);
                }
            }
        }

        var hashLen = hasher.HashLengthInBytes;
        using var hash = new RentedBuffer<byte>(hashLen);
        int len = hasher.GetCurrentHash(hash.Span);
        Debug.Assert(len == hashLen);
        return Convert.ToHexStringLower(hash.Span);
    }

    public static async Task ParseDocumentDirIntoSchedule(
        DocParseContext context,
        string dirName,
        CancellationToken cancellationToken)
    {
        dirName = Path.GetFullPath(dirName);

        await ParseDirectoryToSchedule(
            context,
            dirName,
            cancellationToken: cancellationToken);

        var subdirs = Directory.EnumerateDirectories(dirName, "*", SearchOption.TopDirectoryOnly)
            .Select(x =>
            {
                var lastSegmentStart = x.LastIndexOf(Path.DirectorySeparatorChar);
                Debug.Assert(lastSegmentStart != -1);
                lastSegmentStart += 1;

                var lastSegment = x.AsSpan()[lastSegmentStart ..];

                if (!DateOnly.TryParseExact(
                        lastSegment,
                        format: "dd.MM.yy",
                        provider: null,
                        style: DateTimeStyles.None,
                        result: out var startDate))
                {
                    throw new InvalidOperationException($"The folders must be named in the format 'DD.MM.YYYY'. Found this: {x}");
                }
                return (SubDirPath: x, StartDate: startDate);
            })
            .OrderBy(x => x.StartDate);

        foreach (var t in subdirs)
        {
            await ParseDirectoryToSchedule(
                context,
                t.SubDirPath,
                cancellationToken: cancellationToken,
                period: new()
                {
                    StartDate = t.StartDate,
                });
        }
        return;

        static async Task ParseDirectoryToSchedule(
            DocParseContext context,
            string dirName,
            CancellationToken cancellationToken,
            PeriodBeginning? period = null)
        {
            foreach (var filePath in Directory.EnumerateFiles(dirName, "*.doc", SearchOption.TopDirectoryOnly))
            {
                var outputPath = PathHelper.WithExtension(filePath, ".docx");
                var conversionSuccessful = await DocToDocxConversionHelper.TryConvertFile(
                    inputPath: filePath,
                    outputPath: outputPath,
                    cancellationToken: cancellationToken);
                if (!conversionSuccessful)
                {
                    throw new InvalidOperationException("Could not convert doc to docx");
                }
                File.Delete(filePath);
            }

            foreach (var filePath in Directory.EnumerateFiles(dirName, "*.docx", SearchOption.TopDirectoryOnly))
            {
                using var document = WordprocessingDocument.Open(filePath, isEditable: false);
                context.SetPeriod(period);

                WordScheduleParser.ParseToSchedule(new()
                {
                    Context = context,
                    Document = document,
                });
            }
        }
    }

    // ReSharper disable once UnusedMember.Global
    public static async Task<HolidayPeriod[]> GetHolidayPeriodsFromApi(
        Schedule schedule,
        CancellationToken cancellationToken)
    {
        using var holidaysHttpClient = new HttpClient();
        var holidaysClient = new OpenHolidaysClient(holidaysHttpClient);
        var holidaysProvider = new HolidaysProvider(holidaysClient, new()
        {
            CountryIsoCode = "MD",
        });
        var wholePeriod = schedule.WholePeriod();
        var ret = await holidaysProvider.GetHolidayPeriods(new()
        {
            From = wholePeriod.Start,
            To = wholePeriod.EndExclusive,
            CancellationToken = cancellationToken,
        });
        return ret;
    }

    public struct GenerateFreeRoomsParams
    {
        public required string OutputPath;
        public required ParityDisplayHandler ParityDisplay;
        public required TimeSlotDisplayHandler TimeSlotDisplay;
        public required DayNameProvider DayNameProvider;
        public required CancellationToken CancellationToken;
        public required Schedule Schedule;
        public required LessonTimeConfig TimeConfig;
    }

    public static async Task GenerateFreeRoomsExcel(GenerateFreeRoomsParams p)
    {
        p.OutputPath = Path.GetFullPath(p.OutputPath);

        {
            var dirName = Path.GetDirectoryName(p.OutputPath);
            if (dirName != null)
            {
                Debug.Assert(dirName.Length > 0);
                Directory.CreateDirectory(dirName);
            }
        }

        {
            var rooms = PreprocessedRooms(p.Schedule.RegularLessons);
            var allRooms = rooms.Distinct().ToHashSet();

            await using var outputDoc = File.Create(p.OutputPath);
            await using var spreadsheet = await Spreadsheet.CreateNewAsync(
                outputDoc,
                cancellationToken: p.CancellationToken);
            var headerStyle = spreadsheet.AddStyle(new()
            {
            });

            Parity[] parities = [Parity.EvenWeek, Parity.OddWeek];
            List<DataCell> dataCells = new();

            foreach (var parity in parities)
            {
                string parityLabel = p.ParityDisplay.Get(parity)!;
                await spreadsheet.StartWorksheetAsync(parityLabel, new()
                {
                }, p.CancellationToken);

                foreach (var day in new AllEnumEnumerable<DayOfWeek>())
                {
                    var lessonsThisDay = p.Schedule.RegularLessons
                        .Where(x => x.Date.DayOfWeek == day)
                        .ToArray();
                    if (lessonsThisDay.Length == 0)
                    {
                        continue;
                    }

                    var dayName = p.DayNameProvider.GetDayName(day);
                    await spreadsheet.AddHeaderRowAsync([dayName], headerStyle, p.CancellationToken);

                    foreach (var timeSlot in p.TimeConfig.TimeSlots)
                    {
                        var interval = p.TimeConfig.GetTimeSlotInterval(timeSlot);
                        var intervalText = p.TimeSlotDisplay.IntervalDisplay(interval);
                        dataCells.Add(new(intervalText));

                        var lessons = lessonsThisDay
                            .Where(x => x.Date.TimeSlot == timeSlot
                                && x.Date.Parity.IsMatch(parity));
                        var occupiedRooms = PreprocessedRooms(lessons);
                        var freeRooms = allRooms.Except(occupiedRooms);
                        var orderedFreeRooms = freeRooms.OrderBy(roomId =>
                        {
                            var id = roomId.Id!;
                            // TODO: Maybe store this in the model?
                            var blockIndex = id.IndexOf('/');
                            var block = blockIndex >= 0 ? id[(blockIndex + 1) ..] : null;
                            var room = id[.. blockIndex];
                            return (block, room);
                        });
                        foreach (var freeRoom in orderedFreeRooms)
                        {
                            dataCells.Add(new(freeRoom.Id!));
                        }

                        await spreadsheet.AddRowAsync(dataCells, p.CancellationToken);
                        dataCells.Clear();
                    }
                    await spreadsheet.AddRowAsync(dataCells, p.CancellationToken);
                }
            }
            await spreadsheet.FinishAsync();
        }
        return;

        // var room = schedule.RegularLessons.Where(x => x.Lesson.Room.Id == "15:00").ToArray();
        // var group = room.Select(x => schedule.Get(x.Lesson.Group)).ToArray();
        // _ = group;
        RoomId S(RoomId r)
        {
            if (!r.IsValid)
            {
                return r;
            }
            if (r.Id!.Contains("/"))
            {
                return r;
            }
            var updated = $"{r.Id}/4";
            return new RoomId(updated);
        }

        IEnumerable<RoomId> PreprocessedRooms(IEnumerable<RegularLesson> lessons)
        {
            var preprocessed = lessons
                .Select(x => x.Lesson.Room)
                .Select(S)
                .Where(x => x.IsValid);
            return preprocessed;
        }
    }

    public struct UploadStuffToDriveParams
    {
        public required IConfiguration Configuration;
        public required string OutputDirectory;
        public required CancellationToken CancellationToken;
    }

    public static async Task UploadStuffToDrive(UploadStuffToDriveParams p)
    {
        string[] scopes = [
            DriveService.Scope.DriveFile,
            DriveService.Scope.Drive,
        ];
        var credPath = "google_token_store";

        var clientSecrets = p.Configuration.GetSection("Google").Get<ClientSecrets>();
        if (clientSecrets is null
            || clientSecrets.ClientId == null
            || clientSecrets.ClientSecret == null)
        {
            throw new InvalidOperationException("Configuration for google is missing");
        }

        var credential = await GoogleWebAuthorizationBroker.AuthorizeAsync(
            clientSecrets: clientSecrets,
            scopes: scopes,
            user: "user",
            taskCancellationToken: CancellationToken.None,
            dataStore: new FileDataStore(credPath, fullPath: true));

        using var driveService = new DriveService(
            new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "ScheduleLib",
            });
        _ = driveService;

        var folderId = await driveService.FindFolderId("orar", p.CancellationToken);
        var files = await driveService.GetFiles(folderId, p.CancellationToken);

        var comparer = StringComparer.OrdinalIgnoreCase;
        var existingLocalFiles = Directory.EnumerateFiles(p.OutputDirectory)
            .Select(x => Path.GetFileName(x))
            .ToHashSet(comparer);
        var existingCloudFiles = files.Select(x => x.Name).ToHashSet(comparer);
        var cloudFilesToDelete = new List<BasicDriveFile>();
        var cloudFilesToUpdate = new List<BasicDriveFile>();
        var cloudFilesToCreate = new List<string>();
        foreach (var file in files)
        {
            if (existingLocalFiles.Contains(file.Name))
            {
                cloudFilesToUpdate.Add(file);
            }
            else
            {
                cloudFilesToDelete.Add(file);
            }
        }
        foreach (var local in existingLocalFiles)
        {
            if (!existingCloudFiles.Contains(local))
            {
                cloudFilesToCreate.Add(local);
            }
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(p.CancellationToken);
        var batchDeleteOperation = DriveApiHelper.ExecuteBatchDeleteAsync(
            driveService,
            cloudFilesToDelete,
            cts.Token);
        var taskBuilder = ArrayBuilder.Create<Task>(
            cloudFilesToCreate.Count
            + cloudFilesToUpdate.Count
            + batchDeleteOperation.BatchCount);
        try
        {
            foreach (var deleteTask in batchDeleteOperation.Tasks)
            {
                taskBuilder.Add(deleteTask);
            }
            foreach (var fileName in cloudFilesToCreate)
            {
                var t = driveService.UploadFile(
                    inputFilePath: Path.Combine(p.OutputDirectory, fileName),
                    outputFileName: fileName,
                    folderId: folderId,
                    cancellationToken: cts.Token);
                taskBuilder.Add(t);
            }
            foreach (var file in cloudFilesToUpdate)
            {
                var t = driveService.UpdateFile(
                    fileInputPath: Path.Combine(p.OutputDirectory, file.Name),
                    fileId: file.Id,
                    cancellationToken: cts.Token);
                taskBuilder.Add(t);
            }
            await Task.WhenAll(taskBuilder.Complete());
        }
        catch (Exception)
        {
            cts.Cancel();
            throw;
        }
    }

    public struct PrintFreeHoursOfGroupParams
    {
        public required Schedule Schedule;
        public required DayNameProvider DayNameProvider;
        public required LessonTimeConfig TimeConfig;
        public required string[] Groups;
        public required StringBuilder StringBuilder;
    }

    public static void PrintFreeHoursOfGroup(PrintFreeHoursOfGroupParams p)
    {
        foreach (var parity in new[]{Parity.EvenWeek, Parity.OddWeek})
        {
            foreach (var group in p.Groups)
            {
                foreach (var isOptional in new[] { true, false })
                {
                    var displayHandler = new TimeSlotDisplayHandler();
                    var groupId = p.Schedule.Groups
                        .WithIndex()
                        .Where(x => x.Item.Name == group)
                        .Select(x => new GroupId(x.Index))
                        .Single();
                    var lessons = p.Schedule.RegularLessons
                        .Where(x => x.Lesson.Groups.Contains(groupId) && x.Date.Parity.IsMatch(parity))
                        .Where(x =>
                        {
                            if (!isOptional)
                            {
                                return true;
                            }
                            var sg = x.Lesson.SubGroup;
                            if (sg == SubGroup.All)
                            {
                                return true;
                            }
                            if (sg.Value == "opțional")
                            {
                                return true;
                            }
                            return false;
                        });

                    var allTimes = p.TimeConfig.TimeSlots
                        .SelectMany(x => new[]
                            {
                                DayOfWeek.Monday,
                                DayOfWeek.Tuesday,
                                DayOfWeek.Wednesday,
                                DayOfWeek.Thursday,
                                DayOfWeek.Friday,
                            }
                            .Select(y => (Day: y, Time: x)));

                    var usedTimes = lessons.Select(x => (Day: x.Date.DayOfWeek, Time: x.Date.TimeSlot));
                    var unusedTimes = allTimes.Except(usedTimes);

                    var orderedTimes = unusedTimes.OrderBy(x => (x.Day, x.Time));
                    var byDay = orderedTimes
                        .GroupBy(x => x.Day)
                        .Select(x => (Day: x.Key, Times: MergeConsecutive(x.Select(y => y.Time))));

                    var parityDisplay = new ParityDisplayHandler();
                    p.StringBuilder.AppendLine($"paritatea: {parityDisplay.Get(parity)}, grupa: {group}, optional?: {isOptional}");
                    foreach (var day in byDay)
                    {
                        p.StringBuilder.Append(p.DayNameProvider.GetDayName(day.Day));
                        p.StringBuilder.Append(":");

                        var listBuilder = new ListStringBuilder(p.StringBuilder, ",");
                        foreach (var time in day.Times)
                        {
                            var start = time.Start;
                            var end = time.EndInclusive;
                            var startTime = p.TimeConfig.GetTimeSlotInterval(start).Start;
                            var endTime = p.TimeConfig.GetTimeSlotInterval(end).End;
                            var duration = endTime - startTime;
                            var intervalStr = displayHandler.IntervalDisplay(new TimeSlotInterval(startTime, duration));
                            listBuilder.Append(intervalStr);
                        }
                        p.StringBuilder.AppendLine();
                    }
                    p.StringBuilder.AppendLine();
                    continue;


                    IEnumerable<(TimeSlot Start, TimeSlot EndInclusive)> MergeConsecutive(IEnumerable<TimeSlot> x)
                    {
                        using var e = x.GetEnumerator();
                        if (!e.MoveNext())
                        {
                            yield break;
                        }
                        var start = e.Current;
                        var prev = start;
                        while (true)
                        {
                            if (!e.MoveNext())
                            {
                                yield return (start, prev);
                                yield break;
                            }
                            var c = e.Current;
                            if (c.Index - prev.Index > 1)
                            {
                                yield return (start, prev);
                                start = c;
                            }
                            prev = c;
                        }
                    }
                }
            }
        }
    }

    public static async Task<Schedule> LoadSchedule(
        DocParseContext context,
        string scheduleSourcesDir,
        string serializedSchedulePath,
        Action<DocParseContext> beforeEndAction,
        CancellationToken cancellationToken,
        bool bypassCache = false)
    {
        var scheduleSourcesDirFullPath = Path.GetFullPath(scheduleSourcesDir);

        async ValueTask<SerializationModels.ScheduleModel?> GetValidModel()
        {
            if (bypassCache)
            {
                return null;
            }
            if (!Path.Exists(serializedSchedulePath))
            {
                return null;
            }

            var filesHash = GetDirectoryHash(scheduleSourcesDirFullPath);

            await using var inputFile = File.OpenRead(serializedSchedulePath);
            var serializedModel = await ScheduleSerializer.Deserialize(inputFile, cancellationToken);
            if (serializedModel.Hash != filesHash)
            {
                return null;
            }

            return serializedModel;
        }

        if (await GetValidModel() is { } scheduleSerializedModel)
        {
            ScheduleSerializer.AddToBuilder(
                context.Schedule,
                scheduleSerializedModel,
                context.CourseNameUnifierModule);

            // beforeEndAction(context);
            var schedule = context.Schedule.Build();
            return schedule;
        }

        {
            await ParseDocumentDirIntoSchedule(
                context,
                scheduleSourcesDirFullPath,
                cancellationToken: cancellationToken);

            beforeEndAction(context);

            var schedule = context.Schedule.Build();

            // I think word resaves them in some way.
            var newFilesHash = GetDirectoryHash(scheduleSourcesDirFullPath);
            await using var outputFile = new FileStream(serializedSchedulePath, FileMode.Create);
            await ScheduleSerializer.Serialize(schedule, outputFile, newFilesHash, cancellationToken);
            return schedule;
        }
    }

    public readonly struct GenerateDeadlinesExcelParams()
    {
        public required FilteredSchedule Schedule { get; init; }
        public required IAllScheduledDateProvider DateProvider { get; init; }
        public required Semester Semester { get; init; }
        public required LessonTimeConfig TimeConfig { get; init; }
        public required SemesterIntervalProvider SemesterIntervalProvider { get; init; }
        public required string OutputFilePath { get; init; }
        public required System.Drawing.Color GoodColor { get; init; }
        public required System.Drawing.Color BadColor { get; init; }
        public required int LessonDelayLimit { get; init; }
        public required int MaxTaskRows { get; init; }
        public float ColumnWidth { get; init; } = 5;
    }

    public static void GenerateDeadlinesExcel(GenerateDeadlinesExcelParams p)
    {
        using var workbook = new XLWorkbook();

        foreach (var group in p.Schedule.Groups)
        {
            var lessonsBySubgroup = p.Schedule.Lessons
                .Where(x => x.Lesson.Group == group)
                .GroupBy(x => (x.Lesson.SubGroup, x.Lesson.Course))
                .ToArray();

            if (lessonsBySubgroup.Length > 1
                && lessonsBySubgroup.Any(x => x.Key.SubGroup == SubGroup.All))
            {
                throw new NotImplementedException("Shared labs not implemented");
            }

            foreach (var l in lessonsBySubgroup)
            {
                var key = l.Key;
                var scheduledLessons = ScheduledLessonsHelper.GetSortedScheduledLessons(new()
                {
                    Lessons = l.Select(x => x.Id),
                    Schedule = p.Schedule.Source,
                    DateProvider = p.DateProvider,
                    Semester = p.Semester,
                    TimeConfig = p.TimeConfig,
                    SemesterIntervalProvider = p.SemesterIntervalProvider,
                }).ToArray();
                if (scheduledLessons.Length == 0)
                {
                    continue;
                }

                string sheetName;
                {
                    var s = p.Schedule.Source;
                    var shortName = s.Get(key.Course).Names[^1];
                    var groupName = s.Get(group).Name;
                    sheetName = $"{shortName} - {groupName}";
                    if (key.SubGroup != SubGroup.All)
                    {
                        sheetName = $"{sheetName}({key.SubGroup.Value})";
                    }
                }
                var worksheet = workbook.Worksheets.Add(sheetName);

                const int emptyCols = 1;
                const int firstRowPos = 1;
                const int firstColPos = emptyCols + 1;
                {
                    var firstRow = worksheet.Row(firstRowPos);
                    for (int index = 0; index < scheduledLessons.Length; index++)
                    {
                        int cellIndex = index + firstColPos;
                        var lesson = scheduledLessons[index];
                        var cell = firstRow.Cell(cellIndex);
                        var d = lesson.DateTime;
                        cell.Value = d.ToString("dd.MM");
                    }
                    for (int index = 0; index < scheduledLessons.Length; index++)
                    {
                        worksheet.Column(index + firstColPos).Width = p.ColumnWidth;
                    }
                }

                int maxCols = scheduledLessons.Length;

                var dataRange = worksheet.Range(
                    firstCellRow: firstRowPos + 1,
                    firstCellColumn: firstColPos,
                    lastCellRow: p.MaxTaskRows,
                    lastCellColumn: maxCols);

                for (int i = 0; i <= p.LessonDelayLimit; i++)
                {
                    var gradientPos = (float) i / p.LessonDelayLimit;
                    var color = ColorHelper.Lerp(p.GoodColor, p.BadColor, gradientPos);
                    var xlColor = XLColor.FromColor(color);
                    var conditionalFormat = worksheet.AddConditionalFormat();
                    conditionalFormat.Range = dataRange;
                    conditionalFormat
                        .WhenEquals(-i)
                        .Fill
                        .SetBackgroundColor(xlColor);
                }

                foreach (var cell in dataRange.Cells())
                {
                    string leftCellRef = worksheet
                        .Cell(cell.Address.RowNumber, cell.Address.ColumnNumber - 1)
                        .Address
                        .ToStringRelative();
                    string formula = $"""=IF(AND({leftCellRef}<>"",{leftCellRef}<=0,{leftCellRef}>{-p.LessonDelayLimit}),{leftCellRef}-1,"")""";
                    cell.FormulaA1 = formula;
                }
            }
        }

        {
            if (Path.GetDirectoryName(p.OutputFilePath) is { } outputDirectory)
            {
                Directory.CreateDirectory(outputDirectory);
            }
            using var outputStream = new FileStream(p.OutputFilePath, FileMode.Create, FileAccess.Write);
            workbook.SaveAs(outputStream);
        }
    }

    public static async Task CopyGradesFromMoodleForTest(
        IConfiguration config,
        CourseNameUnifierModule courseNameUnifierModule,
        LookupModule lookupModule,
        Schedule schedule,
        GroupParseContext groupParseContext,
        Semester semester,
        string quizId,
        CancellationToken cancellationToken)
    {
        var registryCredentials = Tasks.GetRegistryCredentials(config, allowUserInput: false);
        var moodleCredentials = config.GetCredentials(MoodleInterop.CredentialsKey);

        using var registryContext = await RegistryScrapingContext.Create(registryCredentials, cancellationToken);
        using var moodleContext = await MoodleScrapingContext.Create(moodleCredentials, cancellationToken);

        var registryNav = registryContext.Navigator(
            new RegistryErrorLogger(),
            cancellationToken);
        var coursesNav = registryNav.Courses(
            courseNameUnifierModule,
            lookupModule);
        var groupsNav = registryNav.Groups(
            schedule,
            groupParseContext);

        var quiz = await moodleContext.ScrapeQuizAttempts(quizId);

        Dictionary<Name, float> gradeByName = new(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        foreach (var q in quiz.Attempts)
        {
            var parser = new Parser(q.UserName);
            var name = NameHelper.TryParseName(ref parser);
            if (name is null)
            {
                Console.WriteLine($"{q.UserName} not parsed as name.");
                continue;
            }

            // They go in different order on moodle.
            {
                var f = name.FirstName;
                var l = name.LastName;
                name.FirstName = l;
                name.LastName = f;
            }

            if (q.Grade is not { } grade1)
            {
                Console.WriteLine($"{q.UserName} not graded yet!");
                continue;
            }
            gradeByName[name] = grade1;
        }

        // determine course from path
        var parsedPath = MoodlePathParser.TryParse(quiz.Path.Select(x => x.Name));
        _ = parsedPath;
        if (parsedPath is null)
        {
            throw new InvalidOperationException("Could not parse path");
        }

        var courseId = courseNameUnifierModule.Find(new()
        {
            Lookup = lookupModule,
            CourseName = parsedPath.CourseName,
        });
        var grade = parsedPath.Grade;
        var qualificationType = parsedPath.QualificationType;

        foreach (var course in await coursesNav.Get(semester))
        {
            if (course.CourseId != courseId)
            {
                continue;
            }

            foreach (var group in await groupsNav.Get(course))
            {
                var groupInfo = schedule.Get(group.GroupId);
                if (groupInfo.QualificationType != qualificationType)
                {
                    continue;
                }
                if (groupInfo.Grade != grade)
                {
                    continue;
                }

                var evaluareDoc = await registryNav.GetHtml(group.EvaluationUri);

                // Find anchor with text Testarea X
                IHtmlAnchorElement TestAnchor()
                {
                    var tables = evaluareDoc.QuerySelectorAll<IHtmlAnchorElement>("table a");
                    var matching = tables.Where(x =>
                    {
                        var parser = new Parser(x.TextContent);
                        parser.SkipWhitespace();
                        if (!parser.ConsumeExactString("Testarea"))
                        {
                            return false;
                        }
                        if (!parser.SkipWhitespace().SkippedAny)
                        {
                            return false;
                        }
                        var bparser = parser.BufferedView();
                        if (!bparser.SkipNumbers().SkippedAny)
                        {
                            return false;
                        }

                        var numberSpan = parser.PeekSpanUntilPosition(bparser.Position);
                        var number = int.Parse(numberSpan);
                        if (parsedPath.TestNumber != number)
                        {
                            return false;
                        }

                        return true;
                    });
                    var header = matching.First();
                    return header;
                }

                var testUrl = TestAnchor();
                var test1Doc = await registryNav.GetHtml(new(testUrl.Href));
                var table = test1Doc.QuerySelector<IHtmlTableElement>("table")
                    ?? throw new InvalidOperationException("No table found");
                int nameColumnIndex = FindColumnIndex("Numele");
                int gradeColumnIndex = FindColumnIndex("Nota");

                for (int i = 1; i < table.Rows.Length; i++)
                {
                    var row = table.Rows[i];
                    var nameCell = row.Cells[nameColumnIndex];

                    Name name;
                    {
                        var nameParser = new Parser(nameCell.TextContent);
                        nameParser.SkipWhitespace();
                        name = NameHelper.ParseName(ref nameParser);
                        nameParser.SkipWhitespace();
                        if (nameParser.ConsumeExactString("exmatr"))
                        {
                            continue;
                        }
                        if (!nameParser.IsEmpty)
                        {
                            throw new InvalidOperationException("Extra text after name");
                        }
                    }

                    if (!gradeByName.Remove(name, out float gradeInDb))
                    {
                        Console.WriteLine($"No student in moodle: {name}");
                        continue;
                    }

                    var gradeRounded = (int) Math.Round(gradeInDb);

                    {
                        var gradeCell = row.Cells[gradeColumnIndex];
                        var input = gradeCell.QuerySelector<IHtmlInputElement>("""input[type="text"]""")
                            ?? throw new InvalidOperationException("No input found in grade cell");
                        input.Value = gradeRounded.ToString();
                    }
                }

                var form = test1Doc.QuerySelector<IHtmlFormElement>("form")
                    ?? throw new InvalidOperationException("No form found");
                _ = form;

                // var button = test1Doc.QuerySelector<IHtmlButtonElement>("form > div > div > button")
                //     ?? throw new InvalidOperationException("No submit button found");
                // await button.SubmitAsync();
                await form.SubmitAsync();
                continue;

                int FindColumnIndex(string name)
                {
                    return table.Rows[0].Cells.WithIndex().Where(x =>
                    {
                        var t = x.Item.TextContent.AsSpan().Trim();
                        return t.SequenceEqual(name);
                    }).Single().Index;
                }
            }
        }

        foreach (var (name, value) in gradeByName)
        {
            Console.WriteLine($"Student not found in registry: {name} ({value})");
        }
    }
}


public enum Option
{
    UploadDocsToDrive,
    AllTeachersExcel,
    PerGroupAndPerTeacherPdfs,
    CreateLessonsInRegistry,
    PullCurriculaFromOneDrive,
    FreeRooms,
    FreeHoursOfGroup,
    TableOfAllLabLessons,
    JsonSchedulesForWebsite,
    CopyGradesFromMoodleToRegistry,
}

file sealed class PersonNameLastFirstAlphabeticComparer : IComparer<PersonName>
{
    public static readonly PersonNameLastFirstAlphabeticComparer Instance = new();

    public int Compare(PersonName x, PersonName y)
    {
        {
            var t = IgnoreDiacriticsAndCase_Name_Comparer.Instance.Compare(x.LastName, y.LastName);
            if (t != 0)
            {
                return t;
            }
        }
        var ret = NamePartHelper.CompareEach(
            x.FirstName,
            y.FirstName,
            Comparer.Instance);
        return ret;
    }

    private sealed class Comparer : IComparer<OptionalNamePart>
    {
        // ReSharper disable once MemberHidesStaticFromOuterClass
        public static readonly Comparer Instance = new();

        public int Compare(OptionalNamePart x, OptionalNamePart y)
        {
            return IgnoreDiacriticsAndCaseComparer.Instance.Compare(x.Longer, y.Longer);
        }
    }
}
