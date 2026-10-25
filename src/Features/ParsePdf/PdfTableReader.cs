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
                        global::System.Collections.Generic.List<global::ScheduleLib.Import.Pdf.PdfBorder> list = raw;
                        bool vertical = true;
                        double position = line.From.X;
                        double doubleValue = page.Height;
                        double max = Math.Max(line.From.Y, line.To.Y);
                        double start = doubleValue - max;
                        double doubleValue2 = page.Height;
                        double min = Math.Min(line.From.Y, line.To.Y);
                        double end = doubleValue2 - min;
                        global::ScheduleLib.Import.Pdf.PdfBorder item = new(vertical, Position: position, Start: start, End: end);
                        list.Add(item);
                    }
                    else if (Math.Abs(line.From.Y - line.To.Y) < 1)
                    {
                        global::System.Collections.Generic.List<global::ScheduleLib.Import.Pdf.PdfBorder> list2 = raw;
                        bool vertical2 = false;
                        double position2 = page.Height - line.From.Y;
                        double start2 = Math.Min(line.From.X, line.To.X);
                        double end2 = Math.Max(line.From.X, line.To.X);
                        global::ScheduleLib.Import.Pdf.PdfBorder item2 = new(vertical2, Position: position2, Start: start2, End: end2);
                        list2.Add(item2);
                    }
                }
            }
        }

        var borders = JoinBorders(raw.Where(b => b.End - b.Start >= 3));
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.Import.Pdf.PdfBox> cells = FindCells(borders);
        global::System.Collections.Generic.IEnumerable<global::ScheduleLib.Import.Pdf.PdfBox> cells2 = cells;
        global::System.Collections.Generic.IReadOnlyList<global::ScheduleLib.Import.Pdf.PdfBorder> borders2 = borders;
        var boxes = RecoverPartialBorders(cells2, borders2);
        if (boxes.Count == 0)
        {
            throw new FormatException($"Page {page.Number}: no bordered schedule table found.");
        }

        global::ScheduleLib.Import.Pdf.PdfTableCell ProjectValue(global::ScheduleLib.Import.Pdf.PdfBox b)
        {
            global::ScheduleLib.Import.Pdf.PdfBox operationValue = b;
            string operationValue2 = ReadText(page, b);
            string operationValue3 = operationValue2;
            global::ScheduleLib.Import.Pdf.PdfTableCell operationValue4 = new PdfTableCell(operationValue, operationValue3);
            return operationValue4;
        }

        return new(page.Number, borders, boxes.Select(ProjectValue)
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
                      where v.Start <= h.Position + Tolerance && v.End >= h.Position - Tolerance
                         && h.Start <= v.Position + Tolerance && h.End >= v.Position - Tolerance
                      select (X: v.Position, Y: h.Position)).Distinct()
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
                if (!Connects(vertical, position: point.X, start: point.Y, end: bottom.Y))
                {
                    continue;
                }

                foreach (var end in right)
                {
                    bool CheckCondition()
                    {
                        if (!Connects(horizontal, position: point.Y, start: point.X, end: end.X))
                        {
                            return true;
                        }

                        if (!Connects(vertical, position: end.X, start: point.Y, end: bottom.Y))
                        {
                            return true;
                        }

                        if (!Connects(horizontal, position: bottom.Y, start: point.X, end: end.X))
                        {
                            return true;
                        }

                        return false;
                    }

                    if (CheckCondition())
                    {
                        continue;
                    }

                    global::System.Collections.Generic.List<global::ScheduleLib.Import.Pdf.PdfBox> list = result;
                    global::ScheduleLib.Import.Pdf.PdfBox item = new(Left: point.X, Top: point.Y, Right: end.X, Bottom: bottom.Y);
                    list.Add(item);
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

    private static bool Connects(IEnumerable<PdfBorder> borders, double position, double start, double end) =>
        borders.Any(b => { if (!(b.Position == position)) { return false; } if (!(b.Start <= start + Tolerance)) { return false; } if (!(b.End >= end - Tolerance)) { return false; } return true; });

    public static IReadOnlyList<PdfBox> RecoverPartialBorders(IEnumerable<PdfBox> cells, IReadOnlyList<PdfBorder> borders)
    {
        var result = new List<PdfBox>();
        foreach (var box in cells)
        {
            var internalBorders = borders.Where(b => { if (!(b.Vertical)) { return false; } if (!(box.Left + 2 < b.Position)) { return false; } if (!(b.Position < box.Right - 2)) { return false; } if (!(Math.Min(b.End, box.Bottom) - Math.Max(b.Start, box.Top) > 5)) { return false; } return true; })
                .ToArray();
            if (internalBorders.Length == 0)
            { result.Add(box); continue; }
            var ys = internalBorders.SelectMany(b => new[] { b.Start, b.End })
                .Where(y => box.Top + 2 < y && y < box.Bottom - 2)
                .Append(box.Top)
                .Append(box.Bottom)
                .Distinct()
                .Order()
                .ToArray();
            for (int row = 1; row < ys.Length; row++)
            {
                double[] value3 = ys;
                int int323 = row - 1;
                double doubleValue = value3[int323] + ys[row];
                var middle = (doubleValue) / 2;
                var xs = internalBorders.Where(b => b.Start - 2 <= middle && middle <= b.End + 2)
                    .Select(b => b.Position)
                    .Append(box.Left)
                    .Append(box.Right)
                    .Distinct()
                    .Order()
                    .ToArray();
                for (int column = 1; column < xs.Length; column++)
                {
                    global::System.Collections.Generic.List<global::ScheduleLib.Import.Pdf.PdfBox> list = result;
                    double[] value = xs;
                    int int32 = column - 1;
                    double left = value[int32];
                    double[] value2 = ys;
                    int int322 = row - 1;
                    double top = value2[int322];
                    double right = xs[column];
                    double bottom = ys[row];
                    global::ScheduleLib.Import.Pdf.PdfBox item = new(Left: left, Top: top, Right: right, Bottom: bottom);
                    list.Add(item);
                }
            }
        }
        return result.Distinct()
            .ToArray();
    }

    private static string ReadText(Page page, PdfBox bounds)
    {
        var letters = page.Letters.Where(l =>
        {
            double CalculateX()
            {
                double doubleValue = l.BoundingBox.Left + l.BoundingBox.Right;
                double x2 = (doubleValue) / 2;
                return x2;
            }

            double CalculateY()
            {
                double doubleValue2 = page.Height;
                double doubleValue3 = l.BoundingBox.Top + l.BoundingBox.Bottom;
                double doubleValue4 = (doubleValue3) / 2;
                double y2 = doubleValue2 - doubleValue4;
                return y2;
            }

            return bounds.Contains(
                                    x: CalculateX(),
                                    y: CalculateY());
        })
            .ToArray();
        if (letters.Length == 0)
        {
            return "";
        }
        // Rotated day labels read from the bottom of the page towards the top.
        if (letters.Count(l => Math.Abs(l.EndBaseLine.Y - l.StartBaseLine.Y) > Math.Abs(l.EndBaseLine.X - l.StartBaseLine.X)) > letters.Length / 2)
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
        return string.Join('\n', rows.Select(row =>
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
