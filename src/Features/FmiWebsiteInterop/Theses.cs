using System.Collections.Immutable;
using AutoConstructor.Attributes;
using FmiWebsiteInterop.Theses.Parsing;
using Google.Apis.Drive.v3;
using ScheduleLib.Application.Config;
using ScheduleLib.Helper;

namespace FmiWebsiteInterop.Theses;

public sealed class RootObject
{
    public required ImmutableArray<Thesis> ScheduleDaysDto { get; set; }
}

public sealed class Thesis
{
    // public required int ThesisId { get; init; }
    public required ThesisType ThesisType { get; init; }
    public required string Teacher { get; init; }
    public string? ThesisNameRo { get; init; }
    public string? ThesisNameEn { get; init; }
    public string? ThesisNameRu { get; init; }
    public required string StudentName { get; init; }
    public required string StudentGroup { get; init; }
}

public sealed class ThesisType
{
    public required int TypeId { get; init; }
    public required string Type { get; init; }
    public required string Label { get; init; }
}

public static class ThesesJsonHelper
{
    private static readonly ThesisType An = new()
    {
        TypeId = 1,
        Type = "TEZA_DE_AN",
        Label = "Teză de an",
    };
    private static readonly ThesisType Licenta = new()
    {
        TypeId = 2,
        Type = "TEZA_DE_LICENTA",
        Label = "Teză de licenta",
    };
    private static readonly ThesisType Master = new()
    {
        TypeId = 3,
        Type = "TEZA_DE_MASTER",
        Label = "Teză de master",
    };

    private static ThesisType ConvertType(Parsing.ThesisType type)
    {
        return type switch
        {
            Parsing.ThesisType.An => An,
            Parsing.ThesisType.Licenta => Licenta,
            Parsing.ThesisType.Master => Master,
            _ => throw Unreachable(),
        };
    }
}


[AutoConstructor]
public sealed partial class ThesesConversionTaskHandler
{
    private readonly GoogleHttpClientProvider _httpClientProvider;
    private readonly IServiceProvider _sp;

    public async Task Handle(CancellationToken cancellationToken)
    {
        var accessor = new GlobalConfigurationApiKeysSource("Google");
        var credentials = await accessor.Get(_sp, cancellationToken);

        await using var outputFile = new FileStream("data/theses.xlsx", FileMode.Create, FileAccess.ReadWrite);
        {
            using var service = new DriveService(new()
            {
                ApiKey = credentials.ClientSecret,
                ApplicationName = "Schedule",
                HttpClientFactory = _httpClientProvider,
            });
            const string xlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            var request = service.Files.Export(
                fileId: "1Wvz4SDxm18fKPZkqwGUfnMTSibTSrKrvf0MIPrWqB_I",
                mimeType: xlsxMimeType);
            await request.DownloadAsync(outputFile, cancellationToken);
        }

        var ret = OneForEach.Enum<Parsing.ThesisType>().CreateArray<ThesisList>();
        foreach (var t in ret)
        {
            outputFile.Seek(0, SeekOrigin.Begin);
            var thesisList = ThesisListParser.Parse(outputFile, t.Key);
            t.Value = thesisList;
        }

    }
}
