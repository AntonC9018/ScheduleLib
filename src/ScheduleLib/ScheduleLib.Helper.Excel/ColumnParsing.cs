using ClosedXML.Excel;
using ScheduleLib.Excel.Helper;

namespace ScheduleLib.Helper.Excel;


public static class MappingsHelper
{
    public static Mappings<TColumn> FindMappings<TColumn>(
        this IXLRow headerRow,
        ColumnSearchRules<TColumn> searchRules)
        where TColumn : struct, Enum
    {
        var columnsMap = OneForEach.Enum<TColumn>().RentArray<int>();
        try
        {
            const int notFound = -1;
            columnsMap.Span.Fill(notFound);
            foreach (var cell in headerRow.Cells())
            {
                if (!cell.TryGetValue(out string text))
                {
                    // reached end of line
                    break;
                }
                var columnNumber = cell.ActualRange().FirstColumn().ColumnNumber();
                TColumn? FindColumn()
                {
                    foreach (var x in searchRules.Array)
                    {
                        if (x.Value.ExactMatch == text)
                        {
                            return x.Key;
                        }
                    }
                    return null;
                }
                if (FindColumn() is { } column)
                {
                    columnsMap[column] = columnNumber;
                }
            }

            {
                var foundColumns = new EnumBitArray<TColumn>();
                foreach (var c in columnsMap)
                {
                    if (c.Value != notFound)
                    {
                        foundColumns.Set(c.Key);
                    }
                }
                var notFoundColumns = foundColumns.Flipped;
                if (!notFoundColumns.IsEmpty)
                {
                    throw headerRow.Exception($"Columns '{notFoundColumns}' not found in the header");
                }
            }
        }
        catch
        {
            columnsMap.Dispose();
            throw;
        }
        return new(columnsMap);
    }

    public static EnumBitArray<TColumn> ProcessRow<TColumn, TProcessor>(
        this IXLCells cells,
        ref TProcessor processor,
        Mappings<TColumn> columnNumbers)
        where TColumn : struct, Enum
        where TProcessor : ICellProcessor<TColumn>, allows ref struct
    {
        var ret = new EnumBitArray<TColumn>();
        foreach (var cell in cells)
        {
            var number = cell.ActualRange().FirstColumn().ColumnNumber();
            if (FindColumn(columnNumbers, number) is not { } c)
            {
                continue;
            }
            if (processor.Process(c, cell))
            {
                ret.Set(c);
            }
            continue;

            static TColumn? FindColumn(
                Mappings<TColumn> columnMappings,
                int columnNumber)
            {
                foreach (var c in columnMappings.Array)
                {
                    if (c.Value == columnNumber)
                    {
                        return c.Key;
                    }
                }
                return null;
            }
        }
        return ret;
    }
}

public readonly record struct SearchRules(string ExactMatch)
{
}

public static class ColumnSearchRules
{
    public static ColumnSearchRules<TColumn> Create<TColumn>(
        Action<ColumnSearchRulesBuilder<TColumn>> configure)
        where TColumn : struct, Enum
    {
        var b = new ColumnSearchRulesBuilder<TColumn>();
        configure(b);
        var ret = b.Build();
        return ret;
    }
}

public readonly struct ColumnSearchRules<TColumn>
    where TColumn : struct, Enum
{
    public readonly OneForEachEnumMemberArray<TColumn, SearchRules> Array;
    public ColumnSearchRules(OneForEachEnumMemberArray<TColumn, SearchRules> array)
    {
        Array = array;
        if (array.Storage.Any(x => x == default))
        {
            throw new InvalidOperationException("Keys for some columns are missing.");
        }
    }
}

public readonly struct ColumnSearchRulesBuilder<TColumn>
    where TColumn : struct, Enum
{
    public readonly OneForEachEnumMemberArray<TColumn, SearchRules> Array;
    public ColumnSearchRulesBuilder() => Array = new();

    public readonly struct SingleBuilder(
        ColumnSearchRulesBuilder<TColumn> _arr,
        TColumn _column)
    {
        public readonly TColumn Column => _column;

        public void ExactMatch(string value)
        {
            _arr.Array[_column] = new(ExactMatch: value);
        }
    }

    public SingleBuilder Column(TColumn column) => new(this, column);
    public ColumnSearchRules<TColumn> Build() => new(Array);
}

public readonly struct Mappings<TColumn> : IDisposable
    where TColumn : struct, Enum
{
    public readonly RentedOneForEachEnumMemberArray<TColumn, int> Array;
    public Mappings(RentedOneForEachEnumMemberArray<TColumn, int> array) => Array = array;
    public void Dispose() => Array.Dispose();
}
