using System.Runtime.InteropServices;
using ScheduleLib;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;

namespace Desktop.MainWindow;

public readonly record struct MatchScore(
    EnumBitArray<NameField> FullMatch,
    EnumBitArray<NameField> PartialMatch,
    int UnmatchedCount)
{
    public EnumBitArray<NameField> PartialMatch { get; } = FullMatch.UnionWith(PartialMatch);

    private struct MatchScoreFields
    {
        public EnumBitArray<NameField> FullMatch;
        public EnumBitArray<NameField> PartialMatch;
        public int UnmatchedCount;
    }

    public static MatchScore Create(
        Name name,
        List<NameParts<string?>> parsedInputParts)
    {
        var ret = new MatchScoreFields();
        var nameFields = name.Fields;

        foreach (var parsedPart in CollectionsMarshal.AsSpan(parsedInputParts))
        {
            if (!Match())
            {
                ret.UnmatchedCount++;
            }

            bool Match()
            {
                bool somethingMatched = false;
                foreach (var existingField in new EnumMembers<NameField>())
                {
                    var existingFieldValue = NameHelper.Field(nameFields, existingField);
                    if (existingFieldValue == default)
                    {
                        continue;
                    }

                    UnsizedBitArray32 exactlyEqual = new();
                    UnsizedBitArray32 partiallyEqual = new();
                    for (int i = 0; i < existingFieldValue.Length; i++)
                    {
                        var p = parsedPart[i];
                        var e = existingFieldValue[i];
                        if (p == null && e == null)
                        {
                            exactlyEqual.Set(i);
                            partiallyEqual.Set(i);
                            break;
                        }
                        if (p == null)
                        {
                            break;
                        }
                        if (e == null)
                        {
                            break;
                        }

                        var wp = new Word(p).Span.Shortened;
                        var we = new Word(e).Span.Shortened;
                        var comparisonResult = wp.Compare(we);
                        if (comparisonResult
                            is CompareShortenedWordsResult.Equal_Exactly
                            or CompareShortenedWordsResult.Equal_SecondBetter)
                        {
                            exactlyEqual.Set(i);
                        }
                        if (comparisonResult.IsEqual())
                        {
                            partiallyEqual.Set(i);
                        }
                    }

                    var allMask = UnsizedBitArray32.GetMask(existingFieldValue.Length);
                    if (exactlyEqual == allMask)
                    {
                        ret.FullMatch.Set(existingField);
                        somethingMatched = true;
                    }
                    else if (partiallyEqual == allMask)
                    {
                        somethingMatched = true;
                        ret.PartialMatch.Set(existingField);
                    }
                }
                return somethingMatched;
            }
        }
        return new(
            FullMatch: ret.FullMatch,
            PartialMatch: ret.PartialMatch,
            UnmatchedCount: ret.UnmatchedCount);
    }

    public readonly int AsInt()
    {
        int full = FullMatch.SetCount;
        int partial = PartialMatch.SetCount;
        int ret = full + partial - UnmatchedCount;
        return ret;
    }

    public override string ToString()
    {
        return AsInt().ToString();
    }
}
