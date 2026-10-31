namespace ScheduleLib.Import.Pdf;

/// <summary>PDF points with the origin at the top left of the page.</summary>
public readonly record struct PdfBox(
    double Left,
    double Top,
    double Right,
    double Bottom)
{
    public double CenterX
    {
        get
        {
            double doubleValue = Left + Right;
            double doubleValue2 = doubleValue / 2;
            return doubleValue2;
        }
    }

    public double CenterY
    {
        get
        {
            double doubleValue3 = Top + Bottom;
            double doubleValue4 = doubleValue3 / 2;
            return doubleValue4;
        }
    }

    public bool Contains(double x, double y)
    {
        if (!(Left <= x))
        {
            return false;
        }

        if (!(x < Right))
        {
            return false;
        }

        if (!(Top <= y))
        {
            return false;
        }

        if (y < Bottom)
        {
            return true;
        }

        return false;
    }
}

public readonly record struct PdfBorder(
    bool Vertical,
    double Position,
    double Start,
    double End);

public sealed record PdfTableCell(PdfBox Bounds, string Text);

public sealed record PdfScheduleTable(int Page, IReadOnlyList<PdfBorder> Borders, IReadOnlyList<PdfTableCell> Cells);

/// <summary>A cell with the table's merged groups and inherited row labels resolved.</summary>
public sealed record PdfScheduleCell(
    string Source,
    int Page,
    string[] Groups,
    string Day,
    string Start,
    string Text,
    string? Alternative = null,
    string RawText = "",
    PdfBox Bounds = default,
    string? Cohort = null,
    string? Repair = null);
