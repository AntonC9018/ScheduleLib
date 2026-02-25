using System.ComponentModel.DataAnnotations;
using AutoConstructor.Attributes;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;

namespace ScheduleLib.Theses.Parsing;

public sealed class ThesesListProvider
{
    private readonly GoogleHttpClientProvider _clientProvider;
    private readonly ThesesListProviderOptions _opts;

    public static void Register(IServiceCollection services)
    {
        services.AddOptions<ThesesListProviderOptions>().ValidateDataAnnotations();
        services.ConfigureOptions<DefaultThesesListProviderConfigureOptions>();
        services.AddScoped<ThesesListProvider>();
    }

    public ThesesListProvider(
        IOptions<ThesesListProviderOptions> opts,
        GoogleHttpClientProvider clientProvider)
    {
        _clientProvider = clientProvider;
        _opts = opts.Value;
    }

    private async Task DownloadTheses(
        FileStream outputFile,
        CancellationToken cancellationToken)
    {
        using var service = new DriveService(new()
        {
            ApiKey = _opts.GoogleApiKey,
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

    public async Task<OneForEachEnumMemberArray<ThesisType, ThesisList>> DownloadAndParse(
        CancellationToken cancellationToken)
    {
        await using var outputFile = new FileStream(_opts.OutputFilePath, FileMode.Create, FileAccess.ReadWrite);
        await DownloadTheses(outputFile, cancellationToken);

        var ret = OneForEach.Enum<ThesisType>().CreateArray<ThesisList>();
        foreach (var t in ret)
        {
            outputFile.Seek(0, SeekOrigin.Begin);
            var thesisList = ThesisListParser.Parse(outputFile, t.Key);
            t.Value = thesisList;
        }
        return ret;
    }
}

public sealed class ThesesListProviderOptions
{
    [MinLength(10)]
    public required string GoogleApiKey { get; set; }
    [MinLength(1)]
    public required string OutputFilePath { get; set; }
}

[AutoConstructor]
public sealed partial class DefaultThesesListProviderConfigureOptions : IConfigureOptions<ThesesListProviderOptions>
{
    private readonly IConfiguration _config;

    public void Configure(ThesesListProviderOptions options)
    {
        options.GoogleApiKey = _config.GetRequiredSection("Google:ApiKey").Get<string>()
            ?? throw new InvalidOperationException("Expected to find Google:ApiKey in the configuration");
        options.OutputFilePath = "data/theses.xlsx";
    }
}
