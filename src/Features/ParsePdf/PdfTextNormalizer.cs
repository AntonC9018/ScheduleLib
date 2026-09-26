using System.Text.RegularExpressions;

namespace ScheduleLib.Import.Pdf;

public static class PdfTextNormalizer
{
    public static string Normalize(string text)
    {
        text = string.Join('\n', text.Split('\n').Select(l => Regex.Replace(l, @"[ \t]+", " ").Trim()));
        text = Regex.Replace(text, @"S\s*e\s*c\s*u\s*r\s*i\s*t\s*a\s*t\s*e\s*a\s+c\s*i\s*b\s*e\s*r\s*n\s*e\s*t\s*i\s*c\s*ă", "Securitatea cibernetică");
        text = text.Replace("UI/U X", "UI/UX").Replace("145a/4/4", "145a/4")
            .Replace("Disciplină umanistică opțională: ", "").Replace("cal cul.", "calcul.")
            .Replace("Tehnol.progr.v", "Tehnol.progr.").Replace(" (IȘE)", "");
        text = Regex.Replace(text, @",\s*\)", ")");
        text = Regex.Replace(text, @"(?m)(\([^()]*\))\)+(?=$|\s)", "$1");
        text = Regex.Replace(text, @",\s*", ", ");
        return Regex.Replace(text, @"(?<=[^\s.)])\n(?=[a-zăâîșț]+(?: |\())", " ");
    }

    public static IEnumerable<PdfScheduleCell> ExpandCohorts(PdfScheduleCell cell)
    {
        var lines = cell.Text.Split('\n');
        var pattern = new Regex(@"^Gr\.(\d+)\s*\((.*?)\)\s*:\s*(.+)$");
        if (!lines.Any(pattern.IsMatch)) { yield return cell; yield break; }
        var course = lines[0];
        if (!course.Contains("Antreprenoriat inovativ")) throw new FormatException($"Unknown cohort course: {cell.Text}");
        var remaining = new List<string>();
        foreach (var line in lines.Skip(1))
        {
            var match = pattern.Match(line);
            if (!match.Success) { remaining.Add(line); continue; }
            var names = Regex.Matches(match.Groups[2].Value, @"\[(.*?)\]").Select(m => m.Groups[1].Value).ToArray();
            var groups = cell.Groups.Where(g => names.Contains(g.Split('(')[0])).ToArray();
            if (groups.Length == 0 || groups.Length != names.Length) throw new FormatException($"Unknown cohort membership: {line}");
            yield return cell with { Groups = groups, Text = course + "\n" + match.Groups[3].Value,
                Alternative = "Antreprenoriat Gr." + match.Groups[1].Value, Cohort = match.Groups[2].Value };
        }
        if (remaining.Count > 0) yield return cell with { Text = string.Join('\n', remaining) };
    }

    public static PdfScheduleCell RepairMissingParity(PdfScheduleCell cell)
    {
        // Source decisions in docs/wayfinder/2026-sem1-clash-fixes.md on the domain-model branch.
        (string Group, string Day, string Start, string Course, string Teacher, string Parity)[] rules =
        [
            ("IA2603(ru)", "Joi", "13:15", "Limba straina", "G.Ciudin", "par"),
            ("IA2604(ru)", "Joi", "13:15", "Limba straina", "O.Basirov", "imp"),
            ("I2502(ru)", "Vineri", "11:30", "Baze de date", "Cr.Ulmanu", "imp"),
        ];
        foreach (var rule in rules)
        {
            if (cell.Groups.Length != 1 || cell.Groups[0] != rule.Group || cell.Day != rule.Day || cell.Start != rule.Start
                || !cell.Text.Contains(rule.Course) || !cell.Text.Contains(rule.Teacher)) continue;
            var lines = cell.Text.Split('\n');
            if (Regex.IsMatch(lines[0], @"\b(?:imp|par)\b")) continue;
            lines[0] = lines[0].Contains(')') ? lines[0].Replace(")", $", {rule.Parity})") : lines[0] + $" ({rule.Parity})";
            return cell with { Text = string.Join('\n', lines), Repair = $"Missing {rule.Parity} marker; documented in 2026-sem1-clash-fixes.md" };
        }
        return cell;
    }
}
