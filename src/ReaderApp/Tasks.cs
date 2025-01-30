using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
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
using RunProperties = DocumentFormat.OpenXml.Wordprocessing.RunProperties;

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

    public static void GenerateAllTeacherExcel(AllTeacherExcelParams p)
    {
        using var stream = File.Open(p.OutputFilePath, FileMode.Create, FileAccess.ReadWrite);
        using var excel = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.Workbook, autoSave: true);

        var workbookPart = excel.AddWorkbookPart();
        var workbook = new Workbook();
        var sheetData = new SheetData();
        var worksheet = new Worksheet(sheetData);

        InitExcelBasics();

        var strings = StringTableBuilder.Create(workbookPart);

        ConfigureFrozenViews();
        ConfigureWidths();
        ConfigureMerges();

        var styles = ConfigureStylesheet(workbookPart);

        var cells = new CellsBuilder(sheetData);
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
                cell.SetStringValue($"{new Spaces(7)}Profesor\n{new Spaces(3)}Ora");
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
}
