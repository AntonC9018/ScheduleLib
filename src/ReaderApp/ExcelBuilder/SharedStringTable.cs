using System.Runtime.InteropServices;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ReaderApp.ExcelBuilder;

public readonly record struct SharedStringItemId(int Value);

public struct StringTableBuilder
{
    private readonly Dictionary<string, SharedStringItemId> _strings = new();
    private readonly SharedStringTable _table;
    private int otherCount;

    public StringTableBuilder(SharedStringTable table) => _table = table;

    public static StringTableBuilder Create(WorkbookPart workbookPart)
    {
        var sharedStringPart = workbookPart.AddNewPart<SharedStringTablePart>();
        var sharedStringTable = new SharedStringTable();
        sharedStringPart.SharedStringTable = sharedStringTable;
        return new(sharedStringTable);
    }

    public SharedStringItemId NextId => new(_strings.Count + otherCount);

    private void NewString(string value)
    {
        var text = new Text(value);
        var item = new SharedStringItem(text);
        _table.AppendChild(item);
    }

    public SharedStringItemId GetOrAddString(string s)
    {
        var id = NextId;
        ref var ret = ref CollectionsMarshal.GetValueRefOrAddDefault(_strings, s, out var exists);
        if (!exists)
        {
            NewString(s);
            ret = id;
        }
        return ret;
    }

    public SharedStringItemId AddString(string s)
    {
        var id = NextId;
        _strings.Add(s, id);
        NewString(s);
        return id;
    }

    public SharedStringItemId AddItem(SharedStringItem item)
    {
        var id = NextId;
        otherCount++;
        _table.AddChild(item);
        return id;
    }
}

public static class SharedStringsHelper
{
    public static void SetSharedStringValue(this Cell cell, SharedStringItemId stringId)
    {
        cell.DataType = CellValues.SharedString;
        cell.CellValue = new(stringId.Value);
    }
}


