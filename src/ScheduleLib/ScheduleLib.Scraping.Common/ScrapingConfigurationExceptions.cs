namespace ScheduleLib.Scraping.Common;

public enum TokenAuthConfigurationError
{
    MissingPasswordLogin,
    CachingWithoutPasswordLogin,
}

public sealed class MissingScrapingConfigurationSectionException(Type configType)
    : InvalidOperationException($"ConfigurationSection must be set before registering options for {configType.Name}.")
{
    public Type ConfigType { get; } = configType;
}

public sealed class InvalidTokenAuthConfigurationException : NotSupportedException
{
    private InvalidTokenAuthConfigurationException(TokenAuthConfigurationError error, string message)
        : base(message)
    {
        Error = error;
    }

    public TokenAuthConfigurationError Error { get; }

    public static InvalidTokenAuthConfigurationException ForCachingWithoutPasswordLogin()
    {
        return new(
            TokenAuthConfigurationError.CachingWithoutPasswordLogin,
            "Token caching requires configuring a password login method.");
    }

    public static InvalidTokenAuthConfigurationException ForMissingPasswordLogin()
    {
        return new(
            TokenAuthConfigurationError.MissingPasswordLogin,
            "Token authentication requires configuring a password login method.");
    }
}

public sealed class UnsupportedDebugDocumentTypeException(Type documentType)
    : InvalidOperationException($"Cannot save debug output for unsupported document type {documentType.Name}.")
{
    public Type DocumentType { get; } = documentType;
}
