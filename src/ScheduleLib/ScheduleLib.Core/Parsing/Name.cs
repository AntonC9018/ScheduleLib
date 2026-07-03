using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing;

public enum NameField
{
    First,
    Last,
    Patronymic,
    Count,
}

public struct NameToStringParams()
{
    public bool IncludeLast = true;
    public bool IncludeFirst = true;
    public bool IncludePatronymic = true;

    public static NameToStringParams IncludeEverything => new();
}

public sealed class Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer : IEqualityComparer<Name>
{
    public static readonly Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer Instance = new();

    public bool Equals(Name? x, Name? y)
    {
        if (ComparisonHelper.AtLeastOneIsNull(x, y, out bool b))
        {
            return b;
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
        if (!IgnoreDiacriticsAndCase_Name_Comparer.Instance.Equals(x.Patronymic, y.Patronymic))
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

public record struct NameFields
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
            if (parts == default)
            {
                return;
            }
            spacesB.MaybeAppendSeparator();
            spacesB = spacesB.ResetBuilder;

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

public sealed record Name
{
    private NameFields _fields;
    public ref readonly NameFields Fields => ref _fields;

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

    public Name(NameFields f = default)
    {
        _fields = f;
        AssertValid();
    }

    [Conditional("DEBUG")]
    private static void AssertValid(NameParts<string?> p)
    {
        Debug.Assert(p.All(x => x != ""));
        Debug.Assert(p.All(x => x.Count(' ') == 0));
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
        return _fields.ToString(p);
    }

    public override string ToString()
    {
        return _fields.ToString();
    }

    public string AsFormattable()
    {
        return ToString();
    }
}

public static class NameTokenType
{
    public const TokenType Whitespace = TokenType.Whitespace;
    public const TokenType DoubleNameSeparator = (TokenType) NameConstants.DoubleNameSeparatorChar;
    public const TokenType Word = TokenType.Invalid + 1;
    public const TokenType ParenthesizedText = Word + 1;
}

public sealed class NameTokenReader : ITokenReader
{
    public static readonly NameTokenReader Instance = new();
    public TokenTypeLabels Labels { get; } = LexerHelper.CreateLabels(typeof(NameTokenType));

    public TokenType Read(ref Parser parser)
    {
        if (parser.SkipLetters().SkippedAny)
        {
            // That's how we do short name support.
            parser.ConsumeExactChar(WordHelper.ShortenedWordCharacter);
            return NameTokenType.Word;
        }
        if (parser.ConsumeExactString(NameConstants.DoubleNameSeparator))
        {
            return NameTokenType.DoubleNameSeparator;
        }
        if (parser.ConsumeExactChar('('))
        {
            var bparser = parser.BufferedView();
            if (!bparser.SkipUntilAny(")").Satisfied)
            {
                return TokenType.Invalid;
            }
            parser.MovePast(bparser.Position);
            return NameTokenType.ParenthesizedText;
        }
        if (parser.SkipWhitespace().SkippedAny)
        {
            return TokenType.Whitespace;
        }
        var s = parser.SkipNotWhitespace();
        Debug.Assert(s.SkippedAny);
        return TokenType.Invalid;
    }
}

public static class NameHelper
{
    public static Name? TryParseName(ref Parser parser)
    {
        try
        {
            return Parse(ref parser);
        }
        catch (NameParsingException)
        {
            return null;
        }
    }

    private static Name ParseImpl(ref LexerScope lexer)
    {
        var ret = new NameFields();

        ret.LastName = ParseRequiredNamePart(ref lexer, "Last name required", "Last name incomplete");
        IgnoreParenthesizedTextAndWhitespace(ref lexer);
        ErrorOnInvalidToken(ref lexer);

        ret.FirstName = ParseRequiredNamePart(ref lexer, "First name required", "First name incomplete");
        if (lexer.IsEmpty)
        {
            return new(ret);
        }

        var blexer = lexer;
        IgnoreParenthesizedTextAndWhitespace(ref blexer);

        ret.Patronymic = ParseNamePart(ref blexer, out bool isIncompletePatro);
        if (isIncompletePatro)
        {
            throw blexer.NameParsingException("Patronymic incomplete");
        }
        if (ret.Patronymic == default)
        {
            return new(ret);
        }

        lexer.MoveTo(blexer.Position);
        lexer.Apply();

        return new(ret);

        static void IgnoreParenthesizedTextAndWhitespace(ref LexerScope lexer)
        {
            lexer.ConsumeAllConsecutive([TokenType.Whitespace]);
            if (lexer.TryConsume(NameTokenType.ParenthesizedText))
            {
                lexer.ConsumeAllConsecutive([TokenType.Whitespace]);
            }
        }

        static void ErrorOnInvalidToken(ref LexerScope lexer)
        {
            if (!lexer.IsEmpty && lexer.Current.Type == TokenType.Invalid)
            {
                throw lexer.NameParsingException("Invalid token");
            }
        }

        static NameParts<string?> ParseRequiredNamePart(
            ref LexerScope lexer,
            string requiredError,
            string incompleteError)
        {
            var ret = ParseNamePart(ref lexer, out bool isIncompleteDoubleName);
            if (isIncompleteDoubleName)
            {
                throw lexer.NameParsingException(incompleteError);
            }
            if (ret == default)
            {
                throw lexer.NameParsingException(requiredError);
            }
            return ret;
        }
    }

    public static NameParts<string?> ParseNamePart(
        ref LexerScope lexer,
        out bool isIncompleteDoubleName)
    {
        var ret = new NameParts<string?>();

        static bool ParsePartOfPart(ref LexerScope lexer, out string? ret)
        {
            if (lexer.IsEmpty)
            {
                ret = null;
                return false;
            }
            var t = lexer.Current;
            if (t.Type != NameTokenType.Word)
            {
                ret = null;
                return false;
            }
            lexer.Move();
            ret = t.Value.ToString();
            return true;
        }

        isIncompleteDoubleName = false;

        if (!ParsePartOfPart(ref lexer, out ret[0]))
        {
            return ret;
        }

        if (lexer.TryConsume(NameTokenType.DoubleNameSeparator))
        {
            if (!ParsePartOfPart(ref lexer, out ret[1]))
            {
                isIncompleteDoubleName = true;
            }
        }
        return ret;
    }

    // LastName FirstName Patronymic
    public static Name Parse(ref Parser parser)
    {
        var p = new NameParser();
        p.Load(parser.SourceUntilEnd());
        var scope = p.Scope();
        var ret = ParseImpl(ref scope);
        scope.Apply(ref parser);
        return ret;
    }

    public static Name Parse(string s)
    {
        var parser = new Parser(s);
        var ret = Parse(ref parser);
        if (!parser.IsEmpty)
        {
            throw new NameParsingException("Extra characters after name");
        }
        return ret;
    }

    public static ref NameParts<string?> FieldMut(ref NameFields fields, NameField field)
    {
        return ref Unsafe.AsRef(in Field(fields, field));
    }

    public static ref readonly NameParts<string?> Field(in NameFields fields, NameField field)
    {
        switch (field)
        {
            case NameField.First:
            {
                return ref fields.FirstName;
            }
            case NameField.Last:
            {
                return ref fields.LastName;
            }
            case NameField.Patronymic:
            {
                return ref fields.Patronymic;
            }
            default:
            {
                throw Unreachable();
            }
        }
    }

    private static NameParsingException NameParsingException(this LexerScope lexer, string err)
    {
        // TODO: Do token errors properly
        return new NameParsingException($"{err} at token {lexer}");
    }
}

#pragma warning disable CA1001 // It is disposable? Fake warning.
public readonly struct NameParser : IDisposable
#pragma warning restore CA1001
{
    internal readonly Lexer _lexer;
    private readonly SingleItemEnumerator<ReadOnlyMemory<char>> _s;

    public void Load(ReadOnlyMemory<char> s)
    {
        _s.Reset(s);
        _lexer.Reset(_s);
    }

    public NameParser()
    {
        _s = new();
        _lexer = new Lexer(NameTokenReader.Instance);
    }

    public LexerScope Scope()
    {
        return _lexer.Scope();
    }

    public void Dispose()
    {
    }
}

public sealed class NameParsingException : Exception
{
    public NameParsingException(string message) : base(message)
    {
    }

    public NameParsingException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

public sealed class NameAlphabeticComparer : IComparer<Name>
{
    public static readonly NameAlphabeticComparer CurrentCultureIgnoreCase = new();

    public int Compare(Name? x, Name? y)
    {
        {
            if (ComparisonHelper.NullGuardCompare(x, y, out int score))
            {
                return score;
            }
        }

        foreach (var f in new EnumMembers<NameField>())
        {
            var a = NameHelper.Field(x.Fields, f);
            var b = NameHelper.Field(y.Fields, f);
            for (int i = 0; i < a.Length; i++)
            {
                var ap = a[i];
                var bp = b[i];
                if (ComparisonHelper.NullGuardCompare(ap, bp, out int score))
                {
                    if (score == 0)
                    {
                        continue;
                    }
                    return score;
                }

                var score1 = ap.CompareTo(bp, StringComparison.CurrentCultureIgnoreCase);
                if (score1 == 0)
                {
                    continue;
                }
                return score1;
            }
        }

        return 0;
    }
}
