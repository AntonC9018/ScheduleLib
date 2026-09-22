using System.Text.Json;
using PdfSharp.Fonts;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using PdfSharp.Drawing;

if (args.Length != 4)
{
    Console.Error.WriteLine(
        "Usage: PdfTemplateProof <background-directory> <manifest-directory> <output-directory> <italic-font-file>");
    return 2;
}

var backgroundDirectory = Path.GetFullPath(args[0]);
var manifestDirectory = Path.GetFullPath(args[1]);
var outputDirectory = Path.GetFullPath(args[2]);
var fontPath = Path.GetFullPath(args[3]);

if (!File.Exists(fontPath))
{
    throw new FileNotFoundException("The italic font file was not found.", fontPath);
}

Directory.CreateDirectory(outputDirectory);
GlobalFontSettings.FontResolver = new SingleItalicFontResolver(fontPath);

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
};

foreach (var manifestPath in Directory.EnumerateFiles(manifestDirectory, "*.json").Order())
{
    var manifest = JsonSerializer.Deserialize<TemplateManifest>(
        File.ReadAllText(manifestPath), jsonOptions)
        ?? throw new InvalidDataException($"Could not read {manifestPath}.");

    var backgroundPath = Path.Combine(backgroundDirectory, manifest.Template);
    if (!File.Exists(backgroundPath))
    {
        throw new FileNotFoundException("The PDF background was not found.", backgroundPath);
    }

    using var document = PdfReader.Open(backgroundPath, PdfDocumentOpenMode.Modify);
    var pageGraphics = new Dictionary<int, XGraphics>();
    var fonts = new Dictionary<double, XFont>();

    try
    {
        foreach (var field in manifest.Fields)
        {
            if (field.Page < 1 || field.Page > document.PageCount)
            {
                throw new InvalidDataException(
                    $"{Path.GetFileName(manifestPath)} places {field.Name} on missing page {field.Page}.");
            }

            var pageIndex = field.Page - 1;
            if (!pageGraphics.TryGetValue(pageIndex, out var graphics))
            {
                graphics = XGraphics.FromPdfPage(
                    document.Pages[pageIndex], XGraphicsPdfPageOptions.Append);
                pageGraphics.Add(pageIndex, graphics);
            }

            var rectangle = new XRect(field.X, field.Y, field.Width, field.Height);
            if (field.Cover)
            {
                graphics.DrawRectangle(XBrushes.White, rectangle);
            }
            graphics.DrawRectangle(new XPen(XColors.Red, 0.65), rectangle);

            var label = field.ProofLabel ?? "{{" + field.Name + "}}";
            var fontSize = FitFontSize(graphics, fonts, label, rectangle, Math.Min(field.Size, 10));
            var font = GetFont(fonts, fontSize);
            var format = field.Align.ToLowerInvariant() switch
            {
                "center" => XStringFormats.Center,
                "right" => XStringFormats.CenterRight,
                _ => XStringFormats.CenterLeft,
            };

            graphics.DrawString(label, font, XBrushes.Red, rectangle, format);
        }
    }
    finally
    {
        foreach (var graphics in pageGraphics.Values)
        {
            graphics.Dispose();
        }
    }

    var outputName = Path.GetFileNameWithoutExtension(manifest.Template) + "-placeholder-proof.pdf";
    var outputPath = Path.Combine(outputDirectory, outputName);
    document.Save(outputPath);
    Console.WriteLine($"Created {outputPath}");
}

return 0;

static double FitFontSize(
    XGraphics graphics,
    Dictionary<double, XFont> fonts,
    string text,
    XRect rectangle,
    double requestedSize)
{
    var size = Math.Max(4, requestedSize);
    while (size > 4)
    {
        var font = GetFont(fonts, size);
        var measured = graphics.MeasureString(text, font);
        if (measured.Width <= Math.Max(1, rectangle.Width - 2) &&
            measured.Height <= Math.Max(1, rectangle.Height - 1))
        {
            break;
        }

        size = Math.Max(4, size - 0.25);
    }

    return size;
}

static XFont GetFont(Dictionary<double, XFont> fonts, double size)
{
    if (!fonts.TryGetValue(size, out var font))
    {
        font = new XFont(
            SingleItalicFontResolver.FamilyName,
            size,
            XFontStyleEx.Italic,
            new XPdfFontOptions(PdfFontEncoding.Unicode, PdfFontEmbedding.TryComputeSubset));
        fonts.Add(size, font);
    }

    return font;
}

internal sealed record TemplateManifest(string Template, FontManifest Font, FieldPlacement[] Fields);

internal sealed record FontManifest(string Family, string Style);

internal sealed record FieldPlacement(
    string Name,
    int Page,
    double X,
    double Y,
    double Width,
    double Height,
    double Size,
    string Align,
    bool Cover = false,
    string? ProofLabel = null);

internal sealed class SingleItalicFontResolver(string fontPath) : IFontResolver
{
    public const string FamilyName = "Liberation Serif Proof";
    private const string FaceName = "LiberationSerif-Italic";
    private readonly byte[] fontBytes = File.ReadAllBytes(fontPath);

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic) =>
        new(FaceName, mustSimulateBold: isBold, mustSimulateItalic: false);

    public byte[] GetFont(string faceName) => faceName == FaceName
        ? fontBytes
        : throw new InvalidOperationException($"Unknown font face {faceName}.");
}
