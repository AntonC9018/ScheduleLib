using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace ScheduleLib.Import.Pdf;

public static class PdfTableReader
{
    private const double Tolerance = 3;

    public static IReadOnlyList<PdfScheduleTable> Read(Stream input)
    {
        using var document = PdfDocument.Open(input);
        return document.GetPages().Select(ReadPage).ToArray();
    }

    private static PdfScheduleTable ReadPage(Page page)
    {
        var raw = new List<PdfBorder>();
        foreach (var path in page.Paths)
        foreach (var subpath in path)
        {
            foreach (var line in subpath.Commands.OfType<PdfSubpath.Line>())
            {
                if (Math.Abs(line.From.X - line.To.X) < 1)
                    raw.Add(new(true, line.From.X, page.Height - Math.Max(line.From.Y, line.To.Y), page.Height - Math.Min(line.From.Y, line.To.Y)));
                else if (Math.Abs(line.From.Y - line.To.Y) < 1)
                    raw.Add(new(false, page.Height - line.From.Y, Math.Min(line.From.X, line.To.X), Math.Max(line.From.X, line.To.X)));
            }
        }
        var borders = JoinBorders(raw.Where(b => b.End - b.Start >= 3));
        var boxes = RecoverPartialBorders(FindCells(borders), borders);
        if (boxes.Count == 0)
            throw new FormatException($"Page {page.Number}: no bordered schedule table found.");
        return new(page.Number, borders, boxes.Select(b => new PdfTableCell(b, ReadText(page, b))).ToArray());
    }

    public static IReadOnlyList<PdfBorder> JoinBorders(IEnumerable<PdfBorder> borders)
    {
        var result = new List<PdfBorder>();
        foreach (var orientation in borders.GroupBy(b => b.Vertical))
        {
            var clusters = new List<List<PdfBorder>>();
            foreach (var border in orientation.OrderBy(b => b.Position))
            {
                if (clusters.Count == 0 || border.Position - clusters[^1][^1].Position > Tolerance)
                    clusters.Add([]);
                clusters[^1].Add(border);
            }
            foreach (var cluster in clusters)
            {
                var position = cluster.Average(b => b.Position);
                PdfBorder? current = null;
                foreach (var border in cluster.OrderBy(b => b.Start))
                {
                    if (current is { } previous && border.Start <= previous.End + Tolerance)
                        current = previous with { End = Math.Max(previous.End, border.End) };
                    else
                    {
                        if (current is { } completed) result.Add(completed);
                        current = border with { Position = position };
                    }
                }
                if (current is { } last) result.Add(last);
            }
        }
        return result;
    }

    public static IReadOnlyList<PdfBox> FindCells(IReadOnlyList<PdfBorder> borders)
    {
        var vertical = borders.Where(b => b.Vertical).ToArray();
        var horizontal = borders.Where(b => !b.Vertical).ToArray();
        var points = (from v in vertical from h in horizontal
                      where v.Start <= h.Position + Tolerance && v.End >= h.Position - Tolerance
                         && h.Start <= v.Position + Tolerance && h.End >= v.Position - Tolerance
                      select (X: v.Position, Y: h.Position)).Distinct().OrderBy(p => p.X).ThenBy(p => p.Y).ToArray();
        var result = new List<PdfBox>();
        foreach (var point in points)
        {
            var below = points.Where(p => p.X == point.X && p.Y > point.Y);
            var right = points.Where(p => p.Y == point.Y && p.X > point.X);
            bool found = false;
            foreach (var bottom in below)
            {
                if (!Connects(vertical, point.X, point.Y, bottom.Y)) continue;
                foreach (var end in right)
                {
                    if (!Connects(horizontal, point.Y, point.X, end.X)
                        || !Connects(vertical, end.X, point.Y, bottom.Y)
                        || !Connects(horizontal, bottom.Y, point.X, end.X)) continue;
                    result.Add(new(point.X, point.Y, end.X, bottom.Y));
                    found = true;
                    break;
                }
                if (found) break;
            }
        }
        return result;
    }

    private static bool Connects(IEnumerable<PdfBorder> borders, double position, double start, double end) =>
        borders.Any(b => b.Position == position && b.Start <= start + Tolerance && b.End >= end - Tolerance);

    public static IReadOnlyList<PdfBox> RecoverPartialBorders(IEnumerable<PdfBox> cells, IReadOnlyList<PdfBorder> borders)
    {
        var result = new List<PdfBox>();
        foreach (var box in cells)
        {
            var internalBorders = borders.Where(b => b.Vertical && box.Left + 2 < b.Position && b.Position < box.Right - 2
                && Math.Min(b.End, box.Bottom) - Math.Max(b.Start, box.Top) > 5).ToArray();
            if (internalBorders.Length == 0) { result.Add(box); continue; }
            var ys = internalBorders.SelectMany(b => new[] { b.Start, b.End })
                .Where(y => box.Top + 2 < y && y < box.Bottom - 2).Append(box.Top).Append(box.Bottom).Distinct().Order().ToArray();
            for (int row = 1; row < ys.Length; row++)
            {
                var middle = (ys[row - 1] + ys[row]) / 2;
                var xs = internalBorders.Where(b => b.Start - 2 <= middle && middle <= b.End + 2)
                    .Select(b => b.Position).Append(box.Left).Append(box.Right).Distinct().Order().ToArray();
                for (int column = 1; column < xs.Length; column++)
                    result.Add(new(xs[column - 1], ys[row - 1], xs[column], ys[row]));
            }
        }
        return result.Distinct().ToArray();
    }

    private static string ReadText(Page page, PdfBox bounds)
    {
        var letters = page.Letters.Where(l => bounds.Contains(
            (l.BoundingBox.Left + l.BoundingBox.Right) / 2,
            page.Height - (l.BoundingBox.Top + l.BoundingBox.Bottom) / 2)).ToArray();
        if (letters.Length == 0) return "";
        // Rotated day labels read from the bottom of the page towards the top.
        if (letters.Count(l => Math.Abs(l.EndBaseLine.Y - l.StartBaseLine.Y) > Math.Abs(l.EndBaseLine.X - l.StartBaseLine.X)) > letters.Length / 2)
            return string.Concat(letters.OrderBy(l => l.StartBaseLine.Y).Select(l => l.Value)).Trim();
        var rows = new List<List<Letter>>();
        foreach (var letter in letters.OrderByDescending(l => l.StartBaseLine.Y))
        {
            if (rows.Count == 0 || Math.Abs(rows[^1][0].StartBaseLine.Y - letter.StartBaseLine.Y) > 3)
                rows.Add([]);
            rows[^1].Add(letter);
        }
        return string.Join('\n', rows.Select(row =>
        {
            var text = new StringBuilder();
            Letter? previous = null;
            foreach (var letter in row.OrderBy(l => l.StartBaseLine.X))
            {
                if (previous is not null && letter.StartBaseLine.X - previous.EndBaseLine.X > 8)
                    text.Append(' ');
                text.Append(letter.Value);
                previous = letter;
            }
            return text.ToString().Trim();
        }).Where(line => line.Length > 0)).Trim();
    }
}
