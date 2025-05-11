using System.Diagnostics;

namespace ReaderApp.Helper;

public readonly struct AllEnumEnumerable<T>
    where T : struct, Enum
{
    public static readonly T Start;
    public static readonly T End;
    public static int Count => EnumAsInt(End) - EnumAsInt(Start) + 1;

    static AllEnumEnumerable()
    {
        Debug.Assert(typeof(T).GetEnumUnderlyingType() == typeof(int));

        var values = Enum.GetValues(typeof(T)).OfType<T>().OrderBy(EnumAsInt).ToArray();
        Debug.Assert(values.Length > 0, "Empty enum?");

        bool adjustForCount = Enum.GetName(typeof(T), values[^1]) == "Count";
        int length = values.Length;
        if (adjustForCount)
        {
            length -= 1;
        }

        Debug.Assert(length >= 1, "Empty enum can't be enumerated");

        T start = values[0];
        T end = adjustForCount ? values[^2] : values[^1];

        Start = start;
        End = end;

        Debug.Assert(Count == length, "Values are not consecutive");
    }

    private static int EnumAsInt(T e)
    {
        return (int) (object) e;
    }

    private static T IntAsEnum(int e)
    {
        return (T) (object) e;
    }

    public struct Enumerator
    {
        private int _index;

        public Enumerator()
        {
            _index = EnumAsInt(Start) - 1;
        }

        public bool MoveNext()
        {
            _index++;
            return _index <= EnumAsInt(End);
        }

        public void Reset()
        {
            _index = EnumAsInt(Start) - 1;
        }

        public readonly T Current => IntAsEnum(_index);
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator();
    }
}
