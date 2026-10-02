using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace FmiWebsiteInterop;

public sealed record FmiScheduleLink(int Year, Uri Url, DateOnly? UploadDate);
public sealed record FmiScheduleDownload(FmiScheduleLink Link, byte[] Content);

public sealed class FmiScheduleClient(HttpClient http)
{
    public static readonly Uri SchedulePage = new("https://fmi.usm.md/orar/");

    public async Task<IReadOnlyList<FmiScheduleDownload>> DownloadAsync(CancellationToken cancellationToken = default)
    {
        var html = await http.GetStringAsync(SchedulePage, cancellationToken);
        var links = Discover(html, SchedulePage);
        var downloads = new List<FmiScheduleDownload>();
        foreach (var link in links)
        {
            var bytes = await http.GetByteArrayAsync(link.Url, cancellationToken);
            if (!bytes.AsSpan().StartsWith("%PDF-"u8)) throw new FormatException($"{link.Url} did not return a PDF.");
            downloads.Add(new(link, bytes));
        }
        return downloads;
    }

    public static IReadOnlyList<FmiScheduleLink> Discover(string html, Uri page)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var prefix = new StringBuilder();
        var links = new Dictionary<int, FmiScheduleLink>();
        Visit(document.Body ?? throw new FormatException("Schedule page has no body."));
        if (!links.Keys.Order().SequenceEqual(new[] { 1, 2, 3 }))
            throw new FormatException("Expected exactly the three regular bachelor schedule PDFs.");
        return links.Values.OrderBy(l => l.Year).ToArray();

        void Visit(INode node)
        {
            if (node is IText text) { prefix.Append(text.Data); return; }
            if (node is IElement element)
            {
                if (element.LocalName is "script" or "style") return;
                if (element.LocalName == "br") prefix.Append(' ');
                if (element.LocalName == "a" && element.GetAttribute("href") is { } href
                    && Uri.TryCreate(page, href, out var uri) && uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var match = Regex.Match(prefix.ToString(), @"Anul\s+(III|II|I)\s+Licen[ţț][ăa]:?\s*$");
                    if (match.Success)
                    {
                        var year = match.Groups[1].Value.Length;
                        if (!links.TryAdd(year, new(year, uri, ReadUploadDate(element))))
                            throw new FormatException($"Ambiguous schedule PDF links for year {year}.");
                    }
                }
            }
            foreach (var child in node.ChildNodes) Visit(child);
        }
    }

    private static DateOnly? ReadUploadDate(IElement link)
    {
        // Only metadata explicitly attached to this schedule. Semester ranges,
        // WordPress page dates, URL directories and PDF creation dates are not upload dates.
        var value = link.GetAttribute("data-upload-date") ?? link.QuerySelector("time[datetime]")?.GetAttribute("datetime");
        if (value is null)
        {
            var labelled = Regex.Match(link.GetAttribute("title") ?? link.TextContent,
                @"(?i)(?:uploaded|încărcat[ă]?|actualizat[ă]?)\s*:?\s*(\d{4}-\d{2}-\d{2}|\d{2}\.\d{2}\.\d{4})");
            if (labelled.Success) value = labelled.Groups[1].Value;
        }
        if (value is null) return null;
        var datePart = value.Split('T')[0];
        if (!DateOnly.TryParseExact(datePart, ["yyyy-MM-dd", "dd.MM.yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            throw new FormatException($"Invalid schedule upload date: {value}");
        return date;
    }
}
