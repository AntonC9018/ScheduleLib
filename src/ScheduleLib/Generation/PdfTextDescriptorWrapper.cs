using QuestPDF.Fluent;

namespace ScheduleLib.Generation;

public sealed class PdfTextDescriptorWrapper : IRichText
{
    private readonly TextDescriptor _descriptor;

    public PdfTextDescriptorWrapper(TextDescriptor descriptor)
    {
        _descriptor = descriptor;
    }

    public void Span(string str, bool isBold = false)
    {
        var sp = _descriptor.Span(str);
        if (isBold)
        {
            sp.Bold();
        }
    }

    public void Line(string str)
    {
        _descriptor.Line(str);
    }
}
