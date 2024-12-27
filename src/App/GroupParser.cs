namespace App;

public sealed class GroupParseContext
{
    // Must be modulo 100
    public required int CurrentStudyYear;

    public struct Params
    {
        public required int CurrentStudyYear;
    }

    public static GroupParseContext Create(Params p)
    {
        int year = p.CurrentStudyYear % 100;
        return new()
        {
            CurrentStudyYear = year,
        };
    }
}

public static class GroupHelper
{
    public static Group Parse(this GroupParseContext context, string name)
    {
        // M2401(ro)
        // IAFR2402
        // IAFR2403 R
        if (name.Length < 5)
        {
            throw new ArgumentException("The minimum length of the name is 5", paramName: nameof(name));
        }

        var parser = new Parser(name);

        var (label, _, isMaster) = ParseLabel();
        QualificationType qualificationType = isMaster ? QualificationType.Master : QualificationType.Licenta;
        int year = ParseYear();
        int grade = context.CurrentStudyYear - year + 1; // no validation for now.
        int group = ParseGroup();
        _ = group;
        Language language = ParseLanguage();

        return new()
        {
            Faculty = new(label),
            Grade = grade,
            Language = language,
            Name = name,
            QualificationType = qualificationType,
        };

        (string Label, bool IsFR, bool IsMaster) ParseLabel()
        {
            var bparser = parser.BufferedView();
            if (!ParserHelper.IsUpper(bparser.Current))
            {
                throw new InvalidOperationException("Must be prefixed with at least one letter indicating the group.");
            }
            bparser.Move();

            bool isMaybeMaster = false;
            bool isFr = false;

            if (bparser.Current == 'M')
            {
                isMaybeMaster = true;
            }

            while (true)
            {
                if (bparser.IsEmpty)
                {
                    throw new InvalidOperationException("After the label, it must include a number!");
                }

                if (bparser.CanPeek(2))
                {
                    var chars = bparser.PeekSpan(2);
                    if (chars[0] == 'F' && chars[1] == 'R' && !ParserHelper.IsUpper(chars[2]))
                    {
                        isFr = true;
                    }
                }

                if (!ParserHelper.IsUpper(bparser.Current))
                {
                    break;
                }

                bparser.Move();
            }

            bool isMaster1 = false;
            // More than 1 letter in label
            if (bparser.Position - parser.Position > 1)
            {
                isMaster1 = isMaybeMaster;
                if (isMaster1)
                {
                    parser.Move();
                }
            }

            var label1 = parser.PeekSpanUntilPosition(bparser.Position);
            parser.MoveTo(bparser.Position);
            return (label1.ToString(), isFr, isMaster1);
        }

        int ParseYear()
        {
            const int yearLen = 2;
            if (!parser.CanPeek(yearLen))
            {
                throw new InvalidOperationException("String must include 2 letters of the year after the label.");
            }

            ReadOnlySpan<char> numChars = parser.PeekSpan(yearLen);
            if (!int.TryParse(numChars, out int ret))
            {
                throw new InvalidOperationException("Must be a valid year that has 2 letters.");
            }

            parser.Move(yearLen);

            return ret;
        }

        int ParseGroup()
        {
            const int groupLen = 2;
            if (!parser.CanPeek(groupLen))
            {
                throw new InvalidOperationException("String must include 2 letters of the group after the year.");
            }

            ReadOnlySpan<char> numChars = parser.PeekSpan(groupLen);
            if (!int.TryParse(numChars, out int ret))
            {
                throw new InvalidOperationException("Must be a valid number that has 2 letters.");
            }

            parser.Move(groupLen);

            return ret;
        }

        Language ParseLanguage()
        {
            parser.SkipWhitespace();
            if (parser.IsEmpty)
            {
                return Language.Ro;
            }

            {
                char ch = parser.Current;
                if (ParserHelper.IsUpper(ch))
                {
                    if (ch != 'R')
                    {
                        throw new InvalidOperationException($"Unrecognized language: {ch}");
                    }
                    parser.Move();
                    return Language.Ru;
                }
            }

            bool isParen = false;
            if (parser.Current == '(')
            {
                parser.Move();
                isParen = true;
            }

            var bparser = parser.BufferedView();
            int languageLen = 0;

            while (true)
            {
                if (bparser.IsEmpty)
                {
                    if (isParen)
                    {
                        throw new InvalidOperationException("Unclosed parenthesis in the language.");
                    }

                    break;
                }

                char ch = bparser.Current;
                bparser.Move();
                if (ParserHelper.IsLower(ch))
                {
                    languageLen++;
                }
                else if (ch == ')' && isParen)
                {
                    break;
                }
            }

            ReadOnlySpan<char> langSpan = parser.PeekSpan(languageLen);
            var ret = Lang(langSpan);
            parser.MoveTo(bparser.Position);
            return ret;
        }

        Language Lang(ReadOnlySpan<char> chars)
        {
            if (chars.Equals("ro", StringComparison.OrdinalIgnoreCase))
            {
                return Language.Ro;
            }
            if (chars.Equals("ru", StringComparison.OrdinalIgnoreCase))
            {
                return Language.Ru;
            }
            if (chars.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                return Language.En;
            }
            throw new InvalidOperationException($"Unrecognized language string");
        }
    }
}
