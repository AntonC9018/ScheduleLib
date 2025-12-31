using System.Collections;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleLib.Helper.Excel;

public static class IndexedRowHelper
{
    public static IndexedRowEnumerable IndexedRows(this SheetData s)
    {
        return new(s);
    }
}

public struct IndexedRow
{
    public required Row Row;
    public required uint Index;
    public required bool IsJump;

    // implicit cast to Indexed<Row>
    public static implicit operator Indexed<Row>(IndexedRow indexedRow)
    {
        return new Indexed<Row>
        {
            Item = indexedRow.Row,
            Index = (int) indexedRow.Index,
        };
    }
}

public readonly struct IndexedRowEnumerable : IEnumerable<IndexedRow>
{
    private readonly SheetData _sheet;

    public IndexedRowEnumerable(SheetData sheet)
    {
        _sheet = sheet;
    }

    public Enumerator GetEnumerator() => new(_sheet);

    IEnumerator<IndexedRow> IEnumerable<IndexedRow>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<IndexedRow>
    {
        private SheetData _sheet;
        private int _index;
        private uint _rowIndex;

        public Enumerator(SheetData sheet)
        {
            _sheet = sheet;
            _index = -1;
            _rowIndex = 0;
        }

        public IndexedRow Current { get; private set; }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var e = _sheet.ChildElements;
            while (true)
            {
                _index++;

                if (_index >= e.Count)
                {
                    return false;
                }
                var item = e[_index];
                if (item is not Row row)
                {
                    continue;
                }

                uint nextRowIndex = _rowIndex + 1;
                bool isJump = false;
                if (row.RowIndex is { } index)
                {
                    uint val = index.Value - 1;
                    if (nextRowIndex != val)
                    {
                        nextRowIndex = val;
                        isJump = true;
                    }
                }
                _rowIndex = nextRowIndex;
                Current = new()
                {
                    Index = _rowIndex,
                    IsJump = isJump,
                    Row = row,
                };
                return true;
            }
        }

        public void Dispose()
        {
        }

        public void Reset()
        {
            throw new NotImplementedException();
        }
    }
}
