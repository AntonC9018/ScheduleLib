using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ReaderApp;

public readonly struct StyleIds<TEnum, TId> where TEnum : struct, Enum
{
    static StyleIds()
    {
        Debug.Assert(Marshal.SizeOf<TId>() == sizeof(uint));
    }

    private readonly uint _first;
    public StyleIds(uint first) => _first = first;

    public readonly TId Get(TEnum e)
    {
        uint i = _first + (uint) (int) (object) e;
        return Unsafe.As<uint, TId>(ref i);
    }
}

public static partial class StylesheetBuilderHelper
{
    /// <summary>
    /// Creates one Border for each enum member of <c>TEnum</c>
    /// </summary>
    public static StyleIds<TEnum, BorderId> Borders<TEnum>(
        this StylesheetBuilder builder,
        Action<Border, TEnum> configure)

        where TEnum : struct, Enum
    {
        return Create<TEnum, BorderId, Border>(builder, StylesheetResource.Border, configure);
    }

    /// <summary>
    /// Creates one CellFormat for each enum member of <c>TEnum</c>
    /// </summary>
    public static StyleIds<TEnum, CellFormatId> CellFormats<TEnum>(
        this StylesheetBuilder builder,
        Action<CellFormat, TEnum> configure)

        where TEnum : struct, Enum
    {
        return Create<TEnum, CellFormatId, CellFormat>(builder, StylesheetResource.CellFormat, configure);
    }

    /// <summary>
    /// Creates one resource for each enum member of <c>TEnum</c>.
    /// </summary>
    private static StyleIds<TEnum, TId> Create<TEnum, TId, TResource>(
        StylesheetBuilder builder,
        StylesheetResource resource,
        Action<TResource, TEnum> configure)

        where TEnum : struct, Enum
        where TResource : OpenXmlCompositeElement
    {
        TEnum IdFromInt(uint i)
        {
            return Unsafe.As<uint, TEnum>(ref i);
        }

        uint Next(TEnum val)
        {
            var (i, it) = builder.Next(resource);
            var id = IdFromInt(i);
            configure((TResource) it, val);
            return i;
        }

        var e = new AllEnumEnumerable<TEnum>().GetEnumerator();
        bool good = e.MoveNext();
        Debug.Assert(good);
        var first = Next(e.Current);
        while (e.MoveNext())
        {
            _ = Next(e.Current);
        }
        return new(first);
    }

}
