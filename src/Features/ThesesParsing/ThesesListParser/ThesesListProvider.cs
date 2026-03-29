using AutoConstructor.Attributes;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Helper;

namespace ScheduleLib.Theses.Parsing;

[AutoConstructor]
public sealed partial class ThesesListProvider
{
    private readonly ThesisListParser _parser;
    private readonly IThesesFileProvider _thesesFileProvider;

    public static void Register(IServiceCollection services)
    {
        services.AddScoped<ThesesListProvider>();
        ThesisListParser.Register(services);
    }

    public async Task<OneForEachEnumMemberArray<ThesisType, ThesisList>> DownloadAndParse(
        CancellationToken cancellationToken)
    {
        await using var inputFile = await _thesesFileProvider.Open(cancellationToken);

        var ret = OneForEach.Enum<ThesisType>().CreateArray<ThesisList>();
        foreach (var t in ret)
        {
            inputFile.Seek(0, SeekOrigin.Begin);
            var thesisList = _parser.Parse(inputFile, t.Key);
            t.Value = thesisList;
        }
        return ret;
    }
}

public interface IThesesFileProvider
{
    public ValueTask<Stream> Open(CancellationToken cancellationToken);
}
