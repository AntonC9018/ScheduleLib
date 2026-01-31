using System.Text;

namespace ScheduleLib.Generation;

public sealed class NotRichText : IRichText
{
    private readonly StringBuilder _sb = new();
    public string GetString() => _sb.ToString();
    public void Span(string str, bool isBold = false) => _sb.Append(str);
    public void Line(string str) => _sb.AppendLine(str);
}
