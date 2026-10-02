using ScheduleLib.Import.Pdf;

using ScheduleLib.Parsing;
using ScheduleLib;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.Lesson;
using ScheduleLib.Parsing.WordDoc;
using ScheduleLib.ScheduleDefaults;

namespace FmiScheduleImport;

// Temporary adaptation of course-specific cohorts to the existing alternatives
// dimension. Common lectures are copied into each cohort, independently of the
// numeric laboratory subgroups and UI/GA2D specializations.
public sealed class ElectiveCohorts
{
    private readonly ScheduleImportContext _context;
    private readonly Dictionary<GroupId, HashSet<Alternative>> _byGroup = new();
    private readonly CourseId _course;
    private static readonly Alternative BaseAlternative = new("Antreprenoriat inovativ");

    public ElectiveCohorts(ScheduleImportContext context, IEnumerable<PdfScheduleCell> cells)
    {
        _context = context;
        _course = context.GetOrAddCourse("Antreprenoriat inovativ".AsMemory());
        foreach (var cell in cells.Where(c => c.Alternative is not null))
        {
            foreach (var group in cell.Groups)
            {
                var id = context.Schedule.Group(group).Id;
                if (!_byGroup.TryGetValue(id, out var alternatives))
                {
                    alternatives = [];
                    _byGroup.Add(id, alternatives);
                }
                alternatives.Add(new(cell.Alternative!));
            }
        }
        var config = new ImplicitSplitConfigBuilder();
        foreach (var scope in Config.ImplicitSplitConfig.Scopes)
        {
            config.Scope(copy =>
            {
                copy.StudyYear = scope.StudyYear;
                copy.Grade = scope.Grade;
                copy.Faculty = scope.Faculty;
                copy.AttendanceMode = scope.AttendanceMode;
                copy.Qualification = scope.Qualification;
                foreach (var (name, value) in scope.SpecializationCourses)
                {
                    copy.Specialization(name, value);
                }
                foreach (var (name, value) in scope.AlternativeCourses)
                {
                    if (value != BaseAlternative)
                    {
                        copy.Alternative(name, value);
                    }
                }
            });
        }
        SplitConfig = config.Build();
    }

    public ImplicitSplitConfig SplitConfig { get; }

    public void Add(ParsedLesson lesson, DayOfWeek day, TimeSlot slot,
        IReadOnlyList<GroupId> groups, string? cohort)
    {
        if (_context.GetOrAddCourse(lesson.LessonName) != _course)
        {
            if (cohort is not null)
            {
                throw new FormatException("Cohort annotation on an unexpected course.");
            }
            _context.AddOrMergeLesson(lesson, day, slot, groups);
            return;
        }
        if (cohort is not null)
        {
            _context.AddOrMergeLesson(lesson, day, slot, groups, new(cohort));
            return;
        }
        foreach (var group in groups)
        {
            if (_byGroup.TryGetValue(group, out var alternatives))
            {
                foreach (var alternative in alternatives)
                {
                    _context.AddOrMergeLesson(lesson, day, slot, [group], alternative);
                }
            }
            else
            {
                _context.AddOrMergeLesson(lesson, day, slot, [group], BaseAlternative);
            }
        }
    }
}
