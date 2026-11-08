using System.Text;
using OpenTK.Mathematics;
using ScheduleLib.Mathematics;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;

namespace ScheduleLib.Import.Pdf;

public static class PdfTableReader
{
    /// <summary>The point where a vertical and a horizontal border cross.</summary>
    private readonly record struct BorderIntersection(double X, double Y);

    private static readonly PdfCoordinateTolerance BorderTolerance = new(3);
    private static readonly PdfCoordinateTolerance AxisAlignmentTolerance = new(1);
    private static readonly PdfCoordinateTolerance RecoveryTolerance = new(2);
    private static readonly PdfCoordinateTolerance TextRowTolerance = new(3);
    private const double RecoveryInset = 2;
    private const double MinimumInternalOverlap = 5;

    public static IReadOnlyList<PdfScheduleTable> Read(Stream input)
    {
        using var document = PdfDocument.Open(input);
        return document.GetPages().Select(ReadPage).ToArray();
    }

    private static PdfScheduleTable ReadPage(Page page)
    {
        var raw = ReadBorders(page);
        var borders = JoinBorders(raw.Where(b => b.End - b.Start >= 3));
        var cells = FindCells(borders);
        var boxes = RecoverPartialBorders(cells, borders);
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
            boxes.Select(ReadCell).ToArray());
    }

    private static IReadOnlyList<PdfBorder> ReadBorders(Page page)
    {
        var borders = new List<PdfBorder>();
        foreach (var path in page.Paths)
        {
            foreach (var subpath in path)
            {
                foreach (var line in subpath.Commands.OfType<PdfSubpath.Line>())
                {
                    if (IsVertical(line))
                    {
                        borders.Add(CreateVerticalBorder(line, page.Height));
                    }
                    else if (IsHorizontal(line))
                    {
                        borders.Add(CreateHorizontalBorder(line, page.Height));
                    }
                }
            }
        }
        return borders;
    }

    private static bool IsVertical(PdfSubpath.Line line)
    {
        return AxisAlignmentTolerance.AreClose(first: line.From.X, second: line.To.X);
    }

    private static bool IsHorizontal(PdfSubpath.Line line)
    {
        return AxisAlignmentTolerance.AreClose(first: line.From.Y, second: line.To.Y);
    }

    private static PdfBorder CreateVerticalBorder(PdfSubpath.Line line, double pageHeight)
    {
        double highestY = Math.Max(line.From.Y, line.To.Y);
        double lowestY = Math.Min(line.From.Y, line.To.Y);
        return new PdfBorder(
            Vertical: true,
            Position: line.From.X,
            Start: pageHeight - highestY,
            End: pageHeight - lowestY);
    }

    private static PdfBorder CreateHorizontalBorder(PdfSubpath.Line line, double pageHeight)
    {
        double left = Math.Min(line.From.X, line.To.X);
        double right = Math.Max(line.From.X, line.To.X);
        return new PdfBorder(
            Vertical: false,
            Position: pageHeight - line.From.Y,
            Start: left,
            End: right);
    }

    public static IReadOnlyList<PdfBorder> JoinBorders(IEnumerable<PdfBorder> borders)
    {
        var result = new List<PdfBorder>();
        foreach (var orientation in borders.GroupBy(b => b.Vertical))
        {
            var clusters = new List<List<PdfBorder>>();
            foreach (var border in orientation.OrderBy(b => b.Position))
            {
                if (clusters.Count == 0
                    || BorderTolerance.ExceedsGap(
                        position: border.Position,
                        previousPosition: clusters[^1][^1].Position))
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
                    if (current is { } previous
                        && BorderTolerance.IsAtMost(value: border.Start, upperBound: previous.End))
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
        var vertical = borders.Where(b => b.Vertical).ToArray();
        var horizontal = borders.Where(b => !b.Vertical).ToArray();
        var points = vertical.SelectMany(v => FindIntersections(v, horizontal))
            .Distinct()
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

    private static IEnumerable<BorderIntersection> FindIntersections(
        PdfBorder vertical,
        IEnumerable<PdfBorder> horizontal)
    {
        return horizontal.Where(h => BordersCross(vertical: vertical, horizontal: h))
            .Select(h => new BorderIntersection(X: vertical.Position, Y: h.Position));
    }

    private static bool BordersCross(PdfBorder vertical, PdfBorder horizontal)
    {
        if (!CoversRange(vertical, start: horizontal.Position, end: horizontal.Position))
        {
            return false;
        }
        if (CoversRange(horizontal, start: vertical.Position, end: vertical.Position))
        {
            return true;
        }
        return false;
    }

    private static bool CoversRange(PdfBorder border, double start, double end)
    {
        if (!BorderTolerance.IsAtMost(value: border.Start, upperBound: start))
        {
            return false;
        }
        if (BorderTolerance.IsAtLeast(value: border.End, lowerBound: end))
        {
            return true;
        }
        return false;
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
            if (CoversRange(b, start: start, end: end))
            {
                return true;
            }
            return false;
        });
    }

    private static bool IsInsideRecoveryWidth(double position, PdfBox box)
    {
        double left = box.Left + RecoveryInset;
        double right = box.Right - RecoveryInset;
        return left < position && position < right;
    }

    private static bool IsInternalBorder(PdfBorder border, PdfBox box)
    {
        if (!border.Vertical)
        {
            return false;
        }
        if (!IsInsideRecoveryWidth(border.Position, box))
        {
            return false;
        }
        double overlapStart = Math.Max(border.Start, box.Top);
        double overlapEnd = Math.Min(border.End, box.Bottom);
        double overlapLength = overlapEnd - overlapStart;
        if (overlapLength > MinimumInternalOverlap)
        {
            return true;
        }
        return false;
    }

    private static bool IsInsideRecoveryHeight(double position, PdfBox box)
    {
        double top = box.Top + RecoveryInset;
        double bottom = box.Bottom - RecoveryInset;
        return top < position && position < bottom;
    }

    private static bool CoversRecoveryRow(PdfBorder border, double middle)
    {
        if (!RecoveryTolerance.IsAtLeast(value: middle, lowerBound: border.Start))
        {
            return false;
        }
        if (RecoveryTolerance.IsAtMost(value: middle, upperBound: border.End))
        {
            return true;
        }
        return false;
    }

    public static IReadOnlyList<PdfBox> RecoverPartialBorders(
        IEnumerable<PdfBox> cells,
        IReadOnlyList<PdfBorder> borders)
    {
        var result = new List<PdfBox>();
        foreach (var box in cells)
        {
            var internalBorders = borders.Where(b => IsInternalBorder(b, box)).ToArray();
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
                .Where(y => IsInsideRecoveryHeight(y, box))
                .Append(box.Top)
                .Append(box.Bottom)
                .Distinct()
                .Order()
                .ToArray();
            for (int row = 1; row < ys.Length; row++)
            {
                var middle = ys[row - 1].Midpoint(ys[row]);
                var xs = internalBorders.Where(b => CoversRecoveryRow(b, middle))
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
        return result.Distinct().ToArray();
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
        var start = new Vector2d(x: letter.StartBaseLine.X, y: letter.StartBaseLine.Y);
        var end = new Vector2d(x: letter.EndBaseLine.X, y: letter.EndBaseLine.Y);
        var displacement = end - start;
        double length = displacement.Length;
        var direction = length > 0
            ? displacement / length
            : Vector2d.UnitX;
        // Lift the point off the baseline towards the glyph's x-height, perpendicular to the text.
        double lift = letter.PointSize / 3;
        var midpoint = start.Midpoint(end);
        var normalOffset = direction.PerpendicularLeft * lift;
        var anchor = midpoint + normalOffset;
        return new PdfPoint(
            x: anchor.X,
            y: anchor.Y);
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
            if (rows.Count == 0
                || TextRowTolerance.AreSeparated(
                    first: rows[^1][0].StartBaseLine.Y,
                    second: letter.StartBaseLine.Y))
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
                        return text.ToString().Trim();
                    })
                    .Where(line => line.Length > 0))
            .Trim();
    }
}
