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
            if (!bytes.AsSpan()
                    .StartsWith("%PDF-"u8))
            {
                throw new FmiScheduleLinkDidNotReturnPdfException(link.Url);
            }

            global::System.Collections.Generic.List<global::FmiWebsiteInterop.FmiScheduleDownload> list = downloads;
            global::FmiWebsiteInterop.FmiScheduleDownload item = new(link, bytes);
            list.Add(item);
        }
        return downloads;
    }

    public static IReadOnlyList<FmiScheduleLink> Discover(string html, Uri page)
    {
        using var document = new HtmlParser().ParseDocument(html);
        var prefix = new StringBuilder();
        var links = new Dictionary<int, FmiScheduleLink>();
        Visit(document.Body ?? throw new FormatException("Schedule page has no body."));
        if (!links.Keys.Order()
                .SequenceEqual(new[]
                {
                    1,
                    2,
                    3,
                }))
        {
            throw new FormatException("Expected exactly the three regular bachelor schedule PDFs.");
        }

        return links.Values.OrderBy(l => l.Year)
            .ToArray();

        void Visit(INode node)
        {
            if (node is IText text)
            {
                prefix.Append(text.Data);
                return;
            }
            if (node is IElement element)
            {
                if (element.LocalName is "script" or "style")
                {
                    return;
                }

                if (element.LocalName == "br")
                {
                    prefix.Append(' ');
                }

                if (element.LocalName == "a")
                {
                    if (element.GetAttribute("href") is { } href)
                    {
                        if (Uri.TryCreate(baseUri: page,
                                href,
                                result: out var uri))
                        {
                            if (uri.AbsolutePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                            {
                                var match = Regex.Match(input: prefix.ToString(),
                                    pattern: @"Anul\s+(III|II|I)\s+Licen[ţț][ăa]:?\s*$");
                                if (match.Success)
                                {
                                    var year = match.Groups[1].Value.Length;
                                    global::System.Collections.Generic.Dictionary<int,
                                        global::FmiWebsiteInterop.FmiScheduleLink> dictionary = links;
                                    int key = year;
                                    int year2 = year;
                                    global::System.Uri url = uri;
                                    global::System.DateOnly? uploadDate = ReadUploadDate(element);
                                    global::FmiWebsiteInterop.FmiScheduleLink value = new(year2,
                                        url,
                                        uploadDate);
                                    bool tryAdd = dictionary.TryAdd(key, value);
                                    if (!tryAdd)
                                    {
                                        throw new AmbiguousFmiScheduleLinksException(year);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            foreach (var child in node.ChildNodes)
            {
                Visit(child);
            }
        }
    }

    private static DateOnly? ReadUploadDate(IElement link)
    {
        // Only metadata explicitly attached to this schedule. Semester ranges,
        // WordPress page dates, URL directories and PDF creation dates are not upload dates.
        var value = link.GetAttribute("data-upload-date")
            ?? link.QuerySelector("time[datetime]")
                ?.GetAttribute("datetime");
        if (value is null)
        {
            var labelled = Regex.Match(input: link.GetAttribute("title") ?? link.TextContent,
                pattern:
                @"(?i)(?:uploaded|încărcat[ă]?|actualizat[ă]?)\s*:?\s*(\d{4}-\d{2}-\d{2}|\d{2}\.\d{2}\.\d{4})");
            if (labelled.Success)
            {
                value = labelled.Groups[1].Value;
            }
        }
        if (value is null)
        {
            return null;
        }

        var datePart = value.Split('T')[0];
        if (!DateOnly.TryParseExact(datePart,
                ["yyyy-MM-dd", "dd.MM.yyyy"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var date))
        {
            throw new InvalidFmiScheduleUploadDateException(value);
        }

        return date;
    }
}
