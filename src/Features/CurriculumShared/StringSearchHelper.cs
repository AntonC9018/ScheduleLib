using System.Collections.Immutable;
using System.Diagnostics;
using DocumentFormat.OpenXml.Wordprocessing;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;

namespace ScheduleLib.Curriculum;

public static class StringSearchHelper
{
    public static SectionParseResult Search<TPreprocess>(
        Paragraph para,
        TPreprocess preprocess,
        ImmutableArray<string> strings)

        where TPreprocess : IPreprocess
    {
        var potentialSectionTypes = BitArray32.AllSet(strings.Length);
        using var readPositions = new RentedBuffer<int>(strings.Length);
        readPositions.Span.Fill(0);
        using var extraStuffFromMatch = new RentedBuffer<ReadOnlyMemory<char>>(strings.Length);
        extraStuffFromMatch.Span.Fill(null);

        // It might be split up into multiple text segments, have to check each.
        bool isFirstCheck = true;
        foreach (var textItem in para.Descendants<Text>())
        {
            var parser = new Parser(textItem.Text);
            if (parser.SkipWhitespace().EndOfInput)
            {
                continue;
            }

            preprocess.Preprocess(ref parser);

            var remainingMem = parser.SourceUntilEnd().Trim();
            var remainingSpan = remainingMem.Span;
            if (remainingSpan.Length == 0)
            {
                continue;
            }

            isFirstCheck = false;

            // For now, check for an exact equality.
            // Maybe look for keywords later?
            foreach (var sectionIndex in potentialSectionTypes.SetBitIndicesLowToHigh)
            {
                ref var refStartIndex = ref readPositions.Array[sectionIndex];
                var sectionsString = strings[sectionIndex];
                var currentSlice = sectionsString.AsSpan(refStartIndex);

                // Partial match is still a match.
                var longer = currentSlice;
                var shorter = remainingSpan;
                if (longer.Length < shorter.Length)
                {
                    var t = longer;
                    longer = shorter;
                    shorter = t;
                }

                if (IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(longer, shorter))
                {
                    // TODO: This is pretty hard to implement correctly.
                    // I need to get the character positions IN THE ORIGINAL string.
                    // This is currently NOT CORRECT.
                    refStartIndex += shorter.Length;
                    var p = new Parser(sectionsString);
                    p.MoveTo(new(refStartIndex));
                    p.SkipWhitespace();
                    refStartIndex = p.Position.Index;

                    if (p.IsEmpty && remainingMem.Length > currentSlice.Length)
                    {
                        ref var x = ref extraStuffFromMatch.Array[sectionIndex];
                        if (x.IsEmpty)
                        {
                            x = remainingMem[currentSlice.Length ..];
                        }
                    }
                }
                else
                {
                    potentialSectionTypes.Unset(sectionIndex);
                }
            }

            if (potentialSectionTypes.IsEmpty)
            {
                return SectionParseResult.CreateUnknown(
                    unmatchedText: parser.SourceUntilEnd());
            }
        }

        if (isFirstCheck)
        {
            // Not a single Text descendant.
            return SectionParseResult.CreateNotHeading();
        }

        if (potentialSectionTypes.SetCount > 1)
        {
            return SectionParseResult.CreateUnknown();
        }

        foreach (var sectionIndex in potentialSectionTypes.SetBitIndicesLowToHigh)
        {
            var str = strings[sectionIndex];
            var start = readPositions.Array[sectionIndex];
            Debug.Assert(start != 0, "Can only happen if only checked empty strings");

            if (str.Length != start)
            {
                return SectionParseResult.CreatePartialMatch(
                    sectionIndex,
                    missingText: str.AsMemory(start));
            }

            var x = extraStuffFromMatch.Array[sectionIndex];
            if (!x.IsEmpty)
            {
                return SectionParseResult.CreatePartialMatch(
                    sectionIndex,
                    unmatchedText: x);
            }

            return SectionParseResult.CreateOk(sectionIndex);
        }

        throw Unreachable();
    }

    public static void DefaultHandleError(SectionParseResult x)
    {
        if (x.IsEmpty)
        {
            return;
        }
        if (x.IsUnknown)
        {
            throw new NotSupportedException($"Unrecognized heading '{x.UnmatchedText}'.");
        }
        if (!x.MissingText.IsEmpty)
        {
            throw new NotSupportedException($"Partially matched heading '{x.MissingText}'.");
        }
    }

    public struct SearchArrayBuilder<T, U> where T : struct, Enum
    {
        internal readonly ImmutableArray<U>.Builder _builder;

        public SearchArrayBuilder()
        {
            _builder = ImmutableArray.CreateBuilder<U>(EnumMembers<T>.Count);
            _builder.Count = _builder.Capacity;
        }

        public readonly void Set(T tag, U str)
        {
            int index = EnumMembers<T>.EnumAsInt(tag);
            Debug.Assert(index >= 0 && index < _builder.Capacity);
            _builder[index] = str;
        }
    }
    public delegate void BuilderDelegate<T, U>(SearchArrayBuilder<T, U> builder) where T : struct, Enum;

    public static ImmutableArray<string> SetupSearchArray<T>(BuilderDelegate<T, string> f) where T : struct, Enum
    {
        return SetupSearchArray<T, string>(f);
    }
    public static ImmutableArray<U> SetupSearchArray<T, U>(BuilderDelegate<T, U> f) where T : struct, Enum
    {
        var builder = new SearchArrayBuilder<T, U>();
        f(builder);
        if (!builder._builder.All(x => x is not null))
        {
            Debug.Fail("Some members not initialized");
        }
        var ret = builder._builder.MoveToImmutable();
        return ret;
    }
}

public interface IPreprocess
{
    public void Preprocess(ref Parser parser);
}
