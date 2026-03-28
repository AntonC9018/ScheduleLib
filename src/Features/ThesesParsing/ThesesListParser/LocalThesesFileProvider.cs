using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ScheduleLib.Theses.Parsing;

public sealed class LocalThesesFileProvider(
    IOptions<LocalThesesFileProviderOptions> _opts) : IThesesFileProvider
{
    public static void Register(IServiceCollection services)
    {
        services.AddOptions<LocalThesesFileProviderOptions>().ValidateDataAnnotations();
        services.AddSingleton<IThesesFileProvider, LocalThesesFileProvider>();
    }

    public ValueTask<Stream> Open(CancellationToken cancellationToken)
    {
        var opts = _opts.Value;
        var filePath = opts.FilePath;
#pragma warning disable CA2000
        var f = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
#pragma warning restore CA2000
        return ValueTask.FromResult((Stream) f);
    }
}

public sealed class LocalThesesFileProviderOptions
{
    [MinLength(1)]
    public required string FilePath { get; set; } = "data/theses.xlsx";
}

