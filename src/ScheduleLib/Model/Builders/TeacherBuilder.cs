using System.Diagnostics;
using System.Runtime.InteropServices;
using ScheduleLib.Parsing;

namespace ScheduleLib.Builders;

public partial class ScheduleBuilder
{
    public ListBuilder<TeacherBuilderModel> Teachers = new();
}

public sealed class TeacherIdList : List<int>
{
}

public readonly struct TeachersByLastName()
{
    private readonly Dictionary<string, TeacherIdList> _dict = new(IgnoreDiacriticsComparer.Instance);

    public TeacherIdList? Get(string lastName) => _dict.GetValueOrDefault(lastName);
    public TeacherIdList AddOrGet(string lastName) => _dict.GetOrAdd(lastName, _ => new());
    public void Clear() => _dict.Clear();
}

public static class TeacherLookupHelper
{
    public static int FindIndexOfBestMatch(
        ScheduleBuilder s,
        TeacherIdList ids,
        FirstNameParts<Word> firstName)
    {
        return FindIndexOfBestMatch(
            s,
            CollectionsMarshal.AsSpan(ids),
            firstName);
    }

    private enum FirstNameComparison
    {
        AllFull,
        AllAsShort,
        EitherFullOrShort,
        Count,
    }

    public static int FindIndexOfBestMatch(
        ScheduleBuilder s,
        ReadOnlySpan<int> ids,
        FirstNameParts<Word> firstName)
    {
        Debug.Assert(firstName.A.Value.Length > 0);
        bool onlyContainsFullNames = firstName.All(x => x.Value == "" || x.LooksFull);

        for (FirstNameComparison i = 0; i < FirstNameComparison.Count; i++)
        {
            if (!ShouldCheckThisStep())
            {
                continue;
            }

            for (int teacherIndex = 0; teacherIndex < ids.Length; teacherIndex++)
            {
                int id = ids[teacherIndex];
                ref var teacher = ref s.Teachers.Ref(id);
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
                    default:
                    {
                        throw Unreachable();
                    }
                }
            }

            bool CheckEquality(FirstNameParts<OptionalFirstNamePart> teacherName)
            {
                bool isSameNameCount = teacherName.EachEquals(firstName, (a, b) =>
                {
                    bool isTeacherEmpty = a.IsNull;
                    bool isNewEmpty = b.Value == "";
                    return isTeacherEmpty == isNewEmpty;
                });
                if (!isSameNameCount)
                {
                    return false;
                }

                switch (i)
                {
                    case FirstNameComparison.AllFull:
                    {
                        return teacherName.EachEquals(firstName, (existingPart, newPart) =>
                        {
                            if (existingPart.Full is not { } full)
                            {
                                return false;
                            }
                            if (!IgnoreDiacriticsComparer.Instance.Equals(full, newPart.Value))
                            {
                                return false;
                            }
                            return true;
                        });
                    }
                    case FirstNameComparison.AllAsShort:
                    {
                        return teacherName.EachEquals(firstName, (existingPart, newPart) =>
                        {
                            if (existingPart.Short is not { } teacherShort)
                            {
                                return false;
                            }
                            if (IgnoreDiacriticsComparer.Instance.Equals(
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
                        return teacherName.EachEquals(firstName, (existingPart, newPart) =>
                        {
                            if (existingPart.Full is { } full)
                            {
                                if (IgnoreDiacriticsComparer.Instance.Equals(full, newPart.Value))
                                {
                                    return true;
                                }
                            }
                            if (existingPart.Short is { } teacherShort)
                            {
                                if (IgnoreDiacriticsComparer.Instance.StartsWith(
                                        // Ignore the separators as well
                                        new WordSpan(teacherShort).Shortened.Value,
                                        newPart.Span.Shortened.Value))
                                {
                                    return true;
                                }
                            }
                            return false;
                        });
                    }
                    default:
                    {
                        throw Unreachable();
                    }
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

    public struct NameModel
    {
        public FirstNameParts<OptionalFirstNamePart> FirstName;
        public string? LastName;
    }
}

public static class TeacherBuilderHelper
{
    public static TeacherBuilder Teacher(this ScheduleBuilder s, TeacherBuilderModel.NameModel name)
    {
        if (name.LastName is { } lastName)
        {
            name.LastName = s.RemapTeacherName(lastName);
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

            // The first name should be updated.
            if (name.FirstName.Any(x => x.Full != null))
            {
                b.FirstName(name.FirstName);
            }
            else
            {
                var shortName = name.FirstName.Map(x => new Word(x.Short ?? ""));
                if (shortName.Any(x => x.Value != ""))
                {
                    b.ShortFirstName(shortName);
                }
            }

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
            builder.FullName(name);

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
            if (longer.Any(x => x != Word.Empty))
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
            if (teacher.Name.LastName == null)
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
        LastName(name.LastName!, updateLookup: updateLookup);
    }

    public void ShortFirstName(FirstNameParts<Word> initials)
    {
        Debug.Assert(initials.Any(x => x != Word.Empty));

        var firstName = Model.Name.FirstName;
        firstName.Update(initials, (f, i) => f with
        {
            Short = i != Word.Empty ? i.Value : f.Short,
        });

        TeacherNameHelper.MaybeValidateInitialsCompatibility(firstName);
        Model.Name.FirstName = firstName;
    }

    public void FirstName(FirstNameParts<OptionalFirstNamePart> initials)
    {
        var firstName = Model.Name.FirstName;
        // I have no idea if this is right, this needs some tests
        firstName.Update(initials, (f, i) => new()
        {
            Full = i.Full ?? f.Full,
            Short = i.Full ?? f.Full,
        });

        TeacherNameHelper.MaybeValidateInitialsCompatibility(firstName);
        Model.Name.FirstName = firstName;
    }

    public void LastName(string lastName, bool updateLookup = true)
    {
        lastName = Schedule.RemapTeacherName(lastName);

        var prevLastName = Model.Name.LastName;
        Model.Name.LastName = lastName;

        if (!updateLookup)
        {
            return;
        }

        if (prevLastName == null
            || !IgnoreDiacriticsComparer.Instance.Equals(prevLastName, lastName))
        {
            return;
        }

        if (Schedule.LookupModule is not { } lookup)
        {
            return;
        }

        {
            var people = lookup.TeachersByLastName.Get(prevLastName);
            Debug.Assert(people != null);
            people.Remove(Id.Id);
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
        if (name.LastName is null)
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
    /// </summary>
    public static TeacherBuilderModel.NameModel ParseName(ref Parser parser)
    {
        var ret = new TeacherBuilderModel.NameModel();
        var bparser = parser.BufferedView();
        // ( is for the maiden name syntax.
        // Not mentioned or used, but it is allowed.
        var result = bparser.SkipUntil(['.', ' ', '(', '-']);
        if (!result.SkippedAny)
        {
            return ret;
        }

        if (result.EndOfInput)
        {
            ret.LastName = parser.Source;
            parser.MoveTo(bparser.Position);
            return ret;
        }

        var firstNamePartE = ret.FirstName.AsRef().GetEnumerator();
        while (true)
        {
            if (!firstNamePartE.MoveNext())
            {
                throw new InvalidOperationException("Too many name parts.");
            }

            bool isShort = false;
            if (bparser.Current == '.')
            {
                bparser.Move();
                isShort = true;
            }

            var nameSpan = parser.PeekSpanUntilPosition(bparser.Position);
            var namePartString = nameSpan.ToString();

            ref var currentOutput = ref firstNamePartE.Current;
            if (isShort)
            {
                currentOutput.Short = namePartString;
            }
            else
            {
                currentOutput.Full = namePartString;
            }

            bparser.Move();
            bparser.SkipWhitespace();

            if (bparser.IsEmpty)
            {
                parser.MoveTo(bparser.Position);
                break;
            }

            if (bparser.Current != '-')
            {
                break;
            }
            bparser.Move();

            RequireFirstNameAfterDash(ref bparser);
            bparser.SkipWhitespace();
            RequireFirstNameAfterDash(ref bparser);

            parser.MoveTo(bparser.Position);

            var skipResult = bparser.SkipUntil(['.', ' ', '-']);
            if (skipResult.EndOfInput || bparser.Current == ' ')
            {
                parser.MoveTo(bparser.Position);
                break;
            }
        }

        RequireSpaceAfterFirstName(ref bparser);
        if (bparser.Current == ' ')
        {
            bparser.Move();
        }
        RequireSpaceAfterFirstName(ref bparser);

        if (bparser.Current == ' ')
        {
            throw new ArgumentException("Only a single space in between first and last name allowed.");
        }

        parser.MoveTo(bparser.Position);
        bparser.SkipUntil([' ']);

        {
            var lastNameSpan = parser.PeekSpanUntilPosition(bparser.Position);
            var lastName = lastNameSpan.ToString();
            ret.LastName = lastName;
        }

        parser.MoveTo(bparser.Position);

        return ret;

        static void RequireFirstNameAfterDash(ref Parser parser)
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
    }

    public struct RequiredFirstNamePart
    {
        public required string Full;
        public required Word Short;
    }

    public static void MaybeValidateInitialsCompatibility(FirstNameParts<OptionalFirstNamePart> firstName)
    {
        foreach (var x in firstName)
        {
            if (x.Full is not { } full)
            {
                continue;
            }
            if (x.Short is not { } short_)
            {
                continue;
            }
            ValidateInitialsCompatibility(new()
            {
                Full = full,
                Short = new Word(short_),
            });
        }
    }

    public static void ValidateInitialsCompatibility(RequiredFirstNamePart requiredFirstName)
    {
        bool isOk = IgnoreDiacriticsComparer.Instance.StartsWith(
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
