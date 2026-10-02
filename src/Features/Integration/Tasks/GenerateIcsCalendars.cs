using System.Collections.Immutable;
using System.Text;
using AutoConstructor.Attributes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Dates;
using ScheduleLib.Generation;
using ScheduleLib.Generation.Ics;

namespace ScheduleLib.Application.Core;

/// <summary>
/// Generates an .ics calendar file per group, per group-partition combination
/// and per teacher, mirroring the PDF set produced by
/// <see cref="GeneratePdfsForGroupsAndTeachersTaskHandler"/>: same filters
/// (latest period, weekly lessons only) and the same file names with the .ics
/// extension.
/// </summary>
/// <remarks>
/// Each weekly lesson is expanded to concrete VEVENTs for the whole semester
/// (no RRULE), so importers need no recurrence support. The text mirrors the
/// PDF cell: the partition prefix and course name with the lesson type in the
/// summary, teacher names (group calendars) or group names (teacher calendars)
/// in the description, and the room as the location. Parity is not printed:
/// concrete dates already encode it.
/// </remarks>
[AutoConstructor]
public sealed partial class GenerateIcsCalendarsTaskHandler
{
    private readonly ScheduledTimeEventsProvider _eventsProvider;
    private readonly LessonTypeDisplayHandler _lessonTypeDisplay;
    private readonly SpecializationRegistry _specializationRegistry;
    private readonly IOptions<StudyYearOptions> _studyYearOptions;
    private readonly Schedule _schedule;
    private readonly ILogger _logger;

    public readonly struct RunParams
    {
        public required OutputDirectory OutputDirectory { get; init; }
    }

    public void Run(RunParams p)
    {
        var semester = _studyYearOptions.Value.Semester;
        var partitionInfoByGroup = _schedule.GetGroupPartitionInfo(_specializationRegistry);

        foreach (var g in _schedule.EnumerateGroups())
        {
            var calendarName = GroupCalendarName(g.Item, semester);
            WriteGroupCalendar($"{g.Item.Name}.ics", calendarName, new()
            {
                OneOfGroupIds = [g.Id],
            });

            foreach (var combination in partitionInfoByGroup[g.Id].Combinations)
            {
                var sb = new StringBuilder();
                sb.Append(g.Item.Name);
                sb.Append('_');
                combination.AppendFileNamePart(new ListStringBuilder(sb, "-"));
                sb.Append(".ics");
                var fileName = sb.ToStringAndClear();

                WriteGroupCalendar(fileName, calendarName, combination.ToGroupFilter(g.Id));
            }
        }

        var sb1 = new StringBuilder();
        for (int teacherId = 0; teacherId < _schedule.Teachers.Length; teacherId++)
        {
            var teacher = _schedule.Teachers[teacherId];
            TeacherNameHelper.AsFileName(sb1, teacher.PersonName);
            sb1.Append(".ics");
            var fileName = sb1.ToStringAndClear();

            WriteCalendar(fileName, TeacherCalendarName(teacher), isTeacherCalendar: true, new()
            {
                TeacherFilter = new()
                {
                    IncludeIds = [new TeacherId(teacherId)],
                },
            });
        }
        return;

        void WriteGroupCalendar(string fileName, string calendarName, in GroupFilter groupFilter)
        {
            WriteCalendar(fileName, calendarName, isTeacherCalendar: false, new()
            {
                GroupFilter = groupFilter,
            });
        }

        void WriteCalendar(string fileName, string calendarName, bool isTeacherCalendar, in ScheduleFilter filter)
        {
            var filteredSchedule = _schedule.Filter(
                filter
                    .WithLatestPeriod(_schedule)
                    .WithLessonRegularity(LessonRegularity.Weekly));
            if (filteredSchedule.IsEmpty)
            {
                return;
            }

            List<IcsEvent> events;
            try
            {
                events = BuildEvents(filteredSchedule, isTeacherCalendar);
            }
            catch (MissingSemesterDateRangeException e)
            {
                // A group's attendance mode/grade has no semester dates
                // configured (e.g. a new Dual program): skip the calendar
                // instead of failing the whole run. Add the missing range to
                // ScheduleDefaults.SemesterIntervalProvider to cover it.
                _logger.LogWarning(e, "Skipping calendar {File}: {Reason}", fileName, e.Message);
                return;
            }
            if (events.Count == 0)
            {
                return;
            }

            var calendar = new IcsCalendar
            {
                Name = calendarName,
                Events = events,
            };
            using var outputFile = p.OutputDirectory.OpenFile(fileName, FileMode.Create, FileAccess.Write);
            IcsCalendarWriter.Write(outputFile, calendar);
        }

        List<IcsEvent> BuildEvents(FilteredSchedule filteredSchedule, bool isTeacherCalendar)
        {
            var events = new List<IcsEvent>();
            var timeEvents = _eventsProvider.Get(new()
            {
                Semester = semester,
                Lessons = filteredSchedule.Lessons,
            });
            foreach (var timeEvent in timeEvents)
            {
                var lesson = filteredSchedule.Source.Get(timeEvent.LessonId);
                var (summary, description) = LessonText(lesson, isTeacherCalendar);

                string? location = null;
                if (lesson.Lesson.Room.IsValid)
                {
                    location = _schedule.Get(lesson.Lesson.Room);
                }

                foreach (var date in timeEvent.Event)
                {
                    events.Add(new()
                    {
                        Summary = summary,
                        Description = description,
                        Location = location,
                        Start = new DateTime(date, timeEvent.TimeInterval.Start),
                        End = new DateTime(date, timeEvent.TimeInterval.End),
                    });
                }
            }
            events.Sort(static (a, b) => a.Start.CompareTo(b.Start));
            return events;
        }

        (string Summary, string? Description) LessonText(AnyLessonAccessor lesson, bool isTeacherCalendar)
        {
            var course = _schedule.Get(lesson.Lesson.Course);

            var sb = new StringBuilder();
            string? prefix = lesson.Lesson.GroupPartitionKey.ToDisplayString();
            if (prefix is { } partitionPrefix)
            {
                sb.Append(partitionPrefix);
                sb.Append(": ");
            }
            sb.Append(course.FullName);
            if (_lessonTypeDisplay.Get(lesson.Lesson.Type) is { } lessonType)
            {
                sb.Append(" (");
                sb.Append(lessonType);
                sb.Append(')');
            }
            var summary = sb.ToStringAndClear();

            // Like the PDFs: a group calendar names the teachers, a teacher
            // calendar names the groups attending.
            string? description = null;
            if (isTeacherCalendar)
            {
                if (lesson.Lesson.Groups.Count > 0)
                {
                    description = JoinGroupNames(lesson.Lesson.Groups);
                }
            }
            else if (lesson.Lesson.Teachers.Length > 0)
            {
                description = JoinTeacherNames(lesson.Lesson.Teachers);
            }

            return (summary, description);
        }

        string JoinTeacherNames(ImmutableArray<TeacherId> teacherIds)
        {
            var sb = new StringBuilder();
            bool added = false;
            foreach (var teacherId in teacherIds)
            {
                if (added)
                {
                    sb.Append(", ");
                }
                NameDisplayHelper.Append(new()
                {
                    Output = sb,
                    Name = _schedule.Get(teacherId).PersonName,
                    LastNameFirst = false,
                    InsertSpaceAfterShortName = false,
                    PreferLonger = false,
                });
                added = true;
            }
            return sb.ToStringAndClear();
        }

        string JoinGroupNames(LessonGroups groupIds)
        {
            var sb = new StringBuilder();
            bool added = false;
            foreach (var groupId in groupIds)
            {
                if (added)
                {
                    sb.Append(", ");
                }
                sb.Append(_schedule.Get(groupId).Name);
                added = true;
            }
            return sb.ToStringAndClear();
        }
    }

    /// <summary>
    /// The calendar display name for a group: the semester number counts from
    /// the start of the group's study, not from the schedule's current year.
    /// </summary>
    public static string GroupCalendarName(Group group, Semester semester)
    {
        return $"Orar {group.Name} Sem {SemesterNumber(group.Grade, semester)}";
    }

    public static string TeacherCalendarName(Teacher teacher)
    {
        return $"Orar {teacher.PersonName}";
    }

    private static int SemesterNumber(Grade grade, Semester semester)
    {
        int sem1 = grade.Value * 2 - 1;
        return semester == Semester.Sem1 ? sem1 : sem1 + 1;
    }
}
