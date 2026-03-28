using System.ComponentModel.DataAnnotations;
using AutoConstructor.Attributes;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;

namespace ScheduleLib.Theses.Parsing;

public sealed class OnlineThesesFileProviderOptions
{
    [MinLength(10)]
    public required string GoogleApiKey { get; set; }
    [MinLength(1)]
    public required string OutputFilePath { get; set; } = "data/online-theses.xlsx";
}

[AutoConstructor]
public sealed partial class DefaultOnlineThesesListProviderConfigureOptions : IConfigureOptions<OnlineThesesFileProviderOptions>
{
    private readonly IConfiguration _config;

    public void Configure(OnlineThesesFileProviderOptions options)
    {
        options.GoogleApiKey = _config.GetRequiredSection("Google:ApiKey").Get<string>()
            ?? throw new InvalidOperationException("Expected to find Google:ApiKey in the configuration");
    }
}

[AutoConstructor]
public sealed partial class OnlineThesesFileProvider : IThesesFileProvider
{
    private readonly GoogleHttpClientProvider _clientProvider;
    private readonly IOptions<OnlineThesesFileProviderOptions> _opts;

    public static void Register(IServiceCollection services)
    {
        services.AddOptions<OnlineThesesFileProviderOptions>().ValidateDataAnnotations();
        services.ConfigureOptions<DefaultOnlineThesesListProviderConfigureOptions>();
        services.AddSingleton<IThesesFileProvider, OnlineThesesFileProvider>();
    }

    public async ValueTask<Stream> Open(CancellationToken cancellationToken)
    {
        var opts = _opts.Value;
        var outputFile = new FileStream(opts.OutputFilePath, FileMode.Create, FileAccess.ReadWrite);
        try
        {
            using var service = new DriveService(new()
            {
                ApiKey = opts.GoogleApiKey,
                ApplicationName = "Schedule",
                HttpClientFactory = _clientProvider,
            });
            const string xlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            var request = service.Files.Export(
                fileId: "1Wvz4SDxm18fKPZkqwGUfnMTSibTSrKrvf0MIPrWqB_I",
                mimeType: xlsxMimeType);
            var progress = await request.DownloadAsync(outputFile, cancellationToken);
            if (progress.Status != DownloadStatus.Completed)
            {
                throw new Exception("Failed download", progress.Exception);
            }
        }
        catch
        {
            await outputFile.DisposeAsync();
            throw;
        }
        return outputFile;
    }
}
