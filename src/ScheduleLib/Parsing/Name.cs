using System.Text;

namespace ScheduleLib.Parsing;

public struct NameToStringParams()
{
    public bool IncludeLast = true;
    public bool IncludeFirst = true;
    public bool IncludePatronymic = true;
}

public sealed class Name_IgnoreDiacritics_EqualityComparer : IEqualityComparer<Name>
{
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
        if (!IgnoreDiacriticsAndCase_Name_Comparer.Instance.Equals(x.Patronymic, y.Patronymic))
        {
            return false;
        }
        return true;
    }

    public int GetHashCode(Name obj)
    {
        return obj.GetHashCode();
    }
}

public sealed record Name
{
    public NameParts<string?> FirstName;
    public NameParts<string?> LastName;
    public NameParts<string?> Patronymic;

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
    // LastName FirstName Patronymic
    public static Name ParseName(ref Parser parser)
    {
        var ret = new Name();

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
            return ret;
        }
        if (parser.ConsumeExactString(NameConstants.DoubleNameSeparator))
        {
            ret.FirstName[1] = ParseNamePart(ref parser, "First name incomplete");
        }

        parser.SkipWhitespace();
        IgnoreParenthesizedText(ref parser);

        if (parser.IsEmpty)
        {
            return ret;
        }

        ret.Patronymic[0] = ParseNamePart(ref parser, "No patronymic");
        if (parser.IsEmpty)
        {
            return ret;
        }

        if (parser.ConsumeExactString(NameConstants.DoubleNameSeparator))
        {
            ret.Patronymic[1] = ParseNamePart(ref parser, "Patronymic incomplete");
        }

        return ret;

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
            return ret;
        }
    }
}

