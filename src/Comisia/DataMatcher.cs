using System.Collections.Immutable;
using System.Runtime.InteropServices;
using ScheduleLib;

namespace Comisia;

public sealed class MatchedData
{
    public required ImmutableArray<MatchedCommission> Commissions;
}

public sealed class MatchedCommission
{
    public required int CommissionNumber;
    public required DateOnly Date;
    public required ImmutableArray<Thesis> Theses;
    public required ImmutableArray<Name> MissingStudents;
}

public static class DataMatcher
{
    private readonly record struct NameKey(NameParts<string?> StudentLast);
    private readonly record struct ThesisByStudentName(NameParts<string?> StudentFirst, Thesis Thesis);

    private static bool AreEqual(NameParts<string?> x, NameParts<string?> y)
    {
        return x.EachEquals(y, static (a, b) =>
        {
            return IgnoreDiacriticsAndCaseComparer.Instance.Equals(a, b);
        });
    }

    private sealed class Comparer : IEqualityComparer<NameKey>
    {
        public static readonly Comparer Instance = new();

        public bool Equals(NameKey x, NameKey y)
        {
            return AreEqual(x.StudentLast, y.StudentLast);
        }

        public int GetHashCode(NameKey obj)
        {
            var hash = 0;
            foreach (var part in obj.StudentLast)
            {
                if (part is not null)
                {
                    hash ^= IgnoreDiacriticsAndCaseComparer.Instance.GetHashCode(part);
                }
            }
            return hash;
        }
    }

    public static MatchedData Match(
        CommissionSchedule commissionSchedule,
        ThesisList theses)
    {
        var dict = new Dictionary<NameKey, List<ThesisByStudentName>>(Comparer.Instance);
        foreach (var thesis in theses.Items)
        {
            var key = new NameKey(thesis.StudentName.LastName);
            List<ThesisByStudentName> List()
            {
                ref var t = ref CollectionsMarshal.GetValueRefOrAddDefault(dict, key, out bool exists);
                if (!exists)
                {
                    t = new();
                }
                return t!;
            }
            var list = List();

            if (list.Any(x => x.StudentFirst == thesis.StudentName.FirstName))
            {
                throw new InvalidOperationException("A student appears twice in the theses list. Dedup!");
            }

            list.Add(new()
            {
                Thesis = thesis,
                StudentFirst = thesis.StudentName.FirstName,
            });
        }

        var ret = ImmutableArray.CreateBuilder<MatchedCommission>(commissionSchedule.Commissions.Length);
        // Just to make sure there are no duplicates
        var thesesUsed = new HashSet<Thesis>();
        foreach (var commission in commissionSchedule.Commissions)
        {
            var thesesOfCommission = ImmutableArray.CreateBuilder<Thesis>(commission.Students.Length);
            var missingStudents = ImmutableArray.CreateBuilder<Name>();
            foreach (var studentName in commission.Students)
            {
                if (!dict.TryGetValue(new(studentName.LastName), out var thesesOfStudentsWithName))
                {
                    missingStudents.Add(studentName);
                    continue;
                }
                var thesisInfo = thesesOfStudentsWithName.Find(x =>
                {
                    return AreEqual(x.StudentFirst, studentName.FirstName);
                });
                if (thesisInfo.Thesis == null)
                {
                    missingStudents.Add(studentName);
                    continue;
                }

                var thesis = thesisInfo.Thesis;
                if (!thesesUsed.Add(thesis))
                {
                    throw new InvalidOperationException($"Thesis added for two commissions for student: {studentName}");
                }

                thesesOfCommission.Add(thesis);
            }

            ret.Add(new()
            {
                Date = commission.Date,
                CommissionNumber = commission.Number,
                Theses = thesesOfCommission.DrainToImmutable(),
                MissingStudents = missingStudents.DrainToImmutable(),
            });
        }
        return new()
        {
            Commissions = ret.MoveToImmutable(),
        };
    }
}
