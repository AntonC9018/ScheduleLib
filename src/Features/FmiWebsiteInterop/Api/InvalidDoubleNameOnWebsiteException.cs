namespace FmiWebsiteInterop.Api;

public sealed class InvalidDoubleNameOnWebsiteException(string value)
    : InvalidOperationException($"Invalid double name on the website '{value}'")
{
    public string Value { get; } = value;
}
