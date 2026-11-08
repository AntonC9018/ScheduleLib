namespace FmiWebsiteInterop;

public sealed class FmiScheduleLinkDidNotReturnPdfException(Uri url)
    : FormatException($"{url} did not return a PDF.")
{
    public Uri Url { get; } = url;
}

public sealed class AmbiguousFmiScheduleLinksException(int year)
    : FormatException($"Ambiguous schedule PDF links for year {year}.")
{
    public int Year { get; } = year;
}

public sealed class InvalidFmiScheduleUploadDateException(string value)
    : FormatException($"Invalid schedule upload date: {value}")
{
    public string Value { get; } = value;
}
