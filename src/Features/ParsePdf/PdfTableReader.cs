using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace ScheduleLib.Import.Pdf;

public static class PdfTableReader
{
    /// <summary>The point where a vertical and a horizontal border cross.</summary>
    private readonly record struct BorderIntersection(double X, double Y);

    private const double Tolerance = 3;

    public static IReadOnlyList<PdfScheduleTable> Read(Stream input)
    {
        using var document = PdfDocument.Open(input);
        return document.GetPages()
            .Select(ReadPage)
            .ToArray();
    }

    private static PdfScheduleTable ReadPage(Page page)
    {
        var raw = new List<PdfBorder>();
        foreach (var path in page.Paths)
        {
            foreach (var subpath in path)
            {
                foreach (var line in subpath.Commands.OfType<PdfSubpath.Line>())
                {
                    if (Math.Abs(line.From.X - line.To.X) < 1)
                    {
                        bool vertical = true;
                        double position = line.From.X;
                        double doubleValue = page.Height;
                        double max = Math.Max(line.From.Y, line.To.Y);
                        double start = doubleValue - max;
                        double doubleValue2 = page.Height;
                        double min = Math.Min(line.From.Y, line.To.Y);
                        double end = doubleValue2 - min;
                        PdfBorder item = new(
                            vertical,
                            Position: position,
                            Start: start,
                            End: end);
                        raw.Add(item);
                    }
                    else if (Math.Abs(line.From.Y - line.To.Y) < 1)
                    {
                        bool vertical2 = false;
                        double position2 = page.Height - line.From.Y;
                        double start2 = Math.Min(line.From.X, line.To.X);
                        double end2 = Math.Max(line.From.X, line.To.X);
                        PdfBorder item2 = new(
                            vertical2,
                            Position: position2,
                            Start: start2,
                            End: end2);
                        raw.Add(item2);
                    }
                }
            }
        }

        var borders = JoinBorders(raw.Where(b => b.End - b.Start >= 3));
        var cells =
            FindCells(borders);
        IEnumerable<PdfBox> cells2 = cells;
        var borders2 = borders;
        var boxes = RecoverPartialBorders(cells2, borders2);
        if (boxes.Count == 0)
        {
            throw new PdfScheduleTableNotFoundException(page.Number);
        }

        PdfTableCell ReadCell(PdfBox b)
        {
            var text = ReadText(page, b);
            return new PdfTableCell(b, text);
        }

        return new(
            page.Number,
            borders,
            boxes.Select(ReadCell)
                .ToArray());
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
                {
                    clusters.Add([]);
                }

                clusters[^1].Add(border);
            }
            foreach (var cluster in clusters)
            {
                var position = cluster.Average(b => b.Position);
                PdfBorder? current = null;
                foreach (var border in cluster.OrderBy(b => b.Start))
                {
                    if (current is { } previous && border.Start <= previous.End + Tolerance)
                    {
                        current = previous with
                        {
                            End = Math.Max(previous.End, border.End),
                        };
                    }
                    else
                    {
                        if (current is { } completed)
                        {
                            result.Add(completed);
                        }

                        current = border with
                        {
                            Position = position,
                        };
                    }
                }
                if (current is { } last)
                {
                    result.Add(last);
                }
            }
        }
        return result;
    }

    public static IReadOnlyList<PdfBox> FindCells(IReadOnlyList<PdfBorder> borders)
    {
        var vertical = borders.Where(b => b.Vertical)
            .ToArray();
        var horizontal = borders.Where(b => !b.Vertical)
            .ToArray();
        var points = (from v in vertical
                      from h in horizontal
                      where v.Start <= h.Position + Tolerance
                          && v.End >= h.Position - Tolerance
                          && h.Start <= v.Position + Tolerance
                          && h.End >= v.Position - Tolerance
                      select new BorderIntersection(X: v.Position, Y: h.Position)).Distinct()
            .OrderBy(p => p.X)
            .ThenBy(p => p.Y)
            .ToArray();
        var result = new List<PdfBox>();
        foreach (var point in points)
        {
            var below = points.Where(p => p.X == point.X && p.Y > point.Y);
            var right = points.Where(p => p.Y == point.Y && p.X > point.X);
            bool found = false;
            foreach (var bottom in below)
            {
                if (!Connects(
                        vertical,
                        position: point.X,
                        start: point.Y,
                        end: bottom.Y))
                {
                    continue;
                }

                foreach (var end in right)
                {
                    bool HasMissingCellEdges()
                    {
                        if (!Connects(
                                horizontal,
                                position: point.Y,
                                start: point.X,
                                end: end.X))
                        {
                            return true;
                        }

                        if (!Connects(
                                vertical,
                                position: end.X,
                                start: point.Y,
                                end: bottom.Y))
                        {
                            return true;
                        }

                        if (!Connects(
                                horizontal,
                                position: bottom.Y,
                                start: point.X,
                                end: end.X))
                        {
                            return true;
                        }

                        return false;
                    }

                    if (HasMissingCellEdges())
                    {
                        continue;
                    }

                    PdfBox item = new(
                        Left: point.X,
                        Top: point.Y,
                        Right: end.X,
                        Bottom: bottom.Y);
                    result.Add(item);
                    found = true;
                    break;
                }
                if (found)
                {
                    break;
                }
            }
        }
        return result;
    }

    private static bool Connects(
        IEnumerable<PdfBorder> borders,
        double position,
        double start,
        double end)
    {
        return borders.Any(b =>
        {
            if (b.Position != position)
            {
                return false;
            }
            if (!(b.Start <= start + Tolerance))
            {
                return false;
            }

            if (b.End >= end - Tolerance)
            {
                return true;
            }

            return false;
        });
    }

    public static IReadOnlyList<PdfBox> RecoverPartialBorders(
        IEnumerable<PdfBox> cells,
        IReadOnlyList<PdfBorder> borders)
    {
        var result = new List<PdfBox>();
        foreach (var box in cells)
        {
            var internalBorders = borders.Where(b =>
                {
                    if (!b.Vertical)
                    {
                        return false;
                    }
                    if (!(box.Left + 2 < b.Position))
                    {
                        return false;
                    }
                    if (!(b.Position < box.Right - 2))
                    {
                        return false;
                    }

                    if (Math.Min(b.End, box.Bottom) - Math.Max(b.Start, box.Top) > 5)
                    {
                        return true;
                    }

                    return false;
                })
                .ToArray();
            if (internalBorders.Length == 0)
            {
                result.Add(box);
                continue;
            }
            var ys = internalBorders.SelectMany(b => new[]
                {
                    b.Start,
                    b.End,
                })
                .Where(y => box.Top + 2 < y && y < box.Bottom - 2)
                .Append(box.Top)
                .Append(box.Bottom)
                .Distinct()
                .Order()
                .ToArray();
            for (int row = 1; row < ys.Length; row++)
            {
                var adjacentBorderSum = ys[row - 1] + ys[row];
                var middle = adjacentBorderSum / 2;
                var xs = internalBorders.Where(b => b.Start - 2 <= middle && middle <= b.End + 2)
                    .Select(b => b.Position)
                    .Append(box.Left)
                    .Append(box.Right)
                    .Distinct()
                    .Order()
                    .ToArray();
                for (int column = 1; column < xs.Length; column++)
                {
                    double left = xs[column - 1];
                    double top = ys[row - 1];
                    double right = xs[column];
                    double bottom = ys[row];
                    PdfBox item = new(
                        Left: left,
                        Top: top,
                        Right: right,
                        Bottom: bottom);
                    result.Add(item);
                }
            }
        }
        return result.Distinct()
            .ToArray();
    }

    /// <summary>
    /// A point inside the glyph, derived from the advance-width baseline rather than the glyph
    /// bounding box. PdfPig takes glyph boxes from the font program; for the non-embedded
    /// Times New Roman in the FMI PDFs it falls back to the descriptor's FontBBox when the font is
    /// not installed (e.g. on Linux), so every box is wider than the glyph and shifted right,
    /// pushing trailing characters into the neighbouring cell. Baselines come from the PDF's own
    /// /Widths and do not depend on installed fonts.
    /// </summary>
    private static PdfPoint LetterAnchor(Letter letter)
    {
        var start = letter.StartBaseLine;
        var end = letter.EndBaseLine;
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        var squaredLength = dx * dx + dy * dy;
        double length = Math.Sqrt(squaredLength);
        var direction = length > 0
            ? new PdfPoint(x: dx / length, y: dy / length)
            : new PdfPoint(x: 1, y: 0);
        // Lift the point off the baseline towards the glyph's x-height, perpendicular to the text.
        double lift = letter.PointSize / 3;
        var midX = (start.X + end.X) / 2;
        var midY = (start.Y + end.Y) / 2;
        var normalOffsetX = direction.Y * lift;
        var normalOffsetY = direction.X * lift;
        return new PdfPoint(
            x: midX - normalOffsetX,
            y: midY + normalOffsetY);
    }

    private static string ReadText(Page page, PdfBox bounds)
    {
        var letters = page.Letters.Where(l =>
            {
                var anchor = LetterAnchor(l);
                return bounds.Contains(x: anchor.X, y: page.Height - anchor.Y);
            })
            .ToArray();
        if (letters.Length == 0)
        {
            return "";
        }
        // Rotated day labels read from the bottom of the page towards the top.
        if (letters.Count(l =>
                Math.Abs(l.EndBaseLine.Y - l.StartBaseLine.Y) > Math.Abs(l.EndBaseLine.X - l.StartBaseLine.X))
            > letters.Length / 2)
        {
            return string.Concat(letters.OrderBy(l => l.StartBaseLine.Y)
                    .Select(l => l.Value))
                .Trim();
        }

        var rows = new List<List<Letter>>();
        foreach (var letter in letters.OrderByDescending(l => l.StartBaseLine.Y))
        {
            if (rows.Count == 0 || Math.Abs(rows[^1][0].StartBaseLine.Y - letter.StartBaseLine.Y) > 3)
            {
                rows.Add([]);
            }

            rows[^1].Add(letter);
        }
        return string.Join(
                '\n',
                rows.Select(row =>
                    {
                        var text = new StringBuilder();
                        Letter? previous = null;
                        foreach (var letter in row.OrderBy(l => l.StartBaseLine.X))
                        {
                            if (previous is not null && letter.StartBaseLine.X - previous.EndBaseLine.X > 8)
                            {
                                text.Append(' ');
                            }

                            text.Append(letter.Value);
                            previous = letter;
                        }
                        return text.ToString()
                            .Trim();
                    })
                    .Where(line => line.Length > 0))
            .Trim();
    }
}
