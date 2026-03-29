using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ScheduleLib.Theses.Parsing;

public sealed class OnlineThesesFileProviderOptions
{
    [MinLength(1)]
    public required string OutputFilePath { get; set; } = "data/online-theses.xlsx";
}

public sealed class OnlineThesesFileProvider : IThesesFileProvider
{
    private readonly OnlineThesesFileProviderOptions _opts;
    private readonly DriveFileLoader _loader;

    public static void Register(IServiceCollection services)
    {
        services.AddOptions<OnlineThesesFileProviderOptions>().ValidateDataAnnotations();
        services.AddScoped<IThesesFileProvider, OnlineThesesFileProvider>();
        // DriveFileLoader.Register(services);
    }

    public OnlineThesesFileProvider(
        IOptions<OnlineThesesFileProviderOptions> opts,
        DriveFileLoader loader)
    {
        _opts = opts.Value;
        _loader = loader;
    }

    public async ValueTask<Stream> Open(CancellationToken cancellationToken)
    {
        var outputFile = new FileStream(_opts.OutputFilePath, FileMode.Create, FileAccess.ReadWrite);
        try
        {
            await _loader.Load(
                outputFile,
                fileId: "1Wvz4SDxm18fKPZkqwGUfnMTSibTSrKrvf0MIPrWqB_I",
                fileType: DriveFileType.Excel,
                cancellationToken);
        }
        catch
        {
            await outputFile.DisposeAsync();
            throw;
        }
        return outputFile;
    }
}

