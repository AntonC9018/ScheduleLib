using System.Diagnostics;
using System.Text;
using ScheduleLib.Builders;
using ScheduleLib.Generation;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.Parsing;

public enum SubGroupStatus
{
    GroupNameIsSubGroup,
    SetFromSubGroup,
}

public sealed class ScheduleImportContext
{
    public IEnumerable<ParsedLesson> ParseLessons(IEnumerable<ReadOnlyMemory<char>> lines)
    {
        using var enumerator = lines.GetEnumerator();
        var parser = ParserFactory.Create();
        parser.Lexer.Reset(enumerator);
        foreach (var lesson in parser.ParseLessons(new StringBuilder()))
            yield return lesson;
    }
    public ScheduleBuilder Schedule { get; }
    public LessonTimeConfig TimeConfig { get; }
    public DayNameParser DayNameParser { get; }
    public CourseNameUnifierModule CourseNameUnifierModule { get; }
    public LessonParserFactory ParserFactory { get; }
    public SubGroupPrefixMatcher SubGroupMatcher { get; }
    public PeriodId CurrentPeriodId { get; private set; } = PeriodId.Unspecified;

    public ScheduleImportContext(
        CourseNameUnifierModule courseNameUnifierModule,
        DayNameParser dayNameParser,
        LessonParserFactory parserFactory,
        ScheduleBuilder schedule,
        LessonTimeConfig timeConfig,
        SpecializationRegistry specializationRegistry,
        SubGroupPrefixMatcher? subGroupMatcher = null)
    {
        CourseNameUnifierModule = courseNameUnifierModule;
        DayNameParser = dayNameParser;
        ParserFactory = parserFactory;
        Schedule = schedule;
        TimeConfig = timeConfig;
        Schedule.SpecializationRegistry = specializationRegistry;
        SubGroupMatcher = subGroupMatcher ?? SubGroupPrefixMatcher.Default;
    }


    public void SetPeriod(PeriodBeginning? period)
    {
        if (period is { } per)
        {
            CurrentPeriodId = Period(per.StartDate);
        }
        else
        {
            CurrentPeriodId = PeriodId.Unspecified;
        }
    }

    public struct CreateParams
    {
        public required DayNameProvider DayNameProvider;
        public required CourseNameUnifierConfig CourseNameUnifierConfig;
        public required LessonParserFactory ParserFactory;
        public SpecializationRegistry? SpecializationRegistry;
        public SubGroupPrefixMatcher? SubGroupMatcher;
    }

    public static ScheduleImportContext Create(CreateParams p)
    {
        var s = new ScheduleBuilder
        {
            ValidationSettings = new()
            {
                SubGroup = SubGroupValidationMode.PossiblyRegisterSubGroup,
            },
        };
        s.EnableLookupModule();
        var timeConfig = LessonTimeConfig.CreateDefault();

        return new(
            schedule: s,
            timeConfig: timeConfig,
            courseNameUnifierModule: new(p.CourseNameUnifierConfig),
            dayNameParser: new DayNameParser(p.DayNameProvider),
            parserFactory: p.ParserFactory,
            specializationRegistry: p.SpecializationRegistry ?? SpecializationRegistry.Empty,
            subGroupMatcher: p.SubGroupMatcher ?? SubGroupPrefixMatcher.Default);
    }

    public SubGroupStatus SetCommonProps(
        ILessonBuilder<ILessonBuilderModel> builder,
        in ParsedLesson parsedLesson)
    {
        var courseId = GetOrAddCourse(parsedLesson.LessonName);
        builder.Course(courseId);

        if (!parsedLesson.RoomName.IsEmpty)
        {
            var roomId = GetOrAddRoom(parsedLesson.RoomName.ToString());
            builder.Room(roomId);
        }

        foreach (var tname in parsedLesson.TeacherNames)
        {
            var teacherId = GetOrAddTeacher(tname);
            builder.Teacher(teacherId);
        }

        builder.Type(parsedLesson.LessonType);
        builder.Period(CurrentPeriodId);

        // Classify the raw labels now that the lesson's groups are known.
        // At most one subgroup and one specialization may be assigned to a lesson.
        SubGroup? subGroup = null;
        Specialization? specialization = null;
        bool groupNameIsLabel = false;

        if (!parsedLesson.GroupName.IsEmpty)
        {
            LessonParsingHelper.RejectExplicitNonBeginners(parsedLesson.GroupName.Span);
            if (SubGroupMatcher.TryFromNamePrefix(parsedLesson.GroupName.Span, out var group))
            {
                groupNameIsLabel = true;
                var remapped = Schedule.RemapSubGroup(group);
                if (Schedule.TryGetSpecialization(remapped, out var spec))
                {
                    specialization = spec;
                }
                else
                {
                    subGroup = remapped;
                }
            }
            else
            {
                // Future specialization labels are registered by their complete
                // name, not by the built-in abbreviation table.
                var remapped = Schedule.RemapSubGroup(new(parsedLesson.GroupName.ToString()));
                if (Schedule.TryGetSpecialization(remapped, out var spec))
                {
                    groupNameIsLabel = true;
                    specialization = spec;
                }
            }
        }

        if (!parsedLesson.PartitionHint.IsEmpty)
        {
            LessonParsingHelper.RejectExplicitNonBeginners(parsedLesson.PartitionHint.Span);
            var remapped = Schedule.RemapSubGroup(new(parsedLesson.PartitionHint.ToString()));
            if (Schedule.TryGetSpecialization(remapped, out var spec))
            {
                if (specialization is { } prevSpec)
                {
                    throw ConflictingSpecializationException.ForConflictingValues(prevSpec.Value, spec.Value);
                }
                specialization = spec;
            }
            else
            {
                var label = remapped;
                if (subGroup is { } prevSub)
                {
                    throw ConflictingSubGroupException.ForConflictingValues(prevSub.Value, label.Value);
                }
                subGroup = label;
            }
        }

        if (subGroup is { } s)
        {
            builder.SubGroup(s);
        }
        if (specialization is { } sp)
        {
            builder.Specialization(sp);
        }

        return groupNameIsLabel
            ? SubGroupStatus.GroupNameIsSubGroup
            : SubGroupStatus.SetFromSubGroup;
    }

    public CourseId GetOrAddCourse(ReadOnlyMemory<char> name)
    {
        var ret = CourseNameUnifierModule.FindOrAdd(new()
        {
            Schedule = Schedule,
            CourseName = name,
            ParseOptions = new()
            {
                // Commas are allowed in course names now, apparently.
                IgnorePunctuation = true,
            },
        });
        return ret;
    }

    public TeacherId GetOrAddTeacher(TeacherName name)
    {
        var nameModel = new TeacherBuilderModel.NameModel
        {
            FirstName = name.FirstName.Map(x =>
            {
                var ret = default(OptionalNamePart);
                if (x.IsEmpty)
                {
                    return ret;
                }
                var w = new WordSpan(x.Span);
                if (w.LooksFull)
                {
                    ret.Full = x.ToString();
                    return ret;
                }
                else
                {
                    ret.Short = x.ToString();
                    return ret;
                }
            }),
            LastName = new(name.LastName.Map(x =>
            {
                if (x.IsEmpty)
                {
                    return null;
                }
                return x.ToString();
            })),
        };

        // Need to remap explicitly, because we do the check for diacritics later.
        _ = Schedule.RemapTeacherName(ref nameModel);

        var teacherBuilder = Schedule.Teacher(nameModel);
        var teacher = teacherBuilder.Model;

        var before = teacher.Name.LastName;
        _ = before;

        teacher.Name.LastName.Parts.Update(
            nameModel.LastName.Parts, (a, b) =>
            {
                if (a == null || b == null)
                {
                    return a ?? b;
                }
                var ret = DiacriticsHelper.SelectWithDiacritics(a, b);
                return ret;
            });

        return teacherBuilder.Id;
    }

    public RoomId GetOrAddRoom(string name) => Schedule.Room(name);

    private PeriodId Period(DateOnly start)
    {
        // Currently, assume that periods are going to be ordered.
        var periods = Schedule.Periods.List;
        Debug.Assert(periods.IsSorted(x => x.Start));

        if (periods.Count == 0)
        {
            return CreatePeriod();
        }

        var lastPeriod = periods[^1];
        if (lastPeriod.Start == start)
        {
            OutOfOrderCheck(periods.SkipLast(1), start);
            return new(periods.Count - 1);
        }

        OutOfOrderCheck(periods, start);

        // Maybe want to encapsulate this more, use the builder?
        Debug.Assert(lastPeriod.EndExclusive == default);
        lastPeriod.EndExclusive = start;
        return CreatePeriod();

        [Conditional("DEBUG")]
        static void OutOfOrderCheck(IEnumerable<PeriodBuilderModel> periods, DateOnly start)
        {
            Debug.Assert(periods.All(x => x.Start < start), "Out of order periods not implemented");
        }

        PeriodId CreatePeriod()
        {
            var ret = Schedule.Period(start);
            return ret;
        }
    }

    /// <summary>Adds a parsed cell using the same group, time and merge rules for every document format.</summary>
    public void AddOrMergeLesson(
        in ParsedLesson lesson,
        DayOfWeek day,
        TimeSlot timeSlot,
        IReadOnlyList<GroupId> groups,
        Alternative alternative = default)
    {
        var builder = Schedule.DetachedRegularLesson();
        builder.TimeSlot(lesson.StartTime is { } startTime
            ? new(FindTimeSlotIndex(startTime))
            : timeSlot);
        builder.DayOfWeek(day);
        builder.Parity(lesson.Parity);
        builder.Alternative(alternative);

        bool groupNameHandledAsSubgroup = SetCommonProps(builder, lesson) == SubGroupStatus.GroupNameIsSubGroup;
        if (lesson.GroupName.IsEmpty || groupNameHandledAsSubgroup)
        {
            builder.Groups([.. groups]);
        }
        else
        {
            builder.Group(Schedule.Group(lesson.GroupName.ToString()));
        }

        if (MaybeMergeIntoAnExistingLesson())
        {
            return;
        }

        builder.Attach();
        return;

        bool MaybeMergeIntoAnExistingLesson()
        {
            var schedule = Schedule;
            var lessonsByCourse = schedule.LookupModule!.LessonsByCourse;
            var courseId = builder.Model.General.Course!.Value;
            var existingLessonsOfThisCourse = lessonsByCourse[courseId];

            foreach (var existingLesson in existingLessonsOfThisCourse)
            {
                var model = schedule.WeeklyLessons.Ref(existingLesson.Id);

                var diffMask = new LessonModelDiffMask
                {
                    Parity = true,
                    Day = true,
                    TimeSlot = true,
                    SubGroup = true,
                    Room = true,
                    LessonType = true,
                    Period = true,
                    // Already checked because we look up by it.
                    // Course = true,
                };
                var diff = LessonBuilderHelper.Diff(
                    builder.Model.Data,
                    model.Data,
                    diffMask);
                if (diff.TheyDiffer)
                {
                    continue;
                }

                LessonBuilderHelper.Merge(
                    to: ref model.Data,
                    from: builder.Model.Data,
                    new()
                    {
                        Groups = true,
                        Teachers = true,
                    });
                return true;
            }
            return false;
        }
    }

    internal int FindTimeSlotIndex(TimeOnly start)
    {
        var timeSlot = TimeConfig.FindTimeSlotByStartTime(start);
        if (timeSlot is not { } v)
        {
            throw InvalidScheduleDocumentException.ForUnknownTimeSlot(start);
        }
        return v.Index;
    }
}

// TODO: Read the whole table once to find these first.
public sealed class DayNameParser(DayNameProvider p)
{
    private readonly Dictionary<string, DayOfWeek> _days = CreateMappings(p);

    public DayOfWeek? Map(ReadOnlySpan<char> s)
    {
        if (_days.GetAlternateLookup<ReadOnlySpan<char>>().TryGetValue(s, out var day))
        {
            return day;
        }
        return null;
    }

    private static Dictionary<string, DayOfWeek> CreateMappings(DayNameProvider p)
    {
        var ret = new Dictionary<string, DayOfWeek>(IgnoreDiacriticsAndCaseComparer.Instance);
        for (int index = 0; index < p.Names.Length; index++)
        {
            string name = p.Names[index];
            ret.Add(name, (DayOfWeek) index);

            if (RomanianLanguageHelper.VariantWithOldLetter(name) is { } x)
            {
                ret.Add(x, (DayOfWeek) index);
            }
        }
        return ret;
    }
}

public static class RomanianLanguageHelper
{
    // Romanian special case: â -> î in the middle of the word.
    // This is surprisingly common even though it's wrong.
    public static string? VariantWithOldLetter(string name)
    {
        var middle = name.AsSpan()[1 .. ^1];
        if (!middle.Contains('â'))
        {
            return null;
        }

        return string.Create(name.Length, middle, (output, middle) =>
        {
            Debug.Assert(name.Length <= 256);
            {
                middle.Replace(output[1 .. ^1], 'â', 'î');
            }
            output[0] = name[0];
            output[^1] = name[^1];
        });
    }
}

public struct PeriodBeginning
{
    public required DateOnly StartDate { get; init; }
}
