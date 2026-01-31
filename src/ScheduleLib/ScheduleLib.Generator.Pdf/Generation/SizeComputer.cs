namespace ScheduleLib.Generation;

public readonly struct SizeComputer
{
    private readonly int _maxRowsInOneCell;
    private readonly int _lessonCount;

    public SizeComputer(int maxRowsInOneCell, int lessonCount)
    {
        _maxRowsInOneCell = maxRowsInOneCell;
        _lessonCount = lessonCount;
    }

    public int ComputeRowOffsetOf(int lessonIndex)
    {
        var total = _maxRowsInOneCell;
        var x = (float) lessonIndex / (float) _lessonCount;
        return (int)(x * total);
    }

    public uint ComputeRowSpan(int lessonIndex)
    {
        var a = ComputeRowOffsetOf(lessonIndex);
        var b = ComputeRowOffsetOf(lessonIndex + 1);
        return (uint)(b - a);
    }

}
