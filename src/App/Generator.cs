using QuestPDF.Fluent;
using QuestPDF.Infrastructure;

namespace App;

public sealed class ScheduleTableDocument : IDocument
{
    public struct Params
    {
        public required LessonTimeConfig LessonTimeConfig;
        public required FilteredSchedule Schedule;
        public required DayNameProvider DayNameProvider;
        public required TimeSlotDisplayHandler TimeSlotDisplay;
    }

    private readonly Params _params;
    private readonly GeneratorCache _cache;

    public ScheduleTableDocument(Params p)
    {
        _params = p;
        _cache = GeneratorCache.Create(_params.Schedule);
    }

    private GroupId[] Groups() => _params.Schedule.Groups;
    private TimeSlot[] TimeSlots() => _params.Schedule.TimeSlots;
    private DayOfWeek[] Days() => _params.Schedule.Days;
    private const int maxGroupsPerPage = 5;

    public void Compose(IDocumentContainer container)
    {
        for (int i = 0; i < Groups().Length; i += maxGroupsPerPage)
        {
            ArraySegment<GroupId> CurrentGroups()
            {
                var groups = Groups();
                // ReSharper disable once AccessToModifiedClosure
                var count = int.Min(groups.Length - i, maxGroupsPerPage);
                // ReSharper disable once AccessToModifiedClosure
                var ret = new ArraySegment<GroupId>(groups, i, count);
                return ret;
            }

            container.Page(page =>
            {
                page.Margin(50);

                var content = page.Content();
                content.Table(table =>
                {
                    table.Header(h =>
                    {
                        var corner = h.Cell();
                        _ = corner;
                        corner.Column(1);

                        var time = h.Cell();
                        _ = time;
                        time.Column(2);

                        var ids = CurrentGroups();
                        for (uint groupIdIndex = 0; groupIdIndex < ids.Count; groupIdIndex++)
                        {
                            var groupId = ids[(int) groupIdIndex];
                            var g = new GroupAccessor(_params.Schedule.Source, groupId);
                            var nameCell = h.Cell();
                            nameCell.Text($"{g.Ref.Name}({g.Ref.Language.GetName()})");
                            nameCell.Column(2 + groupIdIndex);
                        }
                    });

                    TableBody(table, CurrentGroups());
                });
            });
        }
    }

    private uint GetLessonCol(int minOrder)
    {
        var ret = minOrder % maxGroupsPerPage;
        // day and time columns
        ret += 2;
        return (uint) ret;
    }

    private void TableBody(TableDescriptor table, ArraySegment<GroupId> currentGroups)
    {
        TableLeftHeader(table);

        var days = Days();
        uint timeSlotCount = (uint) TimeSlots().Length;
        uint cellsPerTimeSlot = (uint) _cache.MaxRowsInOneCell;
        for (uint dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            var day = days[dayIndex];
            uint dayRowIndex = timeSlotCount * dayIndex * cellsPerTimeSlot;

            var timeSlots = TimeSlots();
            for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
            {
                var timeSlot = timeSlots[timeSlotIndex];
                var rowStartIndex = dayRowIndex + timeSlotIndex * cellsPerTimeSlot;

                for (int currentGroupIndex = 0; currentGroupIndex < currentGroups.Count; currentGroupIndex++)
                {
                    var dayKey = new DayKey
                    {
                        TimeSlot = timeSlot,
                        DayOfWeek = day,
                    };

                    var groupId = currentGroups[currentGroupIndex];

                    var allKey = dayKey.AllKey(groupId);

                    if (!_cache.Dicts.MappingByAll.TryGetValue(allKey, out var lessons))
                    {
                        continue;
                    }

                    if (_cache.Dicts.IsSeparatedLayout)
                    {
                        var cell = table.Cell();
                        cell.ColumnSpan(1);
                        cell.RowSpan(cellsPerTimeSlot);

                        {
                            var col = GetLessonCol(_cache.Dicts.GroupOrder[groupId]);
                            cell.Column(col);
                        }

                        cell.Text(text =>
                        {
                            for (int index = 0; index < lessons.Count; index++)
                            {
                                var lesson = lessons[index];
                                // Temporary.
                                text.Line(lesson.Lesson.Course.Name);
                            }
                        });
                    }
                    else
                    {
                        foreach (var lesson in lessons)
                        {
                            var lessonOrder = _cache.Dicts.LessonVerticalOrder![lesson];
                            {
                                var key = (lessonOrder, allKey);
                                // We're not the cell that defines this.
                                if (_cache.Dicts.SharedCellStart!.Contains(key))
                                {
                                    continue;
                                }
                            }
                            {
                                uint size = (uint) lesson.Lesson.Groups.Count;
                                var cell = table.Cell();
                                cell.RowSpan(1);
                                cell.ColumnSpan(size);
                                cell.Row(rowStartIndex + (uint) lessonOrder);

                                {
                                    var col = GetLessonCol(_cache.Dicts.GroupOrder[groupId]);
                                    cell.Column(col);
                                }

                                cell.Text(text =>
                                {
                                    text.Span(lesson.Lesson.Course.Name);
                                });
                            }
                        }
                    }
                }
            }
        }
    }

    private void TableLeftHeader(TableDescriptor table)
    {
        var days = Days();
        uint timeSlotCount = (uint) TimeSlots().Length;
        for (uint dayIndex = 0; dayIndex < days.Length; dayIndex++)
        {
            // TODO: handle overlaps (do two-per or a dynamic N-per)
            var dayRowIndex = timeSlotCount * dayIndex;

            {
                var weekDay = days[dayIndex];
                var dayCell = table.Cell();
                dayCell.RowSpan(timeSlotCount);
                dayCell.Column(1);
                dayCell.Row(dayRowIndex);

                // DOESN'T WORK!
                // Can't use table because it's not possible to rotate text!
                // Gotta do it manually!

                // var textContainer = dayCell.Container();
                // textContainer.RotateLeft();
                dayCell.Text(text =>
                {
                    var weekDayText = _params.DayNameProvider.GetDayName(weekDay);
                    text.Span(weekDayText);
                    text.DefaultTextStyle(style => style.Bold());
                });
            }

            {
                var timeSlots = TimeSlots();
                for (uint timeSlotIndex = 0; timeSlotIndex < timeSlots.Length; timeSlotIndex++)
                {
                    var timeCell = table.Cell();
                    timeCell.AlignCenter();
                    timeCell.Column(2);
                    timeCell.Row(dayRowIndex + timeSlotIndex);

                    // ReSharper disable once AccessToModifiedClosure
                    timeCell.Text(x => TimeSlotTextBox(x, timeSlotIndex));
                }
            }

        }
    }

    private void TimeSlotTextBox(TextDescriptor text, uint timeSlotIndex)
    {
        // This is deliberately not passed, because the formatting might depend on the index.
        var timeSlot = TimeSlots()[timeSlotIndex];

        {
            var t = _params.TimeSlotDisplay.IndexDisplay(timeSlot.Index + 1);
            text.Line(t);
        }

        {
            var interval = _params.LessonTimeConfig.GetTimeSlotInterval(timeSlot);
            var formattedInterval = _params.TimeSlotDisplay.IntervalDisplay(interval);
            text.Line(formattedInterval);
        }
    }
}
