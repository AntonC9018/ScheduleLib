namespace MainCli.BuilderNew;

public interface ICredentialsConfig
{
    public CredentialsSource? Credentials { get; set; }
}

public sealed class CredentialsSource
{
}

public readonly struct CredentialsSourceBuilder
{
    private readonly CredentialsSource _source;

    public CredentialsSourceBuilder(CredentialsSource source)
    {
        _source = source;
    }
}

public static class CredentialsBuilderExtensions
{
    extension<T> (ConfigBuilder<T> builder)
        where T : class, ICredentialsConfig, IConfig<T>, new()
    {
        public CredentialsSourceBuilder Credentials()
        {
            var t = new CredentialsSource();
            builder.Enable().Value.Credentials = t;
            return new(t);
        }
    }

    extension (CredentialsSourceBuilder builder)
    {
        public void FromConfig(bool isRequired = false)
        {
        }
    }
}
