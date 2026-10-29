using System.Text.RegularExpressions;

namespace ScheduleLib.Import.Pdf;

public static class PdfTextNormalizer
{
    /// <summary>A known cell whose study parity has to be restored after import.</summary>
    private readonly record struct ParityRepairRule(
        string Group,
        string Day,
        string Start,
        string Course,
        string Teacher,
        string Parity);

    public static string Normalize(string text)
    {
        char separator = '\n';
        string[] split = text.Split('\n');
        Func<string, string> selector = l => Regex.Replace(
                l,
                @"[ \t]+",
                " ")
            .Trim();
        var values = split.Select(selector);
        text = string.Join(separator, values);
        text = Regex.Replace(
            text,
            @"S\s*e\s*c\s*u\s*r\s*i\s*t\s*a\s*t\s*e\s*a\s+c\s*i\s*b\s*e\s*r\s*n\s*e\s*t\s*i\s*c\s*ă",
            "Securitatea cibernetică");
        text = text.Replace("UI/U X", "UI/UX")
            .Replace("145a/4/4", "145a/4")
            .Replace("Disciplină umanistică opțională: ", "")
            .Replace("cal cul.", "calcul.")
            .Replace("Tehnol.progr.v", "Tehnol.progr.")
            .Replace(" (IȘE)", "");
        text = Regex.Replace(
            text,
            @",\s*\)",
            ")");
        text = Regex.Replace(
            text,
            @"(?m)(\([^()]*\))\)+(?=$|\s)",
            "$1");
        text = Regex.Replace(
            text,
            @",\s*",
            ", ");
        return Regex.Replace(
            text,
            @"(?<=[^\s.)])\n(?=[a-zăâîșț]+(?: |\())",
            " ");
    }

    public static IEnumerable<PdfScheduleCell> ExpandCohorts(PdfScheduleCell cell)
    {
        var lines = cell.Text.Split('\n');
        var pattern = new Regex(@"^Gr\.(\d+)\s*\((.*?)\)\s*:\s*(.+)$");
        if (!lines.Any(pattern.IsMatch))
        {
            yield return cell;
            yield break;
        }
        var course = lines[0];
        if (!course.Contains("Antreprenoriat inovativ"))
        {
            throw new UnknownPdfCohortCourseException(cell.Text);
        }

        var remaining = new List<string>();
        foreach (var line in lines.Skip(1))
        {
            var match = pattern.Match(line);
            if (!match.Success)
            {
                remaining.Add(line);
                continue;
            }
            var names = Regex.Matches(input: match.Groups[2].Value, pattern: @"\[(.*?)\]")
                .Select(m => m.Groups[1].Value)
                .ToArray();
            var groups = cell.Groups.Where(g =>
                {
                    string CalculateValue()
                    {
                        string[] split = g.Split('(');
                        int int32 = 0;
                        string value = split[int32];
                        return value;
                    }

                    return names.Contains(CalculateValue());
                })
                .ToArray();
            if (groups.Length == 0 || groups.Length != names.Length)
            {
                throw new UnknownPdfCohortMembershipException(line);
            }

            yield return cell with
            {
                Groups = groups,
                Text = $"{course}\n{match.Groups[3].Value}",
                Alternative = $"Antreprenoriat Gr.{match.Groups[1].Value}",
                Cohort = match.Groups[2].Value,
            };
        }
        if (remaining.Count > 0)
        {
            yield return cell with
            {
                Text = string.Join('\n', remaining),
            };
        }
    }

    public static PdfScheduleCell RepairMissingParity(PdfScheduleCell cell)
    {
        // Source decisions in docs/wayfinder/2026-sem1-clash-fixes.md on the domain-model branch.
        ParityRepairRule[] rules =
        [
            new(
                Group: "IA2603(ru)",
                Day: "Joi",
                Start: "13:15",
                Course: "Limba straina",
                Teacher: "G.Ciudin",
                Parity: "par"),
            new(
                Group: "IA2604(ru)",
                Day: "Joi",
                Start: "13:15",
                Course: "Limba straina",
                Teacher: "O.Basirov",
                Parity: "imp"),
            new(
                Group: "I2502(ru)",
                Day: "Vineri",
                Start: "11:30",
                Course: "Baze de date",
                Teacher: "Cr.Ulmanu",
                Parity: "imp"),
        ];
        foreach (var rule in rules)
        {
            bool CheckCondition()
            {
                if (cell.Groups.Length != 1)
                {
                    return true;
                }

                if (cell.Groups[0] != rule.Group)
                {
                    return true;
                }

                if (cell.Day != rule.Day)
                {
                    return true;
                }

                if (cell.Start != rule.Start)
                {
                    return true;
                }

                if (!cell.Text.Contains(rule.Course))
                {
                    return true;
                }

                if (!cell.Text.Contains(rule.Teacher))
                {
                    return true;
                }

                return false;
            }

            if (CheckCondition())
            {
                continue;
            }

            var lines = cell.Text.Split('\n');
            if (Regex.IsMatch(input: lines[0], pattern: @"\b(?:imp|par)\b"))
            {
                continue;
            }

            string CalculateNewValue()
            {
                string newValue = $", {rule.Parity})";
                return newValue;
            }

            lines[0] = lines[0].Contains(')')
                ? lines[0].Replace(")", CalculateNewValue())
                : $"{lines[0]}{$" ({rule.Parity})"}";
            return cell with
            {
                Text = string.Join('\n', lines),
                Repair = $"Missing {rule.Parity} marker; documented in 2026-sem1-clash-fixes.md",
            };
        }
        return cell;
    }
}
