using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
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
using MainCli.Helper;
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
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
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
                PrintsGroupNames = true,
            });
            var sb = new StringBuilder();
            for (int teacherId = 0; teacherId < p.Schedule.Teachers.Length; teacherId++)
            {
                int teacherId1 = teacherId;

                var teacherName = p.Schedule.Teachers[teacherId1].PersonName;
                var nameBuilder = new ListStringBuilder(sb, "_");

                {
                    var firstNameBuilder = new ListStringBuilder(sb, NameConstants.DoubleNameSeparator);
                    foreach (var fname in teacherName.FirstName)
                    {
                        if (fname.Short is not { } s)
                        {
                            break;
                        }

                        var w = new Word(s);

                        firstNameBuilder.Append(w.Span.Shortened.Value);
                    }
                }
                nameBuilder.MaybeAppendSeparator();
                {
                    var lastNameBuilder = new ListStringBuilder(sb, NameConstants.DoubleNameSeparator);
                    foreach (var lname in teacherName.LastName)
                    {
                        if (lname is not { } s)
                        {
                            break;
                        }
                        lastNameBuilder.Append(s);
                    }
                }

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
            var periodId = new PeriodId(p.Schedule.Periods.Length - 1);
            var filteredSchedule = p.Schedule.Filter(filter with
            {
                PeriodFilter = new()
                {
                    PeriodId = periodId,
                    UnspecifiedIsAll = true,
                },
            });
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
        var ret = configuration.MaybeGetCredentials();
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

    public readonly struct ParseAttendanceListsExcelParams
    {
        public required FilteredSchedule Schedule { get; init; }
        public required XLWorkbook Workbook { get; init; }
        public required GroupParseContext GroupParseContext { get; init; }
        public required LookupModule LookupModule { get; init; }
        public required CourseNameUnifierModule CourseNames { get; init; }
    }

    private static class NameTokenType
    {
        public const TokenType NamePart = TokenType.Invalid + 1;
    }

    private static readonly TokenTypeLabels _labels =
        LexerHelper.CreateLabelDict(typeof(NameTokenType));

    private sealed class NameTokenReader : ITokenReader
    {
        public static readonly NameTokenReader Instance = new();

        public TokenType Read(ref Parser parser)
        {
            if (parser.SkipWhitespace().SkippedAny)
            {
                return TokenType.Whitespace;
            }
            if (parser.ConsumeExactChar('('))
            {
                return (TokenType) '(';
            }
            if (parser.ConsumeExactChar(')'))
            {
                return (TokenType) ')';
            }
            parser.Skip(new SkipNotWhitespaceOrSep());
            return NameTokenType.NamePart;
        }

        private struct SkipNotWhitespaceOrSep : IShouldSkip
        {
            public bool ShouldSkip(char ch)
            {
                if (ch is '(' or ')')
                {
                    return false;
                }
                if (char.IsWhiteSpace(ch))
                {
                    return false;
                }
                return true;
            }
        }
    }

    private struct Key()
    {
        public CourseId CourseId = CourseId.Invalid;
        public LessonGroups Groups = [];
        public SubGroup SubGroup = SubGroup.All;
        public LessonType LessonType = LessonType.Lab;
    }

    private struct ParsedName
    {
        public required (ParserPosition Start, ParserPosition End)? NameRange;
        public required LessonType LessonType;
        public required SubGroup SubGroup;
        public required Group? Group;
    }

    private static ParsedName ParseName(
        Lexer lexer,
        GroupParseContext groupParser)
    {
        ParserPosition? nameStart = null;
        ParserPosition? nameEnd = null;
        bool isInParens = false;
        var lessonType = LessonType.Lab;
        var subGroup = SubGroup.All;
        Group? group = null;

        while (!lexer.IsEmpty())
        {
            var token = lexer.Peek();
            switch (token.Type)
            {
                case NameTokenType.NamePart:
                {
                    if (isInParens)
                    {
                        if (LessonTypeParser.Instance.Parse(token.Value.Span) is { } lessonType1)
                        {
                            lessonType = lessonType1;
                            break;
                        }
                        throw new InvalidOperationException($"Lesson type {token.Value.Span} is not a valid lesson type");
                    }
                    if (NumberHelper.FromRoman(token.Value.Span) is { } ord)
                    {
                        _ = ord;
                        subGroup = new(token.Value.ToString());
                        break;
                    }
                    if (groupParser.TryParse(token.Value) is { } x)
                    {
                        group = x;
                        break;
                    }

                    if (nameStart == null)
                    {
                        nameStart = token.Span.ColStart;
                    }
                    nameEnd = token.Span.ColEnd;
                    break;
                }
                case (TokenType) '(':
                {
                    isInParens = true;
                    break;
                }
                case (TokenType) ')':
                {
                    isInParens = false;
                    break;
                }
            }
            lexer.Move();
        }

        return new()
        {
            Group = group,
            LessonType = lessonType,
            NameRange = nameStart is null ? null : (nameStart.Value, nameEnd!.Value),
            SubGroup = subGroup,
        };
    }

#pragma warning disable CA1001 // undisposed field
    private struct ParseNameHelper
#pragma warning restore CA1001
    {
        private readonly SingleItemEnumerator<string> _nameE;
        private readonly Lexer _lexer;

        private readonly FilteredSchedule _schedule;
        private readonly CourseNameUnifierModule _courseNames;
        private readonly GroupParseContext _groupParseContext;
        private readonly LookupModule _lookupModule;

        public ParseNameHelper(
            FilteredSchedule schedule,
            CourseNameUnifierModule courseNames,
            GroupParseContext groupParseContext,
            LookupModule lookupModule)
        {
            _nameE = new SingleItemEnumerator<string>();
            _lexer = new Lexer(NameTokenReader.Instance, _labels);
            _schedule = schedule;
            _courseNames = courseNames;
            _groupParseContext = groupParseContext;
            _lookupModule = lookupModule;
        }

        public RegularLesson? LookupLessonByExcelName(string excelName)
        {
            _nameE.Reset(excelName);
            _lexer.Reset(_nameE);

            var parsedName = ParseName(_lexer, _groupParseContext);
            var courseId = CourseId.Invalid;
            if (parsedName.NameRange is { } nameRange)
            {
                var courseName = excelName[nameRange.Start.Index .. nameRange.End.Index];
                if (_courseNames.Find(new()
                    {
                        CourseName = courseName,
                        Lookup = _lookupModule,
                    }) is not { } x)
                {
                    throw new InvalidOperationException($"Course {courseName} not found");
                }
                courseId = x;
            }

            var groups = new LessonGroups();
            if (parsedName.Group is { } group)
            {
                if (!_lookupModule.Groups.TryGetValue(group.Name, out var x))
                {
                    throw new InvalidOperationException($"Group {group.Name} not found");
                }
                groups = [x];
            }

            var key = new Key
            {
                CourseId = courseId,
                Groups = groups,
                LessonType = parsedName.LessonType,
                SubGroup = parsedName.SubGroup,
            };
            return LookupLesson(key, _schedule);
        }

        private static RegularLesson? LookupLesson(
            Key key,
            FilteredSchedule schedule)
        {
            var diffLesson = new RegularLesson
            {
                Date = default,
                Lesson = default,
            };
            var diffMask = new RegularLessonModelDiffMask();
            {
                if (!key.CourseId.IsInvalid)
                {
                    diffLesson.Lesson.Course = key.CourseId;
                    diffMask.Course = true;
                }
            }
            {
                if (key.Groups.Count > 0)
                {
                    diffLesson.Lesson.Groups = key.Groups;
                    diffMask.AllGroups = true;
                }
            }
            {
                diffLesson.Lesson.SubGroup = key.SubGroup;
                diffMask.SubGroup = true;
            }
            {
                diffLesson.Lesson.Type = key.LessonType;
                diffMask.LessonType = true;
            }

            RegularLesson? result = null;
            var resultDiffMask = new RegularLessonModelDiffMask
            {
                LessonType = true,
                SubGroup = true,
                AllGroups = true,
                Course = true,
                AllTeachers = true,
            };

            foreach (var lesson in schedule.Lessons)
            {
                if (schedule.Source.Get(lesson.Lesson.Course).FullName.Contains("Design"))
                {
                    Console.WriteLine("hello");
                }
                if (result != null)
                {
                    var differences = LessonBuilderHelper.Diff(lesson, result, resultDiffMask);
                    if (differences.Intersect(diffMask).TheyDiffer)
                    {
                        continue;
                    }
                    if (differences.TheyDiffer)
                    {
                        throw new InvalidOperationException("Multiple matches to the partial key");
                    }
                    continue;
                }
                {

                    var differences = LessonBuilderHelper.Diff(lesson, diffLesson, diffMask);
                    if (!differences.TheyAreEqual)
                    {
                        continue;
                    }
                    result = lesson;
                }
            }

            return result;
        }
    }

    public static StudentAttendanceList ParseAttendanceListsExcel(ParseAttendanceListsExcelParams p)
    {
        var helper = new ParseNameHelper(
            schedule: p.Schedule,
            courseNames: p.CourseNames,
            groupParseContext: p.GroupParseContext,
            lookupModule: p.LookupModule);

        var builder = new AllStudentAttendanceListBuilder();

        foreach (var sheet in p.Workbook.Worksheets)
        {
            if (sheet.Name.StartsWith("Design Soft"))
            {
                Console.WriteLine("?");
            }
            if (helper.LookupLessonByExcelName(sheet.Name) is not { } lesson)
            {
                throw new InvalidOperationException($"Not found lesson for string {sheet.Name}");
            }

            foreach (var group in lesson.Lesson.Groups)
            {
                var list = builder.List(new()
                {
                    CourseId = lesson.Lesson.Course,
                    GroupId = group,
                    SubGroup = lesson.Lesson.SubGroup,
                });

                foreach (var row in sheet.Rows())
                {
                    using var cells = row.Cells().GetEnumerator();
                    if (!cells.MoveNext())
                    {
                        break;
                    }

                    if (!cells.Current!.TryGetValue(out string value))
                    {
                        break;
                    }
                    var parser = new Parser(value);
                    if (NameHelper.TryParseName(ref parser) is not { } name)
                    {
                        break;
                    }

                    var student = list.Student(name);

                    while (cells.MoveNext())
                    {
                        if (!cells.Current!.TryGetValue(out string attendanceStr))
                        {
                            throw new InvalidOperationException("Expecting a string in cell");
                        }
                        var attendance = AttendanceHelper.Parse(attendanceStr);
                        if (attendance == Attendance.Grade
                            || attendance == Attendance.None)
                        {
                            throw new InvalidOperationException($"Expecting either empty or 'a' or 'na', got '{attendanceStr}'");
                        }

                        student.Day(attendance);
                    }

                }
            }

        }
        var ret = builder.Build();
        return ret;
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
            ScheduleSerializer.ConvertWithLookup(
                context.Schedule,
                scheduleSerializedModel,
                context.CourseNameUnifierModule);

            beforeEndAction(context);
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
}

public enum Option
{
    UploadDocsToDrive,
    AllTeachersExcel,
    PerGroupAndPerTeacherPdfs,
    CreateLessonsInRegistry,
    PullCurriculaFromOneDrive,
    FreeRooms,
    CuteTeachersExcel,
    FreeHoursOfGroup,
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
        public static readonly Comparer Instance = new();
        public int Compare(OptionalNamePart x, OptionalNamePart y)
        {
            return IgnoreDiacriticsAndCaseComparer.Instance.Compare(x.Longer, y.Longer);
        }
    }
}
