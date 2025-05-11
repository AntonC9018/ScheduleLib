using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;

namespace ScheduleLib.Curriculum.Download;

public sealed class MicrosoftAuthConfig
{
    public required string TenantId { get; init; }
    public required string ClientId { get; init; }
}

public static class CredentialHelper
{
    public static IConfiguration CreateDefaultConfiguration(Assembly assembly)
    {
        var config = new ConfigurationBuilder();
        config.AddUserSecrets(assembly);
        var ret = config.Build();
        return ret;
    }

    public static MicrosoftAuthConfig GetMicrosoftGraphAuth(this IConfiguration c)
    {
        var microsoft = c.GetRequiredSection("Microsoft");
        var ret = microsoft.Get<MicrosoftAuthConfig>();
        if (ret is null)
        {
            throw new InvalidOperationException("Microsoft null");
        }
        if (ret.TenantId == null)
        {
            throw new InvalidOperationException("TenantId null");
        }
        if (ret.ClientId == null)
        {
            throw new InvalidOperationException("ClientId null");
        }
        return ret;
    }

    private const string _TokenPath = "tokencache.bin";

    public static async Task<InteractiveBrowserCredential> CreateMaybeLoadCredential(
        InteractiveBrowserCredentialOptions opts,
        Func<TokenRequestContext> contextFactory,
        CancellationToken cancellationToken)
    {
        Debug.Assert(opts.AuthenticationRecord == null);

        AuthenticationRecord? authRecord = null;
        try
        {
            await using var s = new FileStream(_TokenPath, FileMode.Open, FileAccess.Read);
            authRecord = await AuthenticationRecord.DeserializeAsync(s, cancellationToken);
        }
        catch (IOException)
        {
        }
        catch (JsonException)
        {
        }

        if (authRecord != null)
        {
            opts.AuthenticationRecord = authRecord;
        }

        var credential = new InteractiveBrowserCredential(opts);

        if (authRecord == null)
        {
            var context = contextFactory();
            authRecord = await credential.AuthenticateAsync(context, cancellationToken);
        }
        return credential;
    }

    private static readonly PropertyInfo RecordProperty = typeof(InteractiveBrowserCredential)
            .GetProperty("Record", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Property not found");

    public static async Task SerializeCredential(InteractiveBrowserCredential credential)
    {
        await using var s = new FileStream(_TokenPath, FileMode.Create, FileAccess.Write);
        var token = (Azure.Identity.AuthenticationRecord?) RecordProperty.GetValue(credential);
        if (token != null)
        {
            await token.SerializeAsync(s, CancellationToken.None);
        }
    }

    public static CredentialsDisposable AsDisposable(this InteractiveBrowserCredential c)
    {
        return new(c);
    }
}

public readonly struct CredentialsDisposable : IAsyncDisposable
{
    public readonly InteractiveBrowserCredential Credential;

    public CredentialsDisposable(InteractiveBrowserCredential credential)
    {
        Credential = credential;
    }

    public async ValueTask DisposeAsync()
    {
        await CredentialHelper.SerializeCredential(Credential);
    }
}

