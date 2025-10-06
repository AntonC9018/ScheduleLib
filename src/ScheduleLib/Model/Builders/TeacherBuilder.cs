using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.Common;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public ListBuilder<TeacherBuilderModel> Teachers = new();
}

public sealed class TeacherIdList : List<int>
{
    public new void Add(int id)
    {
        Debug.Assert(!Contains(id));
        base.Add(id);
    }
}

public readonly struct TeachersByLastName()
{
    private readonly Dictionary<NameParts<string?>, TeacherIdList> _dict = new(IgnoreDiacriticsAndCase_Name_Comparer.Instance);

    // TODO:
    // Restore span overloads.
    // The problem is that generic NameParts<T> where T : allows ref struct is impossible.
    public TeacherIdList? Get(LastName lastName)
    {
        _dict.TryGetValue(lastName, out var ret);
        return ret;
    }

    public TeacherIdList AddOrGet(LastName lastName)
    {
        return _dict.GetOrAdd(lastName, _ => new());
    }

    public void Clear() => _dict.Clear();
}

public static class TeacherLookupHelper
{
    public static int FindIndexOfBestMatch(
        ScheduleBuilder s,
        TeacherIdList ids,
        NameParts<Word> name)
    {
        return FindIndexOfBestMatch(
            CollectionsMarshal.AsSpan(s.Teachers.List),
            CollectionsMarshal.AsSpan(ids),
            name);
    }

    private enum FirstNameComparison
    {
        AllFull,
        AllAsShort,
        EitherFullOrShort,
        MatchFullAgainstShort,
        Count,
    }

    public static int FindIndexOfBestMatch(
        ReadOnlySpan<TeacherBuilderModel> teachers,
        ReadOnlySpan<int> ids,
        NameParts<Word> name)
    {
        Debug.Assert(name[0].Value.Length > 0);
        Debug.Assert(name.All(w => w.Value != null));
        bool onlyContainsFullNames = name.All(x => x == Word.Empty || x.LooksFull);

        for (FirstNameComparison i = 0; i < FirstNameComparison.Count; i++)
        {
            if (!ShouldCheckThisStep())
            {
                continue;
            }

            for (int teacherIndex = 0; teacherIndex < ids.Length; teacherIndex++)
            {
                int id = ids[teacherIndex];
                var teacher = teachers[id];
                var teacherFirstName = teacher.Name.FirstName;
                if (CheckEquality(teacherFirstName))
                {
                    return teacherIndex;
                }
            }
            continue;

            bool ShouldCheckThisStep()
            {
                switch (i)
                {
                    case FirstNameComparison.AllFull:
                    {
                        return onlyContainsFullNames;
                    }
                    case FirstNameComparison.AllAsShort:
                    {
                        return true;
                    }
                    case FirstNameComparison.EitherFullOrShort:
                    {
                        return !onlyContainsFullNames;
                    }
                    case FirstNameComparison.MatchFullAgainstShort:
                    {
                        return onlyContainsFullNames;
                    }
                    default:
                    {
                        throw Unreachable();
                    }
                }
            }

            bool CheckEquality(NameParts<OptionalNamePart> teacherName)
            {
                var nonEmptyCount = name.Count(x => x != Word.Empty);

                {
                    var teacherCount = teacherName.Count(x => !x.IsNull);
                    if (teacherCount != nonEmptyCount)
                    {
                        return false;
                    }
                }

                switch (i)
                {
                    case FirstNameComparison.AllFull:
                    {
                        var fullNameCount = teacherName.Count(x => x.Full != null);
                        if (fullNameCount != nonEmptyCount)
                        {
                            return false;
                        }

                        return teacherName.EachEquals(name, (existingPart, newPart) =>
                        {
                            if (existingPart.Full is not { } full)
                            {
                                return true;
                            }
                            if (!IgnoreDiacriticsAndCaseComparer.Instance.Equals(full, newPart.Value))
                            {
                                return false;
                            }
                            return true;
                        });
                    }
                    case FirstNameComparison.AllAsShort:
                    {
                        var shortCount = teacherName.Count(x => x.Short != null);
                        if (shortCount != nonEmptyCount)
                        {
                            return false;
                        }

                        return teacherName.EachEquals(name, (existingPart, newPart) =>
                        {
                            if (existingPart.Short is not { } teacherShort)
                            {
                                return true;
                            }
                            if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(
                                    // Ignore the separators as well
                                    newPart.Span.Shortened.Value,
                                    new WordSpan(teacherShort).Shortened.Value))
                            {
                                return true;
                            }
                            return false;
                        });
                    }
                    case FirstNameComparison.EitherFullOrShort:
                    {
                        return FullOrShort(teacherName, name);
                    }
                    case FirstNameComparison.MatchFullAgainstShort:
                    {
                        return teacherName.EachEquals(name, (existingPart, newPart) =>
                        {
                            if (existingPart.IsNull)
                            {
                                return true;
                            }

                            Debug.Assert(newPart.Span.LooksFull);

                            var newFull = newPart.Span.Value;
                            if (existingPart.Full is { } f)
                            {
                                if (IgnoreDiacriticsAndCaseComparer.Instance.Equals(newFull, f))
                                {
                                    return true;
                                }
                                return false;
                            }
                            if (existingPart.Short is { } s)
                            {
                                if (IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(
                                        newFull,
                                        new Word(s).Span.Shortened.Value))
                                {
                                    return true;
                                }
                                return false;
                            }
                            throw Unreachable();
                        });
                    }
                    default:
                    {
                        throw Unreachable();
                    }
                }

                static bool FullOrShort(
                    NameParts<OptionalNamePart> a,
                    NameParts<Word> b)
                {
                    return a.EachEquals(b, (apart, bpart) =>
                    {
                        if (apart.IsNull)
                        {
                            return true;
                        }

                        if (apart.Full is { } full)
                        {
                            bool matches = false;
                            if (apart.Short is null)
                            {
                                matches = IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(
                                    full,
                                    bpart.Span.Shortened.Value);
                            }
                            else if (bpart.LooksFull)
                            {
                                matches = IgnoreDiacriticsAndCaseComparer.Instance.Equals(
                                    full,
                                    bpart.Value);
                            }
                            if (matches)
                            {
                                return true;
                            }
                        }
                        if (apart.Short is { } teacherShort)
                        {
                            // Ignore the separators as well
                            var longer = new WordSpan(teacherShort).Shortened.Value;
                            var shorter = bpart.Span.Shortened.Value;
                            if (longer.Length < shorter.Length)
                            {
                                var t = longer;
                                longer = shorter;
                                shorter = t;
                            }

                            if (IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(longer, shorter))
                            {
                                return true;
                            }
                        }
                        return false;
                    });
                }
            }
        }

        return -1;
    }
}

public sealed class TeacherBuilderModel
{
    public NameModel Name;
    public PersonContacts Contacts;

    public record struct NameModel
    {
        // Includes the . at the end
        public NameParts<OptionalNamePart> FirstName;
        public LastName LastName;
    }
}

public static class TeacherBuilderHelper
{
    public static TeacherBuilder Teacher(this ScheduleBuilder s, TeacherBuilderModel.NameModel name)
    {
        if (!name.LastName.IsNull)
        {
            name.LastName = s.RemapTeacherName(name.LastName);
        }

        Debug.Assert(name.FirstName.All(x =>
        {
            if (x.Longer is { } l)
            {
                return l.Length > 0;
            }
            return true;
        }));

        var list = Lookup1();
        if (FindId(list) is { } id)
        {
            var b = new TeacherBuilder
            {
                Id = new(id),
                Schedule = s,
            };
            b.FirstName(name.FirstName);

            return b;
        }

        {
            var ret = s.Teachers.New();
            ret.Value = new();
            var builder = new TeacherBuilder
            {
                Id = new(ret.Id),
                Schedule = s,
            };
            builder.FullName(name, updateLookup: false);

            // Update the lookup manually.
            list?.Add(ret.Id);

            return builder;
        }

        int? FindId(TeacherIdList? lookup)
        {
            if (lookup is null)
            {
                return null;
            }
            var longer = name.FirstName.Longer().Map(x => new Word(x ?? ""));
            if (longer.All(x => x == Word.Empty))
            {
                if (lookup.Count > 0)
                {
                    return lookup[0];
                }
                return null;
            }
            int i = TeacherLookupHelper.FindIndexOfBestMatch(s, lookup, longer);
            if (i == -1)
            {
                return null;
            }

            int ret = lookup[i];
            return ret;
        }

        TeacherIdList? Lookup1()
        {
            if (s.LookupModule is not { } lookupModule)
            {
                return null;
            }

            var ret = lookupModule.TeachersByLastName.AddOrGet(name.LastName!);
            return ret;
        }
    }

    public static TeacherBuilder Teacher(this ScheduleBuilder s, string fullName)
    {
        var name = TeacherNameHelper.ParseName(fullName);
        var ret = Teacher(s, name);
        return ret;
    }

    public static void ValidateTeachers(ScheduleBuilder s)
    {
        foreach (ref var teacher in CollectionsMarshal.AsSpan(s.Teachers.List))
        {
            if (teacher.Name.LastName.IsNull)
            {
                throw new InvalidOperationException("The teacher last name must be initialized.");
            }
        }
    }
}

public readonly struct TeacherBuilder
{
    public required ScheduleBuilder Schedule { get; init; }
    public required TeacherId Id { get; init; }
    public ref TeacherBuilderModel Model => ref Schedule.Teachers.Ref(Id.Id);
    public static implicit operator TeacherId(TeacherBuilder r) => r.Id;

    /// <summary>
    /// Allowed syntax: <see cref="TeacherNameHelper.ParseName"/>
    /// </summary>
    public void FullName(string fullName, bool updateLookup = true)
    {
        var name = TeacherNameHelper.ParseName(fullName);
        FullName(name, updateLookup: updateLookup);
    }

    public void FullName(TeacherBuilderModel.NameModel name, bool updateLookup = true)
    {
        var newName = name.FirstName;
        newName.Update(Model.Name.FirstName, (a, b) => new()
        {
            Full = a.Full ?? b.Full,
            Short = a.Short ?? b.Short,
        });
        TeacherNameHelper.MaybeValidateInitialsCompatibility(newName);
        Model.Name.FirstName = newName;

        LastName(name.LastName, updateLookup: updateLookup);
    }

    public void ShortFirstName(NameParts<Word> initials)
    {
        Debug.Assert(initials.Any(x => x != Word.Empty));

        var firstName = Model.Name.FirstName;
        firstName.Update(initials, (f, i) => new()
        {
            Full = f.Full,
            Short = i != Word.Empty ? i.Value : f.Short,
        });

        TeacherNameHelper.MaybeValidateInitialsCompatibility(firstName);
        Model.Name.FirstName = firstName;
    }

    public void FirstName(NameParts<OptionalNamePart> newName)
    {
        var firstName = Model.Name.FirstName;
        firstName.Update(newName, (old, new_) =>
        {
            var full = new_.Full ?? old.Full;
            var short_ = new_.Short ?? old.Short;

            if (full is not null)
            {
                Debug.Assert(new WordSpan(full).LooksFull);
            }

            return new()
            {
                Full = full,
                Short = short_,
            };
        });

        TeacherNameHelper.MaybeValidateInitialsCompatibility(firstName);
        Model.Name.FirstName = firstName;
    }

    public void LastName(LastName lastName, bool updateLookup = true)
    {
        lastName = Schedule.RemapTeacherName(lastName);

        var prevLastName = Model.Name.LastName;
        Model.Name.LastName = lastName;

        if (!updateLookup)
        {
            return;
        }

        if (IgnoreDiacriticsAndCase_Name_Comparer.Instance.Equals(prevLastName, lastName))
        {
            return;
        }

        if (Schedule.LookupModule is not { } lookup)
        {
            return;
        }

        if (prevLastName != default)
        {
            var people = lookup.TeachersByLastName.Get(prevLastName);
            Debug.Assert(people != null);
            bool removed = people.Remove(Id.Id);
            Debug.Assert(removed);
        }

        {
            var people = lookup.TeachersByLastName.AddOrGet(lastName);
            people.Add(Id.Id);
        }
    }

    public void Contacts(PersonContacts contacts)
    {
        Model.Contacts = contacts;
    }
}

public static class TeacherNameHelper
{
    public static TeacherBuilderModel.NameModel ParseName(string fullName)
    {
        var parser = new Parser(fullName);
        var name = ParseName(ref parser);
        if (!parser.IsEmpty)
        {
            throw new ArgumentException("The full name must contain a correct name syntax.", nameof(fullName));
        }
        if (name.LastName == default)
        {
            throw new ArgumentException("Last name must be provided.", nameof(fullName));
        }
        return name;
    }

    /// <summary>
    /// The forms allowed:
    /// F. Last
    /// F.Last
    /// First Last
    /// Last
    /// F.-N. Last
    /// F.-Name Last
    /// First-Name Last
    /// First-N. Last
    /// First Last-Name
    /// F.-N. Last-Name
    /// etc.
    /// </summary>
    public static TeacherBuilderModel.NameModel ParseName(ref Parser parser)
    {
        static ReadOnlySpan<char> Separators() => [
            WordHelper.ShortenedWordCharacter,
            ' ',
            NameConstants.DoubleNameSeparatorChar];

        var ret = new TeacherBuilderModel.NameModel();
        var nameA = ReadName(ref parser);
        // ( is for the maiden name syntax.
        // Not mentioned or used, but it is allowed.
        if (nameA.All(x => x.IsNull))
        {
            return ret;
        }
        // If only a single name has been given, it's the last name.
        if (parser.IsEmpty)
        {
            ret.LastName = new(MakeSureWithoutShortName(nameA));
            return ret;
        }

        ret.FirstName = nameA;

        RequireSpaceAfterFirstName(ref parser);
        if (parser.Current == ' ')
        {
            parser.Move();
        }
        RequireSpaceAfterFirstName(ref parser);

        if (parser.Current == ' ')
        {
            throw new ArgumentException("Only a single space in between first and last name allowed.");
        }

        var nameB = ReadName(ref parser);
        if (nameB == default)
        {
            throw new ArgumentException("The last name must be provided.");
        }
        ret.LastName = new(MakeSureWithoutShortName(nameB));

        return ret;

        static NameParts<string?> MakeSureWithoutShortName(NameParts<OptionalNamePart> x)
        {
            foreach (var a in x)
            {
                if (a.Short != null)
                {
                    throw new ArgumentException("Short names are not allowed in the last name.");
                }
            }

            return x.Map(a => a.Full);
        }

        static void IncompleteDoubleName(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                throw new ArgumentException("The first name is required after the dash.");
            }
        }

        static void RequireSpaceAfterFirstName(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                throw new ArgumentException("The last name is required after the first name.");
            }
        }

        static NameParts<OptionalNamePart> ReadName(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            var result = bparser.SkipUntilAny(Separators());
            if (!result.SkippedAny)
            {
                return default;
            }

            var ret = new NameParts<OptionalNamePart>();
            var retPartE = ret.AsRef().GetEnumerator();
            while (true)
            {
                if (!retPartE.MoveNext())
                {
                    throw new InvalidOperationException("Too many name parts.");
                }

                bool isShort = false;
                if (!bparser.IsEmpty && bparser.Current == WordHelper.ShortenedWordCharacter)
                {
                    bparser.Move();
                    isShort = true;
                }

                var nameSpan = parser.PeekSpanUntilPosition(bparser.Position);
                var namePartString = nameSpan.ToString();

                ref var currentOutput = ref retPartE.Current;
                if (isShort)
                {
                    currentOutput.Short = namePartString;
                }
                else
                {
                    currentOutput.Full = namePartString;
                }

                bparser.SkipWhitespace();

                if (bparser.IsEmpty)
                {
                    break;
                }

                if (!bparser.ConsumeExactString(NameConstants.DoubleNameSeparator))
                {
                    break;
                }

                IncompleteDoubleName(ref bparser);
                bparser.SkipWhitespace();
                IncompleteDoubleName(ref bparser);

                parser.MoveTo(bparser.Position);

                var skipResult = bparser.SkipUntilAny(Separators());
                if (!skipResult.SkippedAny)
                {
                    return ret;
                }
            }
            parser.MoveTo(bparser.Position);
            return ret;
        }
    }

    public struct RequiredFirstNamePart
    {
        public required string Full;
        public required Word Short;
    }

    public static void MaybeValidateInitialsCompatibility(NameParts<OptionalNamePart> name)
    {
        foreach (var x in name)
        {
            if (x.Full is not { } full)
            {
                continue;
            }
            Debug.Assert(full.Length > 0);
            if (x.Short is not { } short_)
            {
                continue;
            }
            Debug.Assert(short_.Length > 0);
            ValidateInitialsCompatibility(new()
            {
                Full = full,
                Short = new Word(short_),
            });
        }
    }

    public static void ValidateInitialsCompatibility(RequiredFirstNamePart requiredFirstName)
    {
        bool isOk = IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(
            requiredFirstName.Full.AsSpan(),
            requiredFirstName.Short.Span.Shortened.Value);
        if (!isOk)
        {
            throw new ArgumentException(
                "The first name must start with initials when those are given.",
                nameof(requiredFirstName));
        }
    }
}
