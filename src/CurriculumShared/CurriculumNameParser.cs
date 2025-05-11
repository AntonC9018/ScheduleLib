using ScheduleLib.Parsing;

namespace ScheduleLib.Curriculum;

public sealed class CurriculumGroupKey
{
    public required QualificationType QualificationType { get; init; }
    public required AttendanceModeFlags AttendanceModes { get; init; }
    public required string Code { get; init; }
    public required string Name { get; init; }
}

public static class CurriculumNameParser
{
    public static CurriculumGroupKey ParseGroupKey(string name)
    {
        var parser = new Parser(name);
        var qual = Qualification(ref parser);
        parser.SkipWhitespace();
        var code = Code(ref parser);
        parser.SkipWhitespace();

        ParserPosition lastSegmentPos = default;
        ParserPosition spacePos = default;
        bool hadSpaces = false;
        {
            var bparser = parser.BufferedView();
            while (true)
            {
                var r = bparser.SkipNotWhitespace();
                if (r.EndOfInput)
                {
                    break;
                }
                hadSpaces = true;
                spacePos = bparser.Position;
                bparser.SkipWhitespace();
                lastSegmentPos = bparser.Position;
            }
        }

        var attendanceModes = AttendanceModeFlags.None;

        // process last segment
        if (hadSpaces)
        {
            var lastSegmentParser = parser.BufferedView();
            lastSegmentParser.MoveTo(lastSegmentPos);
            var lastSegment = lastSegmentParser.SourceUntilEnd();
            attendanceModes = AttendanceModes(lastSegment.Span);
        }

        ReadOnlyMemory<char> nameSegment;
        {
            var nameParser = parser.BufferedView();
            if (attendanceModes == AttendanceModeFlags.None)
            {
                nameSegment = nameParser.SourceUntilEnd();
            }
            else
            {
                nameSegment = nameParser.SourceUntilExclusive(spacePos);
            }
        }

        return new()
        {
            Code = code.ToString(),
            Name = nameSegment.ToString(),
            AttendanceModes = attendanceModes,
            QualificationType = qual,
        };

        static AttendanceModeFlags AttendanceModes(ReadOnlySpan<char> lastSpan)
        {
            var ret = AttendanceModeFlags.None;
            int itemCount = 0;
            bool hasUnparsedItem = false;
            foreach (var x in lastSpan.Split('+'))
            {
                var segment = lastSpan[x];
                if (segment.Length == 0)
                {
                    continue;
                }

                itemCount++;

                var maybeMode = GetMode(segment);
                if (maybeMode is not { } mode)
                {
                    if (hasUnparsedItem)
                    {
                        break;
                    }
                    hasUnparsedItem = true;
                    continue;
                }


                var flag = (AttendanceModeFlags) (1 << (int) mode);
                if ((ret & flag) == flag)
                {
                    throw new NotSupportedException("The same attendance mode specified a second time");
                }
                ret |= flag;
                continue;

                static AttendanceMode? GetMode(ReadOnlySpan<char> s)
                {
                    if (s.SequenceEqual("zi"))
                    {
                        return AttendanceMode.Zi;
                    }
                    if (s.SequenceEqual("fr"))
                    {
                        return AttendanceMode.FrecventaRedusa;
                    }
                    return null;
                }
            }

            if (hasUnparsedItem && itemCount > 1)
            {
                throw new NotSupportedException("'+' in the last segment without it parsing");
            }

            return ret;
        }

        static ReadOnlyMemory<char> Code(ref Parser parser)
        {
            var bparser = parser.BufferedView();

            var skipResult = bparser.SkipNumbers();
            if (!skipResult.SkippedAny)
            {
                throw new NotSupportedException("Expected code");
            }
            if (skipResult.EndOfInput)
            {
                throw new NotSupportedException("Expected '.' after first part of code");
            }
            if (bparser.Current == '.')
            {
                bparser.Move();

                var secondPartSkipResult = bparser.SkipNumbers();
                if (!secondPartSkipResult.SkippedAny)
                {
                    throw new NotSupportedException("Expected number after '.'");
                }
            }

            var ret = parser.SourceUntilExclusive(bparser);
            parser.MoveTo(bparser.Position);
            return ret;
        }

        static QualificationType Qualification(ref Parser parser)
        {
            var bparser = parser.BufferedView();
            if (!bparser.SkipLetters().SkippedAny)
            {
                return QualificationType.Licenta;
            }

            var s = parser.PeekSpanUntilPosition(bparser.Position);
            parser.MoveTo(bparser.Position);

            if (IgnoreDiacriticsComparer.Instance.Equals(s, "master"))
            {
                return QualificationType.Master;
            }

            throw new NotSupportedException("Unknown qualification");
        }
    }
}
