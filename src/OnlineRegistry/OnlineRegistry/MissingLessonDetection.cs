using System.Diagnostics;
using ScheduleLib.Builders;
using ScheduleLib.Helper;

namespace ScheduleLib.OnlineRegistry;

public readonly struct GetDateTimesOfScheduledLessonsParams
{
    public required IEnumerable<RegularLessonId> Lessons { get; init; }
    public required Schedule Schedule { get; init; }
    public required LessonTimeConfig TimeConfig { get; init; }
    public required IAllScheduledDateProvider DateProvider { get; init; }
    public required SemesterIntervalProvider SemesterIntervalProvider { get; init; }
    public required Semester Semester { get; init; }
}

internal interface IDateTime
{
    DateTime DateTime { get; }
}

internal readonly record struct LessonInstance : IDateTime
{
    public required RegularLessonId LessonId { get; init; }
    public required DateTime DateTime { get; init; }
    public required string? Topic { get; init; }
    public required Attendance[]? Attendance { get; init; }
}

internal readonly record struct LessonMatchParams
{
    public required CourseId CourseId { get; init; }
    public required GroupId GroupId { get; init; }
    public required SubGroup SubGroup { get; init; }
    public required LessonsByCourseMap Lookup { get; init; }
    public required Schedule Schedule { get; init; }
}

public static class MissingLessonDetection
{
    internal static IEnumerable<RegularLessonId> MatchLessonsInSchedule(LessonMatchParams p)
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

    // Could be made to rely on a T1 : IDateTime, T2 : IDateTime,
    // and take a custom diff impl.
    internal struct GetLessonEquationCommandsParams
    {
        public required Schedule Schedule;
        public required MatchingLists Lists;
        public required IEnumerable<RemoteLessonInstance> ExistingLessons;
        public required IEnumerable<LessonInstance> AllLessons;
    }

    private static DateOnly GetDateOnly<T>(this T item) where T : struct, IDateTime
    {
        return DateOnly.FromDateTime(item.DateTime);
    }

    internal static IEnumerable<LessonEquationCommand> GetLessonEquationCommands(GetLessonEquationCommandsParams p)
    {
        p.ExistingLessons = p.ExistingLessons.OrderBy(x => x.DateTime);
        p.AllLessons = p.AllLessons.OrderBy(x => x.DateTime);

        using var a_ = p.AllLessons.GetEnumerator();
        var allEnumerator = a_.RememberIsDone();

        using var b_ = p.ExistingLessons.GetEnumerator();
        var existingEnumerator = b_.RememberIsDone();

        allEnumerator.MoveNext();
        existingEnumerator.MoveNext();

        while (true)
        {
            if (allEnumerator.IsDone)
            {
                break;
            }
            if (existingEnumerator.IsDone)
            {
                break;
            }

            var all = allEnumerator.Current;
            var existing = existingEnumerator.Current;

            var allDate = all.GetDateOnly();
            var existingDate = existing.GetDateOnly();
            var todaysDate = allDate < existingDate ? allDate : existingDate;

            AddTodaysItems(ref allEnumerator, p.Lists.AllToday);
            AddTodaysItems(ref existingEnumerator, p.Lists.ExistingToday);

            Debug.Assert(!TwoLessonAtSameTime());
            var matchingContext = p.Lists.CreateContext();

            UseUpExactMatches();
            AddPartialMatches();

            var matchResult = matchingContext.AsResult();
            foreach (var r in matchResult.MatchedLessons())
            {
                yield return LessonEquationCommand.Update(r.Existing, r.All);
            }
            foreach (var r in matchResult.UnusedExisting())
            {
                yield return LessonEquationCommand.Delete(r);
            }
            foreach (var r in matchResult.UnusedAll())
            {
                yield return LessonEquationCommand.Create(r);
            }

            p.Lists.Clear();
            continue;

            void AddTodaysItems<T>(
                ref EnumerableHelper.RememberIsDoneEnumerator<T> e,
                List<T> list)

                where T : struct, IDateTime
            {
                while (true)
                {
                    var c = e.Current;
                    var date = c.GetDateOnly();
                    if (date != todaysDate)
                    {
                        break;
                    }

                    list.Add(c);

                    if (!e.MoveNext())
                    {
                        break;
                    }
                }
            }

            void AddPartialMatches()
            {
                ReadOnlySpan<LessonProperty> criteria = [
                    LessonProperty.Time,
                    LessonProperty.Type,
                    LessonProperty.Topic,
                    // Not doing this by attendance.
                ];
                foreach (var criterion in criteria)
                {
                    foreach (var x in matchingContext.IteratePotentialMappings())
                    {
                        if (!x.CriterionEquals(criterion, p.Schedule))
                        {
                            continue;
                        }
                        matchingContext.AddMatch(x.Mapping);
                    }
                }
            }

            void UseUpExactMatches()
            {
                foreach (var x in matchingContext.IteratePotentialMappings())
                {
                    bool AllEquals()
                    {
                        foreach (var t in new AllEnumEnumerable<LessonProperty>())
                        {
                            if (!x.CriterionEquals(t, p.Schedule))
                            {
                                return false;
                            }
                        }
                        return true;
                    }

                    if (AllEquals())
                    {
                        matchingContext.UseUpMatch(x.Mapping);
                    }
                }
            }

            bool TwoLessonAtSameTime()
            {
                var dates = new HashSet<DateTime>();
                foreach (var a in p.Lists.AllToday)
                {
                    if (!dates.Add(a.DateTime))
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        while (!allEnumerator.IsDone)
        {
            yield return LessonEquationCommand.Create(allEnumerator.Current);
            allEnumerator.MoveNext();
        }

        while (!existingEnumerator.IsDone)
        {
            yield return LessonEquationCommand.Delete(existingEnumerator.Current);
            existingEnumerator.MoveNext();
        }
    }
}

internal readonly record struct Mapping(int AllIndex, int ExistingIndex);

internal struct Matches
{
    public Matches(int allLen, int existingLen)
    {
        if (allLen > BitArray32.MaxLength)
        {
            throw new NotSupportedException("At most 32 lessons per day are supported.");
        }
        if (existingLen > BitArray32.MaxLength)
        {
            throw new NotSupportedException("At most 32 lessons per day are supported.");
        }
        AllMapped = BitArray32.Empty(allLen);
        ExistingMapped = BitArray32.Empty(existingLen);
    }

    public BitArray32 AllMapped;
    public BitArray32 ExistingMapped;

    public void Set(Mapping mapping)
    {
        Debug.Assert(!AllMapped.IsSet(mapping.AllIndex));
        Debug.Assert(!ExistingMapped.IsSet(mapping.ExistingIndex));
        AllMapped.Set(mapping.AllIndex);
        ExistingMapped.Set(mapping.ExistingIndex);
    }
}

internal enum LessonProperty
{
    Time,
    Type,
    Topic,
    Attendance,
}

internal struct MappedLesson
{
    public required Mapping Mapping;
    public required LessonInstance All;
    public required RemoteLessonInstance Existing;

    public readonly bool CriterionEquals(LessonProperty criterion, Schedule s)
    {
        return criterion switch
        {
            LessonProperty.Time => TimeEquals(),
            LessonProperty.Type => LessonTypesEqual(s),
            LessonProperty.Topic => TopicEquals(),
            LessonProperty.Attendance => AttendanceEquals(),
            _ => throw new NotSupportedException(),
        };
    }

    public readonly bool TimeEquals() => All.DateTime == Existing.DateTime;
    public readonly bool LessonTypesEqual(Schedule s)
    {
        if (Existing.LessonType == LessonType.Unspecified)
        {
            return true;
        }

        var lesson = s.Get(All.LessonId).Lesson;
        return lesson.Type == Existing.LessonType;
    }
    public readonly bool TopicEquals()
    {
        if (All.Topic is null)
        {
            return true;
        }
        if (All.Topic == Existing.Topic)
        {
            return true;
        }
        return false;
    }

    public readonly bool AttendanceEquals()
    {
        if (All.Attendance is null)
        {
            return true;
        }
        if (Equal(All.Attendance, Existing.Attendance))
        {
            return true;
        }
        return false;

        static bool Equal(Attendance[] all, Attendance[] existing)
        {
            if (all.Length != existing.Length)
            {
                return false;
            }
            for (int i = 0; i < all.Length; i++)
            {
                var a = all[i];
                var b = existing[i];
                if (a == Attendance.None)
                {
                    continue;
                }
                if (a != b)
                {
                    return false;
                }
            }
            return true;
        }
    }
}

internal readonly struct MatchingLists()
{
    public readonly List<Mapping> Matches = new();
    public readonly List<LessonInstance> AllToday = new();
    public readonly List<RemoteLessonInstance> ExistingToday = new();

    public readonly void Clear()
    {
        Matches.Clear();
        AllToday.Clear();
        ExistingToday.Clear();
    }
}

public enum LessonEquationCommandType
{
    Create,
    Update,
    Delete,
    Count,
}

[Flags]
public enum LessonEquationCommandTypes
{
    None = 0,
    Create = 1 << (int) LessonEquationCommandType.Create,
    Update = 1 << (int) LessonEquationCommandType.Update,
    Delete = 1 << (int) LessonEquationCommandType.Delete,
    All = Create | Update | Delete,
}

public static class LessonEquationCommandTypeHelper
{
    public static bool HasAll(this LessonEquationCommandType type)
    {
        return type is LessonEquationCommandType.Create or LessonEquationCommandType.Update;
    }

    public static bool HasExisting(this LessonEquationCommandType type)
    {
        return type is LessonEquationCommandType.Update or LessonEquationCommandType.Delete;
    }
}

internal readonly struct LessonEquationCommand
{
    public readonly LessonEquationCommandType Type;
    private readonly RemoteLessonInstance _existing;
    private readonly LessonInstance _all;

    private LessonEquationCommand(
        LessonEquationCommandType type,
        RemoteLessonInstance existing = default,
        LessonInstance all = default)
    {
        Type = type;
        _existing = existing;
        _all = all;
    }

    public bool HasAll => Type.HasAll();
    public LessonInstance All
    {
        get
        {
            Debug.Assert(HasAll);
            return _all;
        }
    }

    public bool HasExisting => Type.HasExisting();
    public RemoteLessonInstance Existing
    {
        get
        {
            Debug.Assert(HasExisting);
            return _existing;
        }
    }


    public static LessonEquationCommand Create(LessonInstance all)
    {
        return new(LessonEquationCommandType.Create, all: all);
    }

    public static LessonEquationCommand Update(RemoteLessonInstance existing, LessonInstance all)
    {
        return new(LessonEquationCommandType.Update, existing: existing, all: all);
    }

    public static LessonEquationCommand Delete(RemoteLessonInstance existing)
    {
        return new(LessonEquationCommandType.Delete, existing: existing);
    }
}

internal struct MatchingResult
{
    private readonly MatchingLists _lists;
    private readonly Matches _matches;

    internal MatchingResult(MatchingLists lists, Matches matches)
    {
        _lists = lists;
        _matches = matches;
    }

    public readonly UnusedEnumerable<LessonInstance> UnusedAll()
    {
        return new(_matches.AllMapped, _lists.AllToday);
    }

    public readonly UnusedEnumerable<RemoteLessonInstance> UnusedExisting()
    {
        return new(_matches.ExistingMapped, _lists.ExistingToday);
    }

    public readonly MappingsEnumerable MatchedLessons()
    {
        return new(_lists);
    }

    public readonly struct UnusedEnumerable<T>
    {
        private readonly BitArray32 _bits;
        private readonly List<T> _items;
        public UnusedEnumerable(BitArray32 bits, List<T> items)
        {
            _bits = bits;
            _items = items;
        }
        public UnusedEnumerator<T> GetEnumerator() => new(_bits, _items);
    }

    public struct UnusedEnumerator<T>
    {
        private SetBitIndicesEnumerator _e;
        private readonly List<T> _items;

        public UnusedEnumerator(BitArray32 isUsed, List<T> items)
        {
            _e = isUsed.UnsetBitIndicesLowToHigh.GetEnumerator();
            _items = items;
        }

        public T Current => _items[_e.Current];
        public bool MoveNext() => _e.MoveNext();
    }

    public readonly struct MappingsEnumerable
    {
        private readonly MatchingLists _lists;
        public MappingsEnumerable(MatchingLists lists) => _lists = lists;
        public MappingsEnumerator GetEnumerator() => new(_lists);
    }

    public struct MappingsEnumerator
    {
        private List<Mapping>.Enumerator _e;
        private readonly List<RemoteLessonInstance> _existing;
        private readonly List<LessonInstance> _all;

        public MappingsEnumerator(MatchingLists lists)
        {
            _e = lists.Matches.GetEnumerator();
            _existing = lists.ExistingToday;
            _all = lists.AllToday;
        }

        public MappedLesson Current
        {
            get
            {
                var m = _e.Current;
                return new()
                {
                    Mapping = m,
                    All = _all[m.AllIndex],
                    Existing = _existing[m.ExistingIndex],
                };
            }
        }

        public bool MoveNext() => _e.MoveNext();
    }
}

internal struct MatchingContext
{
    private readonly MatchingLists _lists;
    private Matches _matches;

    public MatchingContext(MatchingLists lists)
    {
        _lists = lists;
        _matches = new(lists.AllToday.Count, _lists.ExistingToday.Count);
    }

    public void AddMatch(Mapping m)
    {
        _lists.Matches.Add(m);
        UseUpMatch(m);
    }

    public void UseUpMatch(Mapping m)
    {
        _matches.Set(m);
    }

    public MatchingResult AsResult() => new(_lists, _matches);

    public readonly MappedLesson Get(Mapping m)
    {
        return new()
        {
            Mapping = m,
            All = _lists.AllToday[m.AllIndex],
            Existing = _lists.ExistingToday[m.ExistingIndex],
        };
    }

    public readonly ref struct PotentialMappingEnumerable
    {
        private readonly ref MatchingContext _context;
        public PotentialMappingEnumerable(ref MatchingContext context) => _context = ref context;
        public PotentialMappingEnumerator GetEnumerator() => new(ref _context);
    }

    public ref struct PotentialMappingEnumerator
    {
        private int _allIndex;
        private int _existingIndex;
        private readonly ref MatchingContext _context;

        public PotentialMappingEnumerator(ref MatchingContext context)
        {
            _allIndex = -1;
            _existingIndex = -1;
            _context = ref context;
        }

        public MappedLesson Current => _context.Get(new(AllIndex: _allIndex, ExistingIndex: _existingIndex));

        public bool MoveNext()
        {
            var unusedAllIndex = _context._matches.AllMapped.GetUnsetAtOrAfter(_allIndex);

            // There's no more available bits to iterate
            if (unusedAllIndex == -1)
            {
                return false;
            }
            if (_context._matches.ExistingMapped.AreAllSet)
            {
                return false;
            }

            // The current All index is unused.
            if (unusedAllIndex != _allIndex)
            {
                _allIndex = unusedAllIndex;
                _existingIndex = _context._matches.ExistingMapped.UnsetBitIndicesLowToHigh.First();
                return true;
            }

            var nextUnusedExistingIndex = _context._matches.ExistingMapped.GetUnsetAfter(_existingIndex);

            // There's no more unused existing indices.
            if (nextUnusedExistingIndex == -1)
            {
                int nextAll = _context._matches.AllMapped.GetUnsetAfter(_allIndex);
                if (nextAll == -1)
                {
                    return false;
                }
                _allIndex = nextAll;
                _existingIndex = _context._matches.ExistingMapped.UnsetBitIndicesLowToHigh.First();
                return true;
            }

            _existingIndex = nextUnusedExistingIndex;
            return true;
        }
    }
}

internal static class MatchingContextHelper
{
    public static MatchingContext.PotentialMappingEnumerable IteratePotentialMappings(
        this ref MatchingContext c)
    {
        return new(ref c);
    }

    public static MatchingContext CreateContext(this MatchingLists lists)
    {
        return new(lists);
    }
}
