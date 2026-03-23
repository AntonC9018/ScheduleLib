using System.Diagnostics;
using System.Text;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Parsing;

public enum NameField
{
    First,
    Last,
    Partonymic,
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
        catch (InvalidOperationException)
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

        ret.Patronymic = ParseNamePart(ref blexer, "Patronymic incomplete");
        if (ret.Patronymic == default)
        {
            return new(ret);
        }

        lexer.MoveTo(blexer.Position);
        lexer.Apply();

        return new(ret);

        static void IgnoreParenthesizedTextAndWhitespace(ref LexerScope lexer)
        {
            lexer.ConsumeMultiple([TokenType.Whitespace]);
            if (lexer.TryConsume(NameTokenType.ParenthesizedText))
            {
                lexer.ConsumeMultiple([TokenType.Whitespace]);
            }
        }

        static void ErrorOnInvalidToken(ref LexerScope lexer)
        {
            if (!lexer.IsEmpty && lexer.Current.Type == TokenType.Invalid)
            {
                throw new InvalidOperationException($"Invalid token {lexer.Current.Value}");
            }
        }

        static NameParts<string?> ParseRequiredNamePart(
            ref LexerScope lexer,
            string requiredError,
            string incompleteError)
        {
            var ret = ParseNamePart(ref lexer, incompleteError);
            if (ret == default)
            {
                throw new InvalidOperationException(requiredError);
            }
            return ret;
        }

        static NameParts<string?> ParseNamePart(ref LexerScope lexer, string incompleteError)
        {
            var ret = new NameParts<string?>();

            bool ParsePartOfPart(ref LexerScope lexer, out string? ret)
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

            if (!ParsePartOfPart(ref lexer, out ret[0]))
            {
                return ret;
            }

            if (lexer.TryConsume(NameTokenType.DoubleNameSeparator))
            {
                if (!ParsePartOfPart(ref lexer, out ret[1]))
                {
                    throw new InvalidOperationException(incompleteError);
                }
            }
            return ret;
        }
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
            throw new InvalidOperationException("Extra characters after name");
        }
        return ret;
    }

    public static ref NameParts<string?> Field(ref NameFields fields, NameField field)
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
            case NameField.Partonymic:
            {
                return ref fields.Patronymic;
            }
            default:
            {
                throw Unreachable();
            }
        }
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

