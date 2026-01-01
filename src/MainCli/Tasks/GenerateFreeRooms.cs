using AutoConstructor.Attributes;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using MainCli.ExcelBuilder;
using ScheduleLib;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using SpreadCheetah;

namespace MainCli;

[AutoConstructor]
public sealed partial class GenerateFreeRoomsTaskHandler
{
    private readonly Schedule _schedule;
    private readonly ParityDisplayHandler _parityDisplay;
    private readonly TimeSlotDisplayHandler _timeSlotDisplay;
    private readonly DayNameProvider _dayNameProvider;
    private readonly LessonTimeConfig _timeConfig;

    public readonly struct RunParams
    {
        public required CancellationToken CancellationToken { get; init; }
        public required Stream OutputStream { get; init; }
    }

    public async ValueTask Run(RunParams p)
    {
        var rooms = PreprocessedRooms(_schedule.EnumerateWeeklyLessons());
        var allRooms = rooms.Distinct().ToHashSet();

        await using var spreadsheet = await Spreadsheet.CreateNewAsync(
            p.OutputStream,
            cancellationToken: p.CancellationToken);
        var headerStyle = spreadsheet.AddStyle(new()
        {
        });

        Parity[] parities = [Parity.EvenWeek, Parity.OddWeek];
        List<DataCell> dataCells = new();

        foreach (var parity in parities)
        {
            string parityLabel = _parityDisplay.Get(parity)!;
            await spreadsheet.StartWorksheetAsync(parityLabel, new()
            {
            }, p.CancellationToken);

            foreach (var day in new AllEnumEnumerable<DayOfWeek>())
            {
                var lessonsThisDay = _schedule.EnumerateWeeklyLessons()
                    .Where(x => x.Date.DayOfWeek == day)
                    .ToArray();
                if (lessonsThisDay.Length == 0)
                {
                    continue;
                }

                var dayName = _dayNameProvider.GetDayName(day);
                await spreadsheet.AddHeaderRowAsync([dayName], headerStyle, p.CancellationToken);

                foreach (var timeSlot in _timeConfig.TimeSlots)
                {
                    var interval = _timeConfig.GetTimeSlotInterval(timeSlot);
                    var intervalText = _timeSlotDisplay.IntervalDisplay(interval);
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

        IEnumerable<RoomId> PreprocessedRooms(IEnumerable<WeeklyLessonAccessor> lessons)
        {
            var preprocessed = lessons
                .Select(x => x.Lesson.Room)
                .Select(S)
                .Where(x => x.IsValid);
            return preprocessed;
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
