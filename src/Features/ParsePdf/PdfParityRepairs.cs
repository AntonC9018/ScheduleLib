namespace ScheduleLib.Import.Pdf;

/// <summary>A known source cell whose week-parity marker is missing.</summary>
public sealed record PdfParityRepairRule(
    string Group,
    string Day,
    string Start,
    string Course,
    string Teacher,
    string Parity)
{
    public bool Matches(PdfScheduleCell cell)
    {
        return cell.Groups.Length == 1
            && cell.Groups[0] == Group
            && cell.Day == Day
            && cell.Start == Start
            && cell.Text.Contains(Course)
            && cell.Text.Contains(Teacher);
    }
}

/// <summary>Parity repairs verified against the source PDFs of one schedule period.</summary>
/// <param name="Reference">Where the repair decisions are documented; reported on repaired cells.</param>
public sealed record PdfParityRepairs(string Reference, IReadOnlyList<PdfParityRepairRule> Rules)
{
    public static readonly PdfParityRepairs None = new(Reference: "", Rules: []);
}
