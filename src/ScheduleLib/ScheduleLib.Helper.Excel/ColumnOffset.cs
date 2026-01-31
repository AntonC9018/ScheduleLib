namespace ScheduleLib.Excel.Helper;

public readonly record struct RestoredIndex(int Value)
{
}

public readonly record struct ColumnOffset(int Value)
{
    public RestoredIndex GetUnOffsetIndex(int columnIndex) => new(columnIndex - Value);
}
