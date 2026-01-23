using System.Diagnostics;
using ScheduleLib.Helper;

namespace ScheduleLib.OnlineRegistry.Impl;

public sealed class SameDayDerivation : IEquationCommandsDerivation
{
    private MatchingLists _lists;

    public SameDayDerivation() : this(new())
    {
    }

    internal SameDayDerivation(MatchingLists lists)
    {
        _lists = lists;
    }

    public IEnumerable<LessonEquationCommand> DeriveCommands(GetLessonEquationCommandsParams p)
    {
        using var a_ = p.LocalLessons.GetEnumerator();
        var allEnumerator = a_.RememberIsDone();

        using var b_ = p.RemoteLessons.GetEnumerator();
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

            AddTodaysItems(ref allEnumerator, _lists.AllToday);
            AddTodaysItems(ref existingEnumerator, _lists.ExistingToday);

            Debug.Assert(!TwoLessonAtSameTime());
            var matchingContext = _lists.CreateContext();

            UseUpExactMatches();
            AddPartialMatches();

            var matchResult = matchingContext.AsResult();
            foreach (var r in matchResult.MatchedLessons())
            {
                yield return LessonEquationCommand.Update(r.Data.Remote, r.Data.Local);
            }
            foreach (var r in matchResult.UnusedExisting())
            {
                yield return LessonEquationCommand.Delete(r);
            }
            foreach (var r in matchResult.UnusedAll())
            {
                yield return LessonEquationCommand.Create(r);
            }

            _lists.Clear();
            continue;

            void AddTodaysItems<T>(
                ref EnumerableExtensions.RememberIsDoneEnumerator<T> e,
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
                        if (!x.Data.CriterionEquals(criterion, p.Schedule))
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
                        foreach (var t in new EnumMembers<LessonProperty>())
                        {
                            if (t is LessonProperty.DateTime or LessonProperty.Date)
                            {
                                continue;
                            }
                            if (!x.Data.CriterionEquals(t, p.Schedule))
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
                foreach (var a in _lists.AllToday)
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

internal struct MappedLesson
{
    public required Mapping Mapping;
    public required MatchedLessonData Data;
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
                    Data = new()
                    {
                        Local = _all[m.AllIndex],
                        Remote = _existing[m.ExistingIndex],
                    },
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
            Data = new()
            {
                Local = _lists.AllToday[m.AllIndex],
                Remote = _lists.ExistingToday[m.ExistingIndex],
            },
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
