using System.Text.Json;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace EmploymentDocs;

/// <summary>
/// Stamps resolved field values onto clean PDF backgrounds using the JSON
/// placement manifests in <c>data/templates</c>.
/// Values are drawn in black embedded Liberation Serif Italic over the blank
/// underline areas. Rectangles marked with <c>cover</c> are first painted
/// white to hide the hardcoded example values left in the Word masters
/// (for example <c>2026–2027</c> or <c>01.09.2026</c>); the manifests keep
/// those rectangles clear of static wording such as “până la” or the
/// external-cumulation clause, which must remain visible.
/// <c>proofLabel</c> is ignored: it exists only for readable proof PDFs.
/// </summary>
internal static class PdfTemplateRenderer
{
    private const double MinFontSize = 7.0;
    private const double FontStep = 0.5;

    public static void Render(
        string backgroundPath,
        string outputPath,
        IReadOnlyDictionary<string, string> fields)
    {
        PdfFontProvider.EnsureInitialized();

        var manifest = PdfManifestCache.Load(backgroundPath);
        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using var document = PdfReader.Open(backgroundPath, PdfDocumentOpenMode.Modify);
        var graphicsByPage = new Dictionary<int, XGraphics>();

        try
        {
            foreach (var field in manifest.Fields)
            {
                if (field.Page < 1 || field.Page > document.PageCount)
                {
                    throw new InvalidOperationException(
                        $"Câmpul {field.Name} este plasat pe pagina inexistentă {field.Page} în {backgroundPath}.");
                }

                if (!fields.TryGetValue(field.Name, out var value))
                {
                    throw new InvalidOperationException(
                        $"Câmp de șablon fără valoare în {backgroundPath}: {field.Name}");
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    continue;
                }

                var pageIndex = field.Page - 1;
                if (!graphicsByPage.TryGetValue(pageIndex, out var graphics))
                {
                    graphics = XGraphics.FromPdfPage(
                        document.Pages[pageIndex], XGraphicsPdfPageOptions.Append);
                    graphicsByPage.Add(pageIndex, graphics);
                }

                var rectangle = new XRect(field.X, field.Y, field.Width, field.Height);
                if (field.Cover)
                {
                    graphics.DrawRectangle(XBrushes.White, rectangle);
                }

                DrawFitted(graphics, value, rectangle, field, backgroundPath);
            }
        }
        finally
        {
            foreach (var graphics in graphicsByPage.Values)
            {
                graphics.Dispose();
            }
        }

        document.Save(outputPath);
    }

    private static void DrawFitted(
        XGraphics graphics,
        string value,
        XRect rectangle,
        PdfFieldPlacement field,
        string backgroundPath)
    {
        var text = value.Trim();
        if (text.Length == 0)
        {
            return;
        }

        // Single line at the requested size, shrinking down to the minimum.
        var size = field.Size;
        while (size >= MinFontSize)
        {
            var font = PdfFontProvider.GetFont(size);
            var measured = graphics.MeasureString(text, font);
            if (measured.Width <= Math.Max(1, rectangle.Width - 2) &&
                measured.Height <= Math.Max(1, rectangle.Height - 1))
            {
                graphics.DrawString(text, font, XBrushes.Black, rectangle, AlignFormat(field.Align));
                return;
            }

            size -= FontStep;
        }

        // The rectangle is tall enough for a second line: wrap at the minimum size.
        var wrappedFont = PdfFontProvider.GetFont(MinFontSize);
        var lineHeight = graphics.MeasureString("Ag", wrappedFont).Height;
        if (rectangle.Height >= lineHeight * 1.5)
        {
            var lines = WrapLines(graphics, text, wrappedFont, Math.Max(1, rectangle.Width - 2));
            if (lines.Count > 1 && lines.Count * lineHeight <= rectangle.Height + 1)
            {
                DrawWrapped(graphics, lines, wrappedFont, rectangle, field.Align, lineHeight);
                return;
            }
        }

        throw new InvalidOperationException(
            $"Valoarea câmpului {field.Name} nu încape în {backgroundPath} " +
            $"(pagina {field.Page}, {rectangle.Width:0.#}x{rectangle.Height:0.#} pt): “{Truncate(text)}”.");
    }

    private static List<string> WrapLines(
        XGraphics graphics, string text, XFont font, double maxWidth)
    {
        var lines = new List<string>();
        var current = string.Empty;
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (graphics.MeasureString(candidate, font).Width <= maxWidth)
            {
                current = candidate;
            }
            else
            {
                if (current.Length == 0)
                {
                    // A single word exceeds the rectangle even at the minimum size.
                    return [text];
                }

                lines.Add(current);
                current = word;
            }
        }

        if (current.Length > 0)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static void DrawWrapped(
        XGraphics graphics,
        List<string> lines,
        XFont font,
        XRect rectangle,
        string align,
        double lineHeight)
    {
        var blockHeight = lines.Count * lineHeight;
        var y = rectangle.Y + (rectangle.Height - blockHeight) / 2;
        for (var index = 0; index < lines.Count; index++)
        {
            var lineRect = new XRect(rectangle.X, y + index * lineHeight, rectangle.Width, lineHeight);
            graphics.DrawString(lines[index], font, XBrushes.Black, lineRect, AlignFormat(align));
        }
    }

    private static XStringFormat AlignFormat(string align) => align.ToLowerInvariant() switch
    {
        "center" => XStringFormats.Center,
        "right" => XStringFormats.CenterRight,
        _ => XStringFormats.CenterLeft,
    };

    private static string Truncate(string value) =>
        value.Length <= 60 ? value : value[..57] + "...";
}

internal sealed record PdfTemplateManifest(string Template, PdfFieldPlacement[] Fields);

internal sealed record PdfFieldPlacement(
    string Name,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    double Size,
    string Align,
    bool Cover = false);

internal static class PdfManifestCache
{
    private static readonly Dictionary<string, PdfTemplateManifest> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lock Gate = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static PdfTemplateManifest Load(string backgroundPath)
    {
        var manifestPath = ResolveManifestPath(backgroundPath);
        lock (Gate)
        {
            if (Cache.TryGetValue(manifestPath, out var cached))
            {
                return cached;
            }

            var manifest = JsonSerializer.Deserialize<PdfTemplateManifest>(
                File.ReadAllText(manifestPath), JsonOptions)
                ?? throw new InvalidDataException($"Could not read {manifestPath}.");

            Cache[manifestPath] = manifest;
            return manifest;
        }
    }

    private static string ResolveManifestPath(string backgroundPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(backgroundPath) + ".json";
        var direct = Path.Combine(
            Path.GetDirectoryName(backgroundPath) ?? string.Empty, fileName);
        if (File.Exists(direct))
        {
            return Path.GetFullPath(direct);
        }

        var dataRelative = Path.Combine("data", "templates", fileName);
        return PdfDataFiles.Resolve(dataRelative);
    }
}

internal static class PdfDataFiles
{
    public static string Resolve(string relativePath)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, relativePath),
            Path.Combine(Directory.GetCurrentDirectory(), relativePath),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Fișierul de date nu a fost găsit: {relativePath}. " +
            "Rulați generatorul din directorul proiectului DocsTemplates.");
    }
}

internal static class PdfFontProvider
{
    public const string FamilyName = "Liberation Serif";
    private const string FaceName = "LiberationSerif-Italic";
    private const string FontRelativePath = "data/fonts/LiberationSerif-Italic.ttf";

    private static readonly Lock Gate = new();
    private static readonly Dictionary<double, XFont> Fonts = new();
    private static bool initialized;

    public static void EnsureInitialized()
    {
        lock (Gate)
        {
            if (initialized)
            {
                return;
            }

            var fontPath = PdfDataFiles.Resolve(FontRelativePath);
            GlobalFontSettings.FontResolver = new BundledItalicFontResolver(fontPath);
            initialized = true;
        }
    }

    public static XFont GetFont(double size)
    {
        lock (Gate)
        {
            if (!Fonts.TryGetValue(size, out var font))
            {
                font = new XFont(
                    FamilyName,
                    size,
                    XFontStyleEx.Italic,
                    new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset));
                Fonts[size] = font;
            }

            return font;
        }
    }

    private sealed class BundledItalicFontResolver(string fontPath) : IFontResolver
    {
        private readonly byte[] fontBytes = File.ReadAllBytes(fontPath);

        public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
            new(FaceName, mustSimulateBold: isBold, mustSimulateItalic: false);

        public byte[] GetFont(string faceName) => faceName == FaceName
            ? fontBytes
            : throw new InvalidOperationException($"Unknown font face {faceName}.");
    }
}
