using System.Diagnostics;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace ReaderApp;

public readonly record struct FontId(uint Value);
public readonly record struct FillId(uint Value);
public readonly record struct BorderId(uint Value);
public readonly record struct CellFormatId(uint Value);

public readonly struct StylesheetBuilder : IDisposable
{
    private readonly Stylesheet _stylesheet;
    private readonly CountedFonts _fonts;
    private readonly CountedFills _fills;
    private readonly CountedBorders _borders;
    private readonly CountedCellFormats _cellFormats;

    public StylesheetBuilder(
        Stylesheet stylesheet,
        CountedFonts fonts,
        CountedFills fills,
        CountedBorders borders,
        CountedCellFormats cellFormats)
    {
        _stylesheet = stylesheet;
        _fonts = fonts;
        _fills = fills;
        _borders = borders;
        _cellFormats = cellFormats;
    }

    public FontId Font(Action<Font> configure)
    {
        var (id, font) = _fonts.Next();
        configure(font);
        return new(id);
    }

    public FillId Fill(Action<Fill> configure)
    {
        var (id, fill) = _fills.Next();
        configure(fill);
        return new(id);
    }

    public BorderId Border(Action<Border> configure)
    {
        var (id, border) = _borders.Next();
        configure(border);
        return new(id);
    }

    public CellFormatId CellFormat(Action<CellFormat> configure)
    {
        var (id, cellFormat) = _cellFormats.Next();
        configure(cellFormat);

        if (cellFormat.Alignment != null && cellFormat.ApplyAlignment == null)
        {
            cellFormat.ApplyAlignment = true;
        }
        if (cellFormat.FontId != null || cellFormat.ApplyFont == null)
        {
            cellFormat.ApplyFont = true;
        }
        if (cellFormat.FillId != null && cellFormat.ApplyFill == null)
        {
            cellFormat.ApplyFill = true;
        }
        if (cellFormat.BorderId != null && cellFormat.ApplyBorder == null)
        {
            cellFormat.ApplyBorder = true;
        }

        return new(id);
    }

    internal (uint Id, OpenXmlElement Obj) Next(StylesheetResource resource)
    {
        INext inext = resource switch
        {
            StylesheetResource.Font => _fonts,
            StylesheetResource.Fill => _fills,
            StylesheetResource.Border => _borders,
            StylesheetResource.CellFormat => _cellFormats,
            _ => throw new ArgumentOutOfRangeException(nameof(resource)),
        };
        return inext.Next();
    }

    public void Dispose()
    {
        Save();
    }

    public void Save()
    {
        if (!_fonts.HasDefault())
        {
            _fonts.AppendDefault();
        }
        if (!_fills.HasDefault())
        {
            _fills.AppendDefault();
        }
        if (!_borders.HasDefault())
        {
            _borders.AppendDefault();
        }
        if (!_cellFormats.HasDefault())
        {
            _cellFormats.AppendDefault();
        }
        _stylesheet.Save();
    }

    public static StylesheetBuilder CreateWithDefaults(WorkbookPart workbookPart)
    {
        var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>();

        var stylesheet = new Stylesheet();
        stylesPart.Stylesheet = stylesheet;

        var fonts = new CountedFonts();
        stylesheet.AppendChild(fonts.Element);
        fonts.AppendDefault();

        var fills = new CountedFills();
        stylesheet.AppendChild(fills.Element);
        fills.AppendDefault();

        var borders = new CountedBorders();
        stylesheet.AppendChild(borders.Element);
        borders.AppendDefault();

        var cellFormats = new CountedCellFormats();
        stylesheet.AppendChild(cellFormats.Element);
        cellFormats.AppendDefault();

        var ret = new StylesheetBuilder(
            stylesheet,
            fonts,
            fills,
            borders,
            cellFormats);
        return ret;
    }
}

public enum StylesheetResource
{
    Font,
    Fill,
    Border,
    CellFormat,
}

internal interface INext
{
    (uint Id, OpenXmlElement Obj) Next();
}

public abstract class CountedAdderBase<T, TItem>

    : INext

    where T : OpenXmlCompositeElement, new()
    where TItem : OpenXmlElement, new()
{
    public T Element { get; } = new();
    private uint _index = 0;

    public void AssertHasDefault()
    {
        Debug.Assert(_index != 0);
    }

    public bool HasDefault()
    {
        return _index != 0;
    }

    (uint Id, OpenXmlElement Obj) INext.Next()
    {
        return Next();
    }

    public (uint Id, TItem Item) Next()
    {
        var ret = new TItem();
        Element.Append(ret);
        var id = _index;
        _index++;
        return (id, ret);
    }

    public virtual TItem AppendDefault()
    {
        (_, var it) = Next();
        return it;
    }
}

public sealed class CountedFonts : CountedAdderBase<Fonts, Font>
{
}

public sealed class CountedFills : CountedAdderBase<Fills, Fill>
{
    public override Fill AppendDefault()
    {
        var fill = base.AppendDefault();
        fill.PatternFill = new()
        {
            PatternType = PatternValues.None,
        };
        return fill;
    }
}

public sealed class CountedBorders : CountedAdderBase<Borders, Border>
{
}

public sealed class CountedCellFormats : CountedAdderBase<CellFormats, CellFormat>
{
}

public static partial class StylesheetBuilderHelper
{
    public static void SetFont(this CellFormat format, FontId fontId)
    {
        format.FontId = new(fontId.Value);
    }

    public static void SetFill(this CellFormat format, FillId fillId)
    {
        format.FillId = new(fillId.Value);
    }

    public static void SetBorder(this CellFormat format, BorderId borderId)
    {
        format.BorderId = new(borderId.Value);
    }

    public static void CenterAndWrap(this CellFormat style)
    {
        style.Alignment = new()
        {
            Horizontal = HorizontalAlignmentValues.Center,
            Vertical = VerticalAlignmentValues.Center,
            WrapText = true,
        };
    }

    public static void AllSides(this Border border, BorderStyleValues style)
    {
        border.LeftBorder = new()
        {
            Style = style,
        };
        border.RightBorder = new()
        {
            Style = style,
        };
        border.TopBorder = new()
        {
            Style = style,
        };
        border.BottomBorder = new()
        {
            Style = style,
        };
    }
}

public static class StyleHelper
{
    public static void SetStyle(this Cell cell, CellFormatId formatId)
    {
        cell.StyleIndex = new(formatId.Value);
    }
}
