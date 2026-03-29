using System.Collections.Immutable;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using AutoConstructor.Attributes;
using FmiWebsiteInterop.Api;
using FmiWebsiteInterop.Teachers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Parsing;

using Parsing = ScheduleLib.Theses.Parsing;

namespace FmiWebsiteInterop.Theses;

public sealed class RootObject
{
    public required ImmutableArray<Thesis> Content { get; set; }
}

public sealed class Thesis
{
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
            ThesisNameRo = thesis.ThesisNames[Parsing.ThesisNameLanguage.Ro],
            ThesisNameRu = thesis.ThesisNames[Parsing.ThesisNameLanguage.Ru],
            ThesisNameEn = thesis.ThesisNames[Parsing.ThesisNameLanguage.En],
        };
    }
}

public sealed class ThesesConversionTaskHandler(
    Parsing.ThesesListProvider _thesesListProvider,
    ItUsmWebsiteTeacherDataProvider _teacherData,
    [param: FromKeyedServices(Parsing.ThesisListParser.TeacherNameRemapperKey)]
    Parsing.INameRemapper _nameRemapper,
    ILogger<ThesesConversionTaskHandler> _logger)
{

    public async Task Handle(
        CancellationToken cancellationToken,
        OutputDirectory outputDirectory)
    {
        var theses = await _thesesListProvider.DownloadAndParse(cancellationToken);
        var slugByName = await _teacherData.SlugMap(cancellationToken);

        var forSerialization = theses.SelectMany(x => x.Value.Items.Select((it, i) => (Type: x.Key, Item: it, Id: i)))
            .GroupBy(x => x.Item.TeacherName, Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance)
            .Select(x => (
                Teacher: x.Key,
                Theses: x
                    .OrderBy(i => i.Type)
                    .ThenBy(i => i.Id)
                    .Select(i => ThesesJsonHelper.ConvertThesis(i.Type, i.Item))));

        foreach (var x in forSerialization)
        {
            var name = _nameRemapper.RemapName(x.Teacher);
            if (!slugByName.TryGetValue(name, out var slug))
            {
                _logger.LogWarning("Not found mapping teacher '{TeacherName}' from theses list to the website", name);
                continue;
            }
            var fileName = $"{slug}.json";
            await using var outputFile = outputDirectory.OpenFile(fileName, FileMode.Create, FileAccess.Write);
            await JsonSerializer.SerializeAsync(
                outputFile,
                new RootObject
                {
                    Content = [.. x.Theses],
                },
                cancellationToken: cancellationToken,
                options: ItUsmWebsiteApi.JsonOptions);
        }
    }
}
