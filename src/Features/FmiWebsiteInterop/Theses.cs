using System.Collections.Immutable;
using AutoConstructor.Attributes;
using Google.Apis.Drive.v3;
using ScheduleLib.Helper;
using Theses;

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

    private static ThesisType ConvertType(global::Theses.ThesisType type)
    {
        return type switch
        {
            global::Theses.ThesisType.An => An,
            global::Theses.ThesisType.Licenta => Licenta,
            global::Theses.ThesisType.Master => Master,
            _ => throw Unreachable(),
        };
    }


}


[AutoConstructor]
public sealed partial class ThesesConversionTaskHandler
{
    private readonly GoogleHttpClientProvider
    public async Task Handle(ClientCredentials credentials)
    {
        var httpClientProvider = c.Services.GetRequiredService<GoogleHttpClientProvider>();

        await using var outputFile = new FileStream("data/theses.xlsx", FileMode.Create, FileAccess.ReadWrite);
        {
            using var service = new DriveService(new()
            {
                ApiKey = credentials.ClientSecret,
                ApplicationName = "Schedule",
                HttpClientFactory = httpClientProvider,
            });
            const string xlsxMimeType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
            var request = service.Files.Export(
                fileId: "1Wvz4SDxm18fKPZkqwGUfnMTSibTSrKrvf0MIPrWqB_I",
                mimeType: xlsxMimeType);
            await request.DownloadAsync(outputFile, c.CancellationToken);
        }

        var ret = OneForEach.Enum<ThesisType>().CreateArray<ThesisList>();
        foreach (var t in ret)
        {
            outputFile.Seek(0, SeekOrigin.Begin);
            var thesisList = ThesisListParser.Parse(outputFile, t.Key);
            t.Value = thesisList;
        }

    }
}
