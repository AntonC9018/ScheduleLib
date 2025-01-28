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
}

public static class TeacherLookupHelper
{
    public static int FindIndexOfBestMatch(
        ScheduleBuilder s,
        TeacherIdList ids,
        Word firstName)
    {
        return FindIndexOfBestMatch(
            s,
            CollectionsMarshal.AsSpan(ids),
            firstName);
    }

    public static int FindIndexOfBestMatch(
        ScheduleBuilder s,
        ReadOnlySpan<int> ids,
        Word firstName)
    {
        Debug.Assert(firstName.Value.Length > 0);

        if (firstName.LooksFull)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                ref var teacher = ref s.Teachers.Ref(id);
                if (teacher.Name.FirstName is { } first &&
                    firstName.LooksFull &&
                    IgnoreDiacriticsComparer.Instance.Equals(firstName.Value, first))
                {
                    return i;
                }
            }
        }

        {
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                ref var teacher = ref s.Teachers.Ref(id);
                if (teacher.Name.ShortFirstName is not { } shortName)
                {
                    continue;
                }

                if (IgnoreDiacriticsComparer.Instance.Equals(
                        // Ignore the separators as well
                        firstName.Span.Shortened.Value,
                        shortName.Span.Shortened.Value))
                {
                    return i;
                }
            }
        }
        // Let's just make sure it's shortened.
        // NOTE: even the short names might have more than 1 character before the separator
        if (!firstName.LooksFull)
        {
            for (int i = 0; i < ids.Length; i++)
            {
                int id = ids[i];
                ref var teacher = ref s.Teachers.Ref(id);
                if (teacher.Name.FirstName is { } first &&
                    IgnoreDiacriticsComparer.Instance.StartsWith(first, firstName.Span.Shortened.Value))
                {
                    return i;
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
        public string? FirstName;
        public Word? ShortFirstName;
        public string? LastName;

        public Word? LongerFirstName
        {
            get
            {
                if (FirstName is { } firstName)
                {
                    return new(firstName);
                }
                if (ShortFirstName is { } shortFirstName)
                {
                    return shortFirstName;
                }
                return null;
            }
        }
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
        if (name.ShortFirstName is { } x)
        {
            Debug.Assert(x.Value.Length > 0);
        }

        var list = Lookup1();
        if (FindId(list) is { } id)
        {
            var b = new TeacherBuilder
            {
                Id = new(id),
                Schedule = s,
            };

            // The first name should be updated.
            if (name.FirstName is { } f)
            {
                b.FirstName(f, name.ShortFirstName);
            }
            else if (name.ShortFirstName is { } f1)
            {
                b.ShortFirstName(f1);
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
            if (name.LongerFirstName is not { } longerFirstName)
            {
                if (lookup.Count > 0)
                {
                    return lookup[0];
                }
                return null;
            }
            int i = TeacherLookupHelper.FindIndexOfBestMatch(s, lookup, longerFirstName);
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
        var newFirstName = name.FirstName ?? Model.Name.FirstName;
        var newShortFirstName = name.ShortFirstName ?? Model.Name.ShortFirstName;
        TeacherNameHelper.MaybeValidateInitialsCompatibility(newFirstName, newShortFirstName);
        Model.Name.FirstName = newFirstName;
        Model.Name.ShortFirstName = newShortFirstName;

        LastName(name.LastName!, updateLookup: updateLookup);
    }

    public void ShortFirstName(Word initials)
    {
        Debug.Assert(initials.Value.Length > 0);
        if (Model.Name.FirstName is not { } firstName)
        {
            return;
        }
        TeacherNameHelper.ValidateInitialsCompatibility(firstName, initials);
        Model.Name.ShortFirstName = initials;
    }

    public void FirstName(string firstName, Word? initials)
    {
        Model.Name.FirstName = firstName;

        if ((initials ?? Model.Name.ShortFirstName) is { } i)
        {
            ShortFirstName(i);
        }
    }

    public void FirstName(string firstName, string? initials = null)
    {
        Word? w = initials is null ? null : new Word(initials);
        FirstName(firstName, w);
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
    /// </summary>
    public static TeacherBuilderModel.NameModel ParseName(ref Parser parser)
    {
        var ret = new TeacherBuilderModel.NameModel();
        var bparser = parser.BufferedView();
        var result = bparser.SkipUntil(['.', ' ', '(']);
        if (!result.SkippedAny)
        {
            return ret;
        }

        if (result.EndOfInput)
        {
            ret.LastName = parser.Source;
            return ret;
        }

        if (bparser.Current == '.')
        {
            bparser.Move();
            var firstName = parser.PeekSpanUntilPosition(bparser.Position);
            ret.ShortFirstName = new(firstName.ToString());
            parser.MoveTo(bparser.Position);

            if (parser.IsEmpty)
            {
                throw new ArgumentException("The string can't only have the first name.");
            }

            if (parser.Current == ' ')
            {
                parser.Move();
            }
        }
        else if (bparser.Current == ' ')
        {
            var firstName = parser.PeekSpanUntilPosition(bparser.Position);
            ret.FirstName = firstName.ToString();
            parser.MovePast(bparser.Position);
        }

        if (parser.IsEmpty)
        {
            throw new ArgumentException("The last name is required after the first name.");
        }

        bparser = parser.BufferedView();
        if (bparser.Current == ' ')
        {
            throw new ArgumentException("Only a single space in between first and last name allowed.");
        }

        bparser.SkipUntil([' ']);

        {
            var lastNameSpan = parser.PeekSpanUntilPosition(bparser.Position);
            var lastName = lastNameSpan.ToString();
            ret.LastName = lastName;
        }

        parser.MoveTo(bparser.Position);

        return ret;
    }

    public static void MaybeValidateInitialsCompatibility(string? firstName, Word? initials)
    {
        if (firstName is not { } firstNameNotNull)
        {
            return;
        }
        if (initials is not { } initialsNotNull)
        {
            return;
        }
        ValidateInitialsCompatibility(firstNameNotNull, initialsNotNull);
    }

    public static void ValidateInitialsCompatibility(string firstName, Word initials)
    {
        // This might be wrong.
        // Ana-Maria  ->  A-M. ?
        // I don't know.
        bool isOk = IgnoreDiacriticsComparer.Instance.StartsWith(
            firstName.AsSpan(),
            initials.Span.Shortened.Value);
        if (!isOk)
        {
            throw new ArgumentException(
                "The first name must start with initials when those are given.",
                nameof(initials));
        }
    }
}
