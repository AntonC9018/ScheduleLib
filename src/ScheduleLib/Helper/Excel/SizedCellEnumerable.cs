using System.Collections;
using System.Diagnostics;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleLib.Helper.Excel;

public static class SizedCellHelper
{
    public static SizedCellEnumerable SizedCells(this IndexedRow row, MergeCellMap mergeCells)
    {
        return SizedCells((Indexed<Row>) row, mergeCells);
    }
    public static SizedCellEnumerable SizedCells(this Indexed<Row> row, MergeCellMap mergeCells)
    {
        return new(mergeCells, row);
    }
}

public struct CellInfo
{
    public required Cell Cell;
    public required int Size;
    public required int Position;
}

public readonly struct SizedCellEnumerable : IEnumerable<CellInfo>
{
    private readonly Indexed<Row> _row;
    private readonly MergeCellMap _map;

    public SizedCellEnumerable(MergeCellMap map, Indexed<Row> row)
    {
        _map = map;
        _row = row;
    }

    public Enumerator GetEnumerator() => new(_map, _row);

    IEnumerator<CellInfo> IEnumerable<CellInfo>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<CellInfo>
    {
        private readonly Indexed<Row> _row;
        private readonly MergeCellMap _map;
        private int _index;
        private int _colIndex;

        public Enumerator(MergeCellMap map, Indexed<Row> row)
        {
            _map = map;
            _row = row;
            _index = -1 + 1; // adding -1 as the first step, adjusting for that here.
            _colIndex = 0;
            Current = new()
            {
                Cell = null!,
                Position = 0,
                Size = 0,
            };
        }

        public CellInfo Current { get; private set; }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var e = _row.Item.ChildElements;
            _index += Current.Size - 1;
            _colIndex += Current.Size;

            while (true)
            {
                _index++;

                if (_index >= e.Count)
                {
                    return false;
                }
                var it = e[_index];
                if (it is not Cell cell)
                {
                    continue;
                }

                var pos = ExcelRangeHelper.GetPosition(new(_colIndex, cell), _row);
                if (pos.Col != _colIndex)
                {
                    _colIndex = (int) pos.Col;
                }

                var size = _map.GetCellWidth(pos);
                Debug.Assert(size >= 1);
                Current = new()
                {
                    Cell = cell,
                    Position = _colIndex,
                    Size = size,
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
