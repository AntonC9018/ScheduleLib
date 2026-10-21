using ScheduleLib.Builders;
using ScheduleLib.Parsing.Lesson;

namespace ScheduleLib.ParserTests;

internal static class LessonParserHelper
{
    public static void AssertEqualName(string expected, TeacherName actual)
    {
        Assert.True(CheckEqualName(expected, actual));
    }

    public static bool CheckEqualName(string expected, TeacherName actual)
    {
        var expectedName = TeacherNameHelper.ParseName(expected);
        if (!ShortNameEqual())
        {
            return false;
        }
        if (!LastNameEqual())
        {
            return false;
        }
        return true;

        bool ShortNameEqual()
        {
            return expectedName.FirstName.EachEquals(actual.FirstName, (e, a) =>
            {
                if (e.IsNull)
                {
                    if (!a.IsEmpty)
                    {
                        return false;
                    }
                    return true;
                }

                var e1 = (e.Full ?? e.Short)!.AsSpan();
                var a1 = new WordSpan(a.Span).Value;
                if (!e1.Equals(a1, StringComparison.Ordinal))
                {
                    return false;
                }
                return true;
            });
        }

        bool LastNameEqual()
        {
            if (actual.LastName.All(x => x.IsEmpty))
            {
                return expectedName.LastName == default;
            }

            return expectedName.LastName.Parts.EachEquals(actual.LastName, (a, b) =>
            {
                var a1 = a.AsSpan();
                var b1 = b.Span;
                return a1.Equals(b1, StringComparison.Ordinal);
            });
        }
    }

}
