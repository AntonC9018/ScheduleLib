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

    public static string SelectWithDiacritics(string a, string b)
    {
        if (HasDiacritics(a))
        {
            return a;
        }
        return b;
    }

    public static bool HasDiacritics(string input)
    {
        foreach (char c in input.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                return true;
            }
        }
        return false;
    }
}

public sealed class IgnoreDiacriticsAndCaseComparer :
    IEqualityComparer<string>,
    IComparer<string>,
    IAlternateEqualityComparer<ReadOnlySpan<char>, string>
{
    public static readonly IgnoreDiacriticsAndCaseComparer Instance = new();

    public bool Equals(string? x, string? y)
    {
        if (ComparisonHelper.AtLeastOneIsNull(x, y, out bool b))
        {
            return b;
        }
        var x1 = DiacriticsHelper.RemoveDiacritics(x);
        var y1 = DiacriticsHelper.RemoveDiacritics(y);
        return x1.Equals(y1, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(string obj)
    {
        var x = DiacriticsHelper.RemoveDiacritics(obj);
        return StringComparer.OrdinalIgnoreCase.GetHashCode(x);
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

    public bool Contains(ReadOnlySpan<char> x, ReadOnlySpan<char> y)
    {
        var x1 = DiacriticsHelper.RemoveDiacritics(x.ToString());
        var y1 = DiacriticsHelper.RemoveDiacritics(y.ToString());
        return x1.Contains(y1, StringComparison.OrdinalIgnoreCase);
    }

    public int Compare(string? a, string? b)
    {
        if (a is null && b is null)
        {
            return 0;
        }
        if (a is null)
        {
            return -1;
        }
        if (b is null)
        {
            return 1;
        }

        var x1 = DiacriticsHelper.RemoveDiacritics(a);
        var y1 = DiacriticsHelper.RemoveDiacritics(b);
        return string.Compare(x1, y1, StringComparison.OrdinalIgnoreCase);
    }

    public bool Equals(ReadOnlySpan<char> alternate, string other)
    {
        var alt = DiacriticsHelper.RemoveDiacritics(alternate.ToString());
        var oth = DiacriticsHelper.RemoveDiacritics(other);
        return alt.Equals(oth, StringComparison.OrdinalIgnoreCase);
    }

    public int GetHashCode(ReadOnlySpan<char> alternate)
    {
        var alt = DiacriticsHelper.RemoveDiacritics(alternate.ToString());
        return StringComparer.OrdinalIgnoreCase.GetHashCode(alt);
    }

    public string Create(ReadOnlySpan<char> alternate)
    {
        return alternate.ToString();
    }
}
