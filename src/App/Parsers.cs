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

public struct Parser
{
    private readonly string _input;
    private int _index;

    public Parser(string input)
    {
        _input = input;
    }

    public readonly bool IsEmpty => _index >= _input.Length;
    public readonly char Peek(int i) => _input[_index + i];
    public readonly bool CanPeek(int i) => _index + i < _input.Length;
    public readonly char Current => _input[_index];
    public void Move(int x = 1) => _index += x;
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

        int index = 0;

        char Ch() => name[index];
        char Peek(int x) => name[index + x];
        bool CheckUpper(char ch)
        {
            return ch >= 'A' && ch <= 'Z';
        }
        bool CheckLower(char ch)
        {
            return ch >= 'a' && ch <= 'z';
        }

        var (labelStart, labelLength, _, isMaster) = ParseLabel();
        string label = name[labelStart .. labelLength];
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

        (int StartIndex, int Length, bool IsFR, bool IsMaster) ParseLabel()
        {
            if (!CheckUpper(Ch()))
            {
                throw new InvalidOperationException("Must be prefixed with at least one letter indicating the group.");
            }
            index++;

            bool isMaybeMaster = false;
            bool isFr = false;

            if (Ch() == 'M')
            {
                isMaybeMaster = true;
            }

            while (true)
            {
                if (index >= name.Length)
                {
                    throw new InvalidOperationException("After the label, it must include a number!");
                }

                if (index + 2 < name.Length)
                {
                    char current = Ch();
                    char next = Peek(1);
                    char afterNext = Peek(2);
                    if (current == 'F' && next == 'R' && !CheckUpper(afterNext))
                    {
                        isFr = true;
                    }
                }

                if (!CheckUpper(Ch()))
                {
                    break;
                }

                index++;
            }

            int startIndex = 0;
            bool isMaster1 = false;
            // More than 1 letter in label
            if (index != 1)
            {
                isMaster1 = isMaybeMaster;
                if (isMaster1)
                {
                    startIndex++;
                }
            }

            return (startIndex, index, isFr, isMaster1);
        }

        int ParseYear()
        {
            int remainingLen = name.Length - index;
            if (remainingLen < 2)
            {
                throw new InvalidOperationException("String must include 2 letters of the year after the label.");
            }

            ReadOnlySpan<char> numChars = name.AsSpan(index, 2);
            if (!int.TryParse(numChars, out int ret))
            {
                throw new InvalidOperationException("Must be a valid year that has 2 letters.");
            }

            index += 2;

            return ret;
        }

        int ParseGroup()
        {
            int remainingLen = name.Length - index;
            if (remainingLen < 2)
            {
                throw new InvalidOperationException("String must include 2 letters of the group after the year.");
            }

            ReadOnlySpan<char> numChars = name.AsSpan(index, 2);
            if (!int.TryParse(numChars, out int ret))
            {
                throw new InvalidOperationException("Must be a valid number that has 2 letters.");
            }

            index += 2;

            return ret;
        }

        Language ParseLanguage()
        {
            while (true)
            {
                if (index >= name.Length)
                {
                    return Language.Ro;
                }

                if (Ch() != ' ')
                {
                    break;
                }

                index++;
            }

            {
                char ch = Ch();
                if (CheckUpper(ch))
                {
                    if (ch != 'R')
                    {
                        throw new InvalidOperationException($"Unrecognized language: {ch}");
                    }
                    index++;
                    return Language.Ru;
                }
            }

            bool isParen = false;
            if (Ch() == '(')
            {
                index++;
                isParen = true;
            }

            int offset = 0;
            int languageLen = 0;

            while (true)
            {
                if (offset + index + 1 >= name.Length)
                {
                    if (isParen)
                    {
                        throw new InvalidOperationException("Unclosed parenthesis in the language.");
                    }

                    break;
                }

                char ch = Peek(offset);
                offset++;
                if (CheckLower(ch))
                {
                    languageLen++;
                }
                else if (ch == ')' && isParen)
                {
                    break;
                }
            }

            ReadOnlySpan<char> langSpan = name.AsSpan(index, languageLen);
            index += offset;
            if (langSpan.Equals("ro", StringComparison.OrdinalIgnoreCase))
            {
                return Language.Ro;
            }
            if (langSpan.Equals("ru", StringComparison.OrdinalIgnoreCase))
            {
                return Language.Ru;
            }
            if (langSpan.Equals("en", StringComparison.OrdinalIgnoreCase))
            {
                return Language.En;
            }

            throw new InvalidOperationException($"Unrecognized language string");
        }
    }
}
