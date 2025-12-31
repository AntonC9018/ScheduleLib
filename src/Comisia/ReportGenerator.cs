using System.Collections.Immutable;
using System.Xml;
using System.Xml.Serialization;
using OpenXmlPowerTools;
using ScheduleLib.Generation;
using ScheduleLib.Helper;

namespace Comisia;

public struct GenerateReportParams
{
    public required MatchedData Data;
    public required string OutputFileName;
    public required string TemplateFileName;
}
public static class ReportGenerator
{
    public static void GenerateReport(GenerateReportParams p)
    {
        var templateDocx = new FileInfo(p.TemplateFileName);

        var wmlTemplate = new WmlDocument(templateDocx.FullName);
        var xmldata = XmlModels.LoadDataIntoXmlDocument(p.Data);
        var afterAssembling = DocumentAssembler.AssembleDocument(wmlTemplate, xmldata, out bool returnedTemplateError);
        if (returnedTemplateError)
        {
            Console.WriteLine("There were errors");
        }
        var assembledDocx = new FileInfo(p.OutputFileName);
        afterAssembling.SaveAs(assembledDocx.FullName);

        ExplorerHelper.TryOpenExplorerAndSelectFile(assembledDocx.FullName);
    }
}

public static class XmlModels
{
    [XmlRoot("Data")]
    public sealed class Data
    {
        [XmlArray("Commissions")]
        [XmlArrayItem("Commission")]
        public required ImmutableArray<Commission> Commissions { get; init; }
    }

    public sealed class Commission
    {
        [XmlElement("CommissionName")]
        public required string CommissionName { get; init; }

        [XmlElement("Date")]
        public required string Date { get; init; }

        [XmlArray("Theses")]
        [XmlArrayItem("Thesis")]
        public required ImmutableArray<Thesis> Theses { get; init; }

        [XmlElement("HasMissingStudents")]
        public required bool HasMissingStudents { get; init; }

        [XmlArray("MissingStudents")]
        [XmlArrayItem("StudentName")]
        public required ImmutableArray<string> MissingStudents { get; init; }
    }

    public sealed class Thesis
    {
        [XmlElement("Number")]
        public required int Number { get; init; }

        [XmlElement("StudentName")]
        public required string StudentName { get; init; }

        [XmlElement("TeacherName")]
        public required string TeacherName { get; init; }

        [XmlElement("ThesisNameRo")]
        public required string ThesisNameRo { get; init; }

        [XmlElement("ThesisNameRu")]
        public required string ThesisNameRu { get; init; }

        [XmlElement("ThesisNameEn")]
        public required string ThesisNameEn { get; init; }
    }

    internal static XmlDocument LoadDataIntoXmlDocument(MatchedData data)
    {
        var model = new Data
        {
            Commissions = [
                .. data.Commissions.Select(c => new Commission
                {
                    CommissionName = $"Comisia {NumberHelper.ToRoman(c.CommissionNumber)}",
                    Date = c.Date.ToString("dd.MM.yy"),
                    Theses = [
                        .. c.Theses.Select((t, i) => new Thesis
                        {
                            Number = i + 1,
                            StudentName = t.StudentName.ToString(),
                            TeacherName = t.TeacherName.ToString(),
                            ThesisNameRo = t.ThesisNameRomanian,
                            ThesisNameRu = t.ThesisNameRussian ?? "",
                            ThesisNameEn = t.ThesisNameEnglish ?? "",
                        }),
                    ],
                    HasMissingStudents = !c.MissingStudents.IsEmpty,
                    MissingStudents = [.. c.MissingStudents.Select(s => s.ToString())],
                }),
            ],
        };

        var serializer = new XmlSerializer(typeof(Data));
        using var stream = new MemoryStream();
        serializer.Serialize(stream, model);
        stream.Position = 0; // Reset stream position for reading
        var xmlDoc = new XmlDocument();
        xmlDoc.Load(stream);
        return xmlDoc;
    }
}
