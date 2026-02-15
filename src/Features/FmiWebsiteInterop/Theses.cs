using System.Collections.Immutable;
using System.Text;
using FmiWebsiteInterop.Theses.Parsing;
using Google.Apis.Download;
using Google.Apis.Drive.v3;
using ScheduleLib;
using ScheduleLib.Application.Config;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Helper;
using ScheduleLib.Parsing;
using Microsoft.Extensions.Configuration;
using IConfiguration = Microsoft.Extensions.Configuration.IConfiguration;

namespace FmiWebsiteInterop.Theses;

public sealed class RootObject
{
    public required ImmutableArray<Thesis> Content { get; set; }
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

    public static Thesis ConvertThesis(Parsing.ThesisType type, Parsing.Thesis thesis)
    {
        return new()
        {
            ThesisType = ConvertType(type),
            StudentGroup = thesis.GroupName,
            StudentName = thesis.StudentName.ToString(),
            Teacher = thesis.TeacherName.ToString(),
            ThesisNameEn = thesis.ThesisNameEnglish,
            ThesisNameRo = thesis.ThesisNameRomanian,
            ThesisNameRu = thesis.ThesisNameRussian,
        };
    }
}


public sealed class ThesesConversionTaskHandler
{
    private readonly GoogleHttpClientProvider _httpClientProvider;
    private readonly string _apiKey;

    public ThesesConversionTaskHandler(
        IConfiguration config,
        GoogleHttpClientProvider httpClientProvider)
    {
        _httpClientProvider = httpClientProvider;
        _apiKey = config.GetRequiredSection("Google:ApiKey").Get<string>()
            ?? throw new InvalidOperationException("Expected to find Google:ApiKey in the configuration");
    }

    public async Task Handle(
        CancellationToken cancellationToken,
        string thesesOutputFile,
        OutputDirectory outputDirectory)
    {
        OneForEachEnumMemberArray<Parsing.ThesisType, ThesisList> ret;
        {
            await using var outputFile = new FileStream(thesesOutputFile, FileMode.Create, FileAccess.ReadWrite);
            await DownloadTheses(outputFile, cancellationToken);

            ret = OneForEach.Enum<Parsing.ThesisType>().CreateArray<ThesisList>();
            foreach (var t in ret)
            {
                outputFile.Seek(0, SeekOrigin.Begin);
                var thesisList = ThesisListParser.Parse(outputFile, t.Key);
                t.Value = thesisList;
            }
        }

        var forSerialization = ret.SelectMany(x => x.Value.Items.Select((it, i) => (Type: x.Key, Item: it, Id: i)))
            .GroupBy(x => x.Item.TeacherName)
            .Select(x => (
                Teacher: x.Key,
                Theses: x
                    .OrderBy(i => i.Type)
                    .ThenBy(i => i.Id)
                    .Select(i => ThesesJsonHelper.ConvertThesis(i.Type, i.Item))));

        foreach (var x in forSerialization)
        {
            var sb = new StringBuilder();
            AppendNameAsFileName(sb, x.Teacher);
            sb.Append(".json");
            await using var outputFile = outputDirectory.OpenFile(sb.ToString(), FileMode.Create, FileAccess.Write);
            await System.Text.Json.JsonSerializer.SerializeAsync(outputFile, new RootObject
            {
                Content = [.. x.Theses],
            }, cancellationToken: cancellationToken);
        }
    }

    private static void AppendNameAsFileName(StringBuilder output, Name name)
    {
        var lb = new ListStringBuilder(output, "_");
        AppendPart(name.FirstName);
        lb.MaybeAppendSeparator();
        AppendPart(name.LastName);

        void AppendPart(NameParts<string?> x)
        {
            var nb = new ListStringBuilder(output, NameConstants.DoubleNameSeparator);
            foreach (var n in x)
            {
                if (n != null)
                {
                    nb.Append(n);
                }
            }
        }
    }

    private async Task DownloadTheses(
        FileStream outputFile,
        CancellationToken cancellationToken)
    {
        using var service = new DriveService(new()
        {
            ApiKey = _apiKey,
            ApplicationName = "Schedule",
            HttpClientFactory = _httpClientProvider,
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

}
