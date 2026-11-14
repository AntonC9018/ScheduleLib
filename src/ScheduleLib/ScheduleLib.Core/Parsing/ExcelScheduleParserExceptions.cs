namespace ScheduleLib.Parsing;

public enum ExcelScheduleSection
{
    MetadataSeparatorRow,
    GroupsHeaderRow,
}

public sealed class ExcelScheduleMissingSectionException : NotSupportedException
{
    private ExcelScheduleMissingSectionException(ExcelScheduleSection section, string message)
        : base(message)
    {
        Section = section;
    }

    public ExcelScheduleSection Section { get; }

    public static ExcelScheduleMissingSectionException ForMissingMetadataSeparatorRow()
    {
        return new(ExcelScheduleSection.MetadataSeparatorRow, "Expected a separator row after the groups header.");
    }

    public static ExcelScheduleMissingSectionException ForMissingGroupsHeaderRow()
    {
        return new(ExcelScheduleSection.GroupsHeaderRow, "No groups row found.");
    }
}

public sealed class IncompleteExcelGroupHeaderException : NotSupportedException
{
    public IncompleteExcelGroupHeaderException(int expectedGroupCount, int actualGroupCount)
        : base($"Not all columns covered by year ({expectedGroupCount}) are covered by groups ({actualGroupCount}).")
    {
        ExpectedGroupCount = expectedGroupCount;
        ActualGroupCount = actualGroupCount;
    }

    public int ExpectedGroupCount { get; }
    public int ActualGroupCount { get; }
}
