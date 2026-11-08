using ScheduleLib.Import.Pdf;

public sealed class PdfGeometryTests
{
    [Theory]
    [InlineData(13, 1)]
    [InlineData(13.25, 2)]
    public void JoiningBordersIncludesTheToleranceBoundary(double nextStart, int expectedCount)
    {
        PdfBorder first = new(Vertical: true, Position: 0, Start: 0, End: 10);
        PdfBorder next = new(Vertical: true, Position: 0, Start: nextStart, End: 20);

        var joined = PdfTableReader.JoinBorders([first, next]);

        Assert.Equal(expected: expectedCount, actual: joined.Count);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(3.25, 0)]
    [InlineData(double.NaN, 0)]
    public void CellEdgesMustReachEveryCornerWithinTolerance(double inset, int expectedCount)
    {
        double end = 10 - inset;
        PdfBorder left = new(Vertical: true, Position: 0, Start: inset, End: end);
        PdfBorder right = new(Vertical: true, Position: 10, Start: inset, End: end);
        PdfBorder top = new(Vertical: false, Position: 0, Start: inset, End: end);
        PdfBorder bottom = new(Vertical: false, Position: 10, Start: inset, End: end);

        var cells = PdfTableReader.FindCells([left, right, top, bottom]);

        Assert.Equal(expected: expectedCount, actual: cells.Count);
    }

    [Theory]
    [InlineData(2, 1)]
    [InlineData(2.25, 3)]
    [InlineData(197.75, 3)]
    [InlineData(198, 1)]
    [InlineData(double.NaN, 1)]
    public void RecoveryRequiresABorderStrictlyInsideTheBox(double position, int expectedCount)
    {
        PdfBox box = new(Left: 0, Top: 0, Right: 200, Bottom: 100);
        PdfBorder border = new(Vertical: true, Position: position, Start: 50, End: 100);

        var cells = PdfTableReader.RecoverPartialBorders([box], [border]);

        Assert.Equal(expected: expectedCount, actual: cells.Count);
    }

    [Theory]
    [InlineData(95, 1)]
    [InlineData(94.75, 3)]
    public void RecoveryRequiresMoreThanFivePointsOfOverlap(double start, int expectedCount)
    {
        PdfBox box = new(Left: 0, Top: 0, Right: 200, Bottom: 100);
        PdfBorder border = new(Vertical: true, Position: 100, Start: start, End: 100);

        var cells = PdfTableReader.RecoverPartialBorders([box], [border]);

        Assert.Equal(expected: expectedCount, actual: cells.Count);
    }
}
