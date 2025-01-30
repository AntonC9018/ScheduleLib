namespace ReaderApp;



public enum OddEdge
{
    Even_Top = 0,
    Even_Bottom = 1,
    Even_Neither = 2,
    Odd_Top = 3,
    Odd_Bottom = 4,
    Odd_Neither = 5,
    Count,
}

public enum Oddness
{
    Even,
    Odd,
}

public enum Edge
{
    Top,
    Bottom,
    Neither,
    Count,
}

public static class UsefulStylesheetEnumsHelper
{
    public static bool IsOdd(this OddEdge opt) => opt >= OddEdge.Odd_Top;
    public static Oddness GetOddness(this OddEdge e) => e.IsOdd() ? Oddness.Odd : Oddness.Even;
    public static Edge GetEdge(this OddEdge opt) => (Edge) ((int) opt % 3);

    public static OddEdge OddEdgeFromIndex(
        int oddnessIndex,
        int edgenessIndex,
        int height)
    {
        bool odd = oddnessIndex % 2 == 1;
        bool top = edgenessIndex == 0;
        bool bottom = edgenessIndex == height - 1;
        bool neither = !top && !bottom;

        int val = 0;
        if (neither)
        {
            val += (int) Edge.Neither;
        }
        else if (bottom)
        {
            val += (int) Edge.Bottom;
        }

        if (odd)
        {
            val += (int) Edge.Count;
        }

        return (OddEdge) val;
    }
}
