using MainCli.BuilderNew;
using Anton.LayeredConfig.Retrieval;

namespace MainCli;

public sealed class DeadlinesConfigMapper : IConfigMapper<DeadlinesExcelConfig, DeadlinesExcelBuiltConfig>
{
    public DeadlinesExcelBuiltConfig Map(DeadlinesExcelConfig input)
    {
        return new()
        {
            BadColor = input.BadColor!.Value,
            ColumnWidth = input.ColumnWidth,
            GoodColor = input.GoodColor!.Value,
            LessonDelayLimit = input.LessonDelayLimit,
            MaxTaskRows = input.MaxTaskRows,
        };
    }
}

// TODO: Generate with source gen
public sealed class DeadlinesExcelConfigMerger : IMerger<DeadlinesExcelConfig>
{
    public DeadlinesExcelConfig Merge(DeadlinesExcelConfig from, DeadlinesExcelConfig? into)
    {
        if (into is null)
        {
            into = new();
        }
        if (from.BadColor is { } badColor)
        {
            into.BadColor = badColor;
        }
        if (from.GoodColor is { } goodColor)
        {
            into.GoodColor = goodColor;
        }
        if (!float.IsNegativeInfinity(from.ColumnWidth))
        {
            into.ColumnWidth = from.ColumnWidth;
        }
        if (from.LessonDelayLimit != -1)
        {
            into.LessonDelayLimit = from.LessonDelayLimit;
        }
        if (from.MaxTaskRows != -1)
        {
            into.MaxTaskRows = from.MaxTaskRows;
        }
        return into;
    }
}

