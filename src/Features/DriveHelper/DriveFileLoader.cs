using System.ComponentModel.DataAnnotations;
using AutoConstructor.Attributes;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;

namespace ScheduleLib.Theses.Parsing;

public enum DriveFileType
{
    Excel,
}

public sealed class DriveFileLoader : IDisposable
{
    private readonly DriveService _driveService;

    public static void Register(IServiceCollection services)
    {
        services.AddOptions<DriveFileLoaderOptions>().ValidateDataAnnotations();
        services.ConfigureOptions<DefaultDriveFileLoaderConfigureOptions>();
        services.AddScoped<DriveFileLoader>();
    }

    public DriveFileLoader(
        GoogleHttpClientProvider clientProvider,
        IOptions<DriveFileLoaderOptions> opts)
    {
        var service = new DriveService(new()
        {
            ApiKey = opts.Value.GoogleApiKey,
            ApplicationName = "Schedule",
            HttpClientFactory = clientProvider,
        });
        _driveService = service;
    }

    // TODO: how to start a download and have it NOT reach full completion before returning?
    public async Task<IDownloadProgress> Load(
        Stream outputStream,
        string fileId,
        DriveFileType fileType,
        CancellationToken cancellationToken)
    {
        const string xlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
        var fileTypeString = fileType switch
        {
            DriveFileType.Excel => xlsxMimeType,
            _ => throw Unreachable(),
        };
        var request = _driveService.Files.Export(
            fileId: fileId,
            mimeType: fileTypeString);
        var progress = await request.DownloadAsync(outputStream, cancellationToken);
        return progress;
    }

    public void Dispose()
    {
        _driveService.Dispose();
    }
}

public sealed class DriveFileLoaderOptions
{
    [MinLength(10)]
    public required string GoogleApiKey { get; set; }
}

[AutoConstructor]
public sealed partial class DefaultDriveFileLoaderConfigureOptions : IConfigureOptions<DriveFileLoaderOptions>
{
    private readonly IConfiguration _config;

    public void Configure(DriveFileLoaderOptions options)
    {
        options.GoogleApiKey = _config.GetRequiredSection("Google:ApiKey").Get<string>()
            ?? throw new InvalidOperationException("Expected to find Google:ApiKey in the configuration");
    }
}

