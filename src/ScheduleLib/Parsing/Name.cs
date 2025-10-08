using System.Diagnostics;
using System.Text;
using DocumentFormat.OpenXml.Bibliography;
using ScheduleLib.Parsing.Common;

namespace ScheduleLib.Parsing;

public struct NameToStringParams()
{
    public bool IncludeLast = true;
    public bool IncludeFirst = true;
    public bool IncludePatronymic = true;
}

public sealed class Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer : IEqualityComparer<Name>
{
    public static readonly Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer Instance = new();

    public bool Equals(Name? x, Name? y)
    {
        if (x is null && y is null)
        {
            return true;
        }
        if (x is null || y is null)
        {
            return false;
        }
        if (!IgnoreDiacriticsAndCase_Name_Comparer.Instance.Equals(x.FirstName, y.FirstName))
        {
            return false;
        }
        if (!IgnoreDiacriticsAndCase_Name_Comparer.Instance.Equals(x.LastName, y.LastName))
        {
            return false;
        }
        if (x.Patronymic == default || y.Patronymic == default)
        {
            return true;
        }
        if (IgnoreDiacriticsAndCase_Name_Comparer.Instance.Equals(x.Patronymic, y.Patronymic))
        {
            return false;
        }
        return true;
    }

    public int GetHashCode(Name obj)
    {
        var a = IgnoreDiacriticsAndCase_Name_Comparer.Instance.GetHashCode(obj.FirstName);
        var b = IgnoreDiacriticsAndCase_Name_Comparer.Instance.GetHashCode(obj.LastName);
        // var c = IgnoreDiacriticsAndCase_Name_Comparer.Instance.GetHashCode(obj.Patronymic);
        return HashCode.Combine(a, b);
    }
}

public struct NameFields
{
    public NameParts<string?> FirstName;
    public NameParts<string?> LastName;
    public NameParts<string?> Patronymic;
}

public sealed record Name
{
    private NameFields _fields;

    public NameParts<string?> FirstName
    {
        get => _fields.FirstName;
        set
        {
            AssertValid(value);
            _fields.FirstName = value;
        }
    }

    public NameParts<string?> LastName
    {
        get => _fields.LastName;
        set
        {
            AssertValid(value);
            _fields.LastName = value;
        }
    }

    public NameParts<string?> Patronymic
    {
        get => _fields.Patronymic;
        set
        {
            AssertValid(value);
            _fields.Patronymic = value;
        }
    }

    internal Name(NameFields f = default)
    {
        _fields = f;
        AssertValid();
    }

    private static void AssertValid(NameParts<string?> p)
    {
        Debug.Assert(p.All(x => x != ""));
    }

    internal void AssertValid()
    {
        void F(NameParts<string?> p)
        {
            Debug.Assert(p.All(x => x != ""));
        }
        F(FirstName);
        F(LastName);
        F(Patronymic);
    }

    public Name Copy() => (Name) MemberwiseClone();

    public string ToString(NameToStringParams p)
    {
        StringBuilder ret = new();
        var spacesB = new ListStringBuilder(ret, " ");
        if (p.IncludeLast)
        {
            AppendName(LastName);
        }
        if (p.IncludeFirst)
        {
            AppendName(FirstName);
        }
        if (p.IncludePatronymic)
        {
            AppendName(Patronymic);
        }
        return ret.ToString();

        void AppendName(NameParts<string?> parts)
        {
            spacesB.MaybeAppendSeparator();
            spacesB = new(ret, " ");

            var list = new ListStringBuilder(ret, "-");
            foreach (var x in parts)
            {
                if (x != null)
                {
                    list.Append(x);
                }
            }
        }
    }

    public override string ToString()
    {
        return ToString(new NameToStringParams()
        {
            IncludePatronymic = false,
        });
    }
}

public static class NameHelper
{
    public static Name? TryParseName(ref Parser parser)
    {
        try
        {
            return ParseName(ref parser);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private static Name ParseNameImpl(ref Parser parser)
    {
        var ret = new NameFields();

        ret.LastName[0] = ParseNamePart(ref parser, "No last name");

        LastNameCheck(ref parser);
        if (parser.ConsumeExactString(NameConstants.DoubleNameSeparator))
        {
            ret.LastName[1] = ParseNamePart(ref parser, "Last name incomplete");
        }

        parser.SkipWhitespace();
        IgnoreParenthesizedText(ref parser);
        FirstNameCheck(ref parser);

        ret.FirstName[0] = ParseNamePart(ref parser, "No first name");

        parser.SkipWhitespace();
        if (parser.IsEmpty)
        {
            return new(ret);
        }
        if (parser.ConsumeExactString(NameConstants.DoubleNameSeparator))
        {
            ret.FirstName[1] = ParseNamePart(ref parser, "First name incomplete");
        }

        parser.SkipWhitespace();
        IgnoreParenthesizedText(ref parser);

        if (parser.IsEmpty)
        {
            return new(ret);
        }

        ret.Patronymic[0] = ParseNamePart(ref parser, "No patronymic");
        if (parser.IsEmpty)
        {
            return new(ret);
        }

        if (parser.ConsumeExactString(NameConstants.DoubleNameSeparator))
        {
            ret.Patronymic[1] = ParseNamePart(ref parser, "Patronymic incomplete");
        }

        return new(ret);

        static void IgnoreParenthesizedText(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                return;
            }

            if (parser.Current != '(')
            {
                return;
            }

            var s = parser.SkipUntilAny([')']);
            if (!s.Satisfied)
            {
                throw new InvalidOperationException("Unclosed parenthesis");
            }

            parser.Move();
            parser.SkipWhitespace();
        }

        static void FirstNameCheck(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                throw new InvalidOperationException("First name expected");
            }
        }

        static void LastNameCheck(ref Parser parser)
        {
            if (parser.IsEmpty)
            {
                throw new InvalidOperationException("Last name expected");
            }
        }

        static string ParseNamePart(ref Parser parser, string error)
        {
            var bparser = parser.BufferedView();
            var skipResult = bparser.SkipLetters();
            if (!skipResult.SkippedAny)
            {
                throw new InvalidOperationException(error);
            }

            var ret = parser.PeekSpanUntilPosition(bparser.Position).ToString();
            parser.MoveTo(bparser.Position);
            return new(ret);
        }
    }

    // LastName FirstName Patronymic
    public static Name ParseName(ref Parser parser)
    {
        var ret = ParseNameImpl(ref parser);
        return ret;
    }
}

