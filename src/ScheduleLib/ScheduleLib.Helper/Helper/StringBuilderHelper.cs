using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace ScheduleLib;

public static class StringBuilderHelper
{
    public static string ToStringAndClear(this StringBuilder sb)
    {
        var ret = sb.ToString();
        sb.Clear();
        return ret;
    }

    public static bool EndsWith(this StringBuilder sb, string text)
    {
        if (sb.Length < text.Length)
        {
            return false;
        }

        var sbLength = sb.Length;
        var textLength = text.Length;
        for (int i = 1; i <= textLength; i++)
        {
            if (text[textLength - i] != sb[sbLength - i])
            {
                return false;
            }
        }
        return true;
    }
}

public readonly struct ListStringBuilder(
    StringBuilder sb,
    string separator = " ")
{
    private readonly int _initialCount = sb.Length;
    public StringBuilder StringBuilder => sb;

    public void MaybeAppendSeparator()
    {
        if (sb.Length <= _initialCount)
        {
            return;
        }
        if (!sb.EndsWith(separator))
        {
            sb.Append(separator);
        }
    }

    public void Append(ReadOnlySpan<char> s)
    {
        MaybeAppendSeparator();
        sb.Append(s);
    }

    public void Append([InterpolatedStringHandlerArgument("")] ref WriteInterpolatedStringHandler handler)
    {
        _ = this;
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    [InterpolatedStringHandler]
    public readonly ref struct WriteInterpolatedStringHandler
    {
        private readonly ListStringBuilder writer;

        public WriteInterpolatedStringHandler(int literalLength, int formattedCount, ListStringBuilder writer)
        {
            _ = literalLength;
            _ = formattedCount;
            this.writer = writer;

            writer.MaybeAppendSeparator();
        }

        public void AppendLiteral(string value)
        {
            AppendFormatted(value.AsSpan());
        }

        public void AppendFormatted(string? value)
        {
            if (value is not null)
            {
                AppendFormatted(value.AsSpan());
            }
        }

        public void AppendFormatted(ReadOnlySpan<char> value)
        {
            this.writer.StringBuilder.Append(value);
        }

        public void AppendFormatted<T>(T value)
        {
            AppendFormatted(value, null);
        }

        public void AppendFormatted<T>(T value, string? format)
        {
            if (value is ISpanFormattable spanFormattable)
            {
                AppendFormatted(spanFormattable.ToString(format, CultureInfo.InvariantCulture));
            }
            else if (value is IFormattable formattable)
            {
                AppendFormatted(formattable.ToString(format, CultureInfo.InvariantCulture));
            }
            else if (value is not null)
            {
                AppendFormatted(value.ToString());
            }
        }
    }
}
