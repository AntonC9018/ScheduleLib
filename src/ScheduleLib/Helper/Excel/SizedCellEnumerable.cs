using System.Collections;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ScheduleLib.Helper.Excel;

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
        private int _sizeAccum;
        private int _colIndex;

        public Enumerator(MergeCellMap map, Indexed<Row> row)
        {
            _map = map;
            _row = row;
            _index = -1;
            _colIndex = 0;
            _sizeAccum = 0;
        }

        public CellInfo Current { get; private set; }

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            var e = _row.Item.ChildElements;
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

                var size = _map.GetCellWidth(new(_colIndex, cell), _row);
                Current = new()
                {
                    Cell = cell,
                    Position = _sizeAccum,
                    Size = size,
                };
                _sizeAccum += size;
                _colIndex += 1;
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
