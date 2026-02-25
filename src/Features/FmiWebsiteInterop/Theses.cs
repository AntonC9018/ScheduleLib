using System.Collections.Immutable;
using System.Text;
using AutoConstructor.Attributes;
using ScheduleLib;
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

[AutoConstructor]
public sealed partial class ThesesConversionTaskHandler
{
    private readonly Parsing.ThesesListProvider _thesesListProvider;

    public async Task Handle(
        CancellationToken cancellationToken,
        OutputDirectory outputDirectory)
    {
        var theses = await _thesesListProvider.DownloadAndParse(cancellationToken);

        var forSerialization = theses.SelectMany(x => x.Value.Items.Select((it, i) => (Type: x.Key, Item: it, Id: i)))
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
}
