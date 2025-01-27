using System.Globalization;
using System.Text;

namespace ScheduleLib;

public static class DiacriticsHelper
{
    // TODO: A way to do this without allocating memory.
    // https://stackoverflow.com/a/249126/9731532
    public static string RemoveDiacritics(string s)
    {
        using var buffer = new RentedBuffer<char>(s.Length);
        var normalizedString = s.Normalize(NormalizationForm.FormD);

        int writePos = 0;
        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory != UnicodeCategory.NonSpacingMark)
            {
                buffer.Span[writePos] = c;
                writePos++;
            }
        }

        return buffer.Span[.. writePos]
            .ToString()
            .Normalize(NormalizationForm.FormC);
    }

    public static string SelectOneWithMostDiacritics(string s1, string s2)
    {
        var d1 = CountDiacritics(s1);
        var d2 = CountDiacritics(s2);
        return d1 > d2 ? s1 : s2;
    }

    public static int CountDiacritics(string s)
    {
        var normalizedString = s.Normalize(NormalizationForm.FormD);
        int count = 0;
        for (int i = 0; i < normalizedString.Length; i++)
        {
            char c = normalizedString[i];
            var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
            if (unicodeCategory == UnicodeCategory.NonSpacingMark)
            {
                count++;
            }
        }
        return count;
    }
}

public sealed class IgnoreDiacriticsComparer : IEqualityComparer<string>
{
    public static readonly IgnoreDiacriticsComparer Instance = new();

    public bool Equals(string? x, string? y)
    {
        if (x is null && y is null)
        {
            return true;
        }
        if (x is null)
        {
            return false;
        }
        if (y is null)
        {
            return false;
        }
        var x1 = DiacriticsHelper.RemoveDiacritics(x);
        var y1 = DiacriticsHelper.RemoveDiacritics(y);
        return x1.Equals(y1, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(string obj)
    {
        var x = DiacriticsHelper.RemoveDiacritics(obj);
        return x.GetHashCode();
    }

    public bool Equals(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var x1 = DiacriticsHelper.RemoveDiacritics(x.ToString());
        var y1 = DiacriticsHelper.RemoveDiacritics(y.ToString());
        return Equals(x1, y1);
    }

    public bool StartsWith(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var x1 = DiacriticsHelper.RemoveDiacritics(x.ToString());
        var y1 = DiacriticsHelper.RemoveDiacritics(y.ToString());
        return x1.StartsWith(y1, StringComparison.OrdinalIgnoreCase);
    }
}
