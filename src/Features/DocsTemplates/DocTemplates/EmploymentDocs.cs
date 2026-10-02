using System.Xml;
using System.Xml.Serialization;
using OpenXmlPowerTools;
using ScheduleLib.Helper;
using ScheduleLib.Helper.Parsing;

namespace EmploymentDocs;

public sealed class PersonInfo
{
    public required string FirstName;
    public required string LastName;
    // asistent universitar
    public required string Function;
    public required string Faculty;
    public required string Department;
    public required DateOnly Date;
    public required float Units;
    public required HireType HireType;
    public required string WorkingPlace;
    public required WorkingMode WorkingMode;
    public DateOnly ContractEndDate = DateOnly.MinValue;
    public DateOnly ProbationPeriodEndDate = DateOnly.MinValue;
    public required string HomeAddress;
    public required string PhoneNumber;
    public required string Email;
    public required IDInfo ID;
}

public sealed class IDInfo
{
    public required string BSeriesCode;
    public required DateOnly IssueDate;
    public required string PersonalIdentifier;
}

public enum WorkingMode
{
    Serviciu,
    Domiciliu,
    Birou,
}

public enum HireType
{
    CumulIntern,
    CumulExtern,
    Contract,
    Titular,
}

public struct FilePaths
{
    public required string Declaration;
    public required string DeclarationConsimtand;
    public required string Hire;
    public required string CIM;

    public readonly FilePaths Map(Func<string, string> f)
    {
        return new FilePaths
        {
            Declaration = f(Declaration),
            DeclarationConsimtand = f(DeclarationConsimtand),
            Hire = f(Hire),
            CIM = f(CIM),
        };
    }
}

public struct GenerateReportParams
{
    public required PersonInfo[] Data;
    public required string OutputDir;
    public required FilePaths TemplateFilePaths;
    public required FilePaths OutputFilePaths;
}

public static class DocsGenerator
{
    public static void Generate(GenerateReportParams p)
    {
        if (p.Data.Length == 0)
        {
            return;
        }

        foreach (var data in p.Data)
        {
            var outputDir = Path.Combine(p.OutputDir, $"{data.LastName}_{data.FirstName}");
            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, recursive: true);
            }

            var outputPaths = p.OutputFilePaths.Map(fp => Path.Combine(outputDir, Path.GetFileName(fp)));

            {
                var declarationData = XmlModels.ToDeclarationData(data);
                GenerateDoc(declarationData, p.TemplateFilePaths.Declaration, outputPaths.Declaration);
            }
            {
                var r = TrySplitUnits(data);
                for (int i = 0; i < r.Documents.Length; i++)
                {
                    var d = r.Documents[i];
                    var hireData = XmlModels.ToHireData(data, d.HireType, d.SplitUnits);
                    var outputFileName = r.OutputFileName(outputPaths.Hire, i);
                    GenerateDoc(hireData, p.TemplateFilePaths.Hire, outputFileName);
                }

                for (int i = 0; i < r.Documents.Length; i++)
                {
                    var d = r.Documents[i];
                    var hireData = XmlModels.ToCimData(data, d.SplitUnits);
                    var outputFileName = r.OutputFileName(outputPaths.CIM, i);
                    GenerateDoc(hireData, p.TemplateFilePaths.CIM, outputFileName);
                }
            }
            {
                var declarationConsimtandData = XmlModels.ToDeclarationConsimtandData(data);
                var outputFileName = outputPaths.DeclarationConsimtand;
                GenerateDoc(declarationConsimtandData, p.TemplateFilePaths.DeclarationConsimtand, outputFileName);
            }
        }

        var fullPath = Path.GetFullPath(p.OutputDir);
        ExplorerHelper.TryOpenExplorerAndSelectFile(fullPath);
    }
    private static string OutputFileName(this TrySplitUnitsResult r, string filePath, int index)
    {
        if (!r.IsMultipleDocuments)
        {
            return filePath;
        }

        string name = Path.GetFileNameWithoutExtension(filePath);
        string ext = Path.GetExtension(filePath);
        string newFilename = $"{name}{index + 1}{ext}";
        if (Path.GetDirectoryName(filePath) is { } dir)
        {
            return Path.Combine(dir, newFilename);
        }
        return newFilename;
    }

    private struct UnitsSplitInfo
    {
        public float SplitUnits;
        public int Number;
        public HireType HireType;
    }

    private readonly record struct TrySplitUnitsResult
    {
        public readonly UnitsSplitInfo[] Documents;
        public readonly bool IsMultipleDocuments => Documents.Length > 1;

        public TrySplitUnitsResult(UnitsSplitInfo[] a)
        {
            Documents = a;
        }
    }

    private static TrySplitUnitsResult TrySplitUnits(PersonInfo p)
    {
        float units = p.Units;
        bool isMultipleDocument = false;
        const float maxUnits = 1.0f;
        if (units > maxUnits)
        {
            isMultipleDocument = true;
        }

        int count = isMultipleDocument ? 2 : 1;
        var ret = new UnitsSplitInfo[count];

        int index = 0;
        HireType hireType = p.HireType;
        if (isMultipleDocument)
        {
            hireType = HireType.CumulIntern;
            ret[index] = new()
            {
                Number = index + 1,
                HireType = p.HireType,
                SplitUnits = maxUnits,
            };
            index++;
            units -= maxUnits;
        }

        if (units > maxUnits * 2)
        {
            throw new InvalidOperationException("Units can't be more than 2");
        }

        {
            ret[index] = new()
            {
                HireType = hireType,
                Number = index + 1,
                SplitUnits = units,
            };
            index++;
            _ = index;
        }

        return new(ret);
    }


    private static void GenerateDoc(
        object obj,
        string templatePath,
        string outputPath)
    {
        var xmldata = XmlModels.ObjectToXmlDocument(obj);
        var templateDocx = new FileInfo(templatePath);

        var wmlTemplate = new WmlDocument(templateDocx.FullName);
        var afterAssembling = DocumentAssembler.AssembleDocument(wmlTemplate, xmldata, out bool returnedTemplateError);
        if (returnedTemplateError)
        {
            Console.WriteLine("There were errors");
        }
        var assembledDocx = new FileInfo(outputPath);
        if (assembledDocx.Directory is { } dir)
        {
            dir.Create();
        }
        afterAssembling.SaveAs(assembledDocx.FullName);
    }
}

public static class XmlModels
{
    [XmlRoot("Data")]
    public sealed class DeclarationData
    {
        [XmlElement("Name")]
        public required string Name { get; init; }

        [XmlElement("Function")]
        public required string Function { get; init; }

        [XmlElement("Department")]
        public required string Department { get; init; }

        [XmlElement("Faculty")]
        public required string Faculty { get; init; }

        [XmlElement("Date")]
        public required string Date { get; init; }
    }

    [XmlRoot("Data")]
    public sealed class HireData
    {
        [XmlElement("Name")]
        public required string Name { get; init; }

        [XmlElement("Function")]
        public required string Function { get; init; }

        [XmlElement("Department")]
        public required string Department { get; init; }

        [XmlElement("Faculty")]
        public required string Faculty { get; init; }

        [XmlElement("Date")]
        public required string Date { get; init; }

        [XmlElement("HireType")]
        public required string HireType { get; init; }

        [XmlElement("Units")]
        public required string Units { get; init; }

        [XmlElement("DateFrom")]
        public required string DateFrom { get; init; }

        [XmlElement("DateTo")]
        public required string DateTo { get; init; }

        [XmlElement("YearFrom")]
        public required string YearFrom { get; init; }

        [XmlElement("YearTo")]
        public required string YearTo { get; init; }
    }

    [XmlRoot("Data")]
    public sealed class CIMData
    {
        [XmlElement("Name")]
        public required string Name { get; init; }

        [XmlElement("Function")]
        public required string Function { get; init; }

        [XmlElement("WorkingPlace")]
        public required string WorkingPlace { get; init; }

        [XmlElement("WorkMode")]
        public required string WorkMode { get; init; }

        [XmlElement("ContractEndDate")]
        public required string ContractEndDate { get; init; }

        [XmlElement("ProbationEndDate")]
        public required string ProbationEndDate { get; init; }

        [XmlElement("Units")]
        public required string Units { get; init; }

        [XmlElement("Risks")]
        public required string Risks { get; init; }

        [XmlElement("AdditionalRights")]
        public required string AdditionalRights { get; init; }

        [XmlElement("AdditionalObligations")]
        public required string AdditionalObligations { get; init; }

        [XmlElement("WorkRegime")]
        public required string WorkRegime { get; init; }

        [XmlElement("RestRegime")]
        public required string RestRegime { get; init; }

        [XmlElement("AnnualVacationDuration")]
        public required string AnnualVacationDuration { get; init; }

        [XmlElement("AdditionalAnnualVacationDuration")]
        public required string AdditionalAnnualVacationDuration { get; init; }

        [XmlElement("SpecialClauses")]
        public required string SpecialClauses { get; init; }

        [XmlElement("HomeAddress")]
        public required string HomeAddress { get; init; }

        [XmlElement("PhoneNumber")]
        public required string PhoneNumber { get; init; }

        [XmlElement("Email")]
        public required string Email { get; init; }

        [XmlElement("ID")]
        public required ID ID { get; init; }
    }

    public sealed class ID
    {
        [XmlElement("BSeriesCode")]
        public required string BSeriesCode { get; init; }

        [XmlElement("IssueDate")]
        public required string IssueDate { get; init; }

        [XmlElement("PersonalIdentifier")]
        public required string PersonalIdentifier { get; init; }
    }

    public sealed class DeclarationConsimtandData
    {
        [XmlElement("Name")]
        public required string Name { get; init; }

        [XmlElement("ID")]
        public required string ID { get; init; }

        [XmlElement("BOrI")]
        public required string BOrI { get; init; }

        [XmlElement("BSeries")]
        public required string BISeries { get; init; }

        [XmlElement("Date")]
        public required string Date { get; init; }

        [XmlElement("Year2")]
        public required string Year2 { get; init; }
    }

    internal static DeclarationData ToDeclarationData(PersonInfo data)
    {
        return new DeclarationData
        {
            Date = data.Date.ToString("dd.MM.yyyy"),
            Department = data.Department,
            Faculty = data.Faculty,
            Function = data.Function,
            Name = $"{data.LastName} {data.FirstName}",
        };
    }

    internal static HireData ToHireData(PersonInfo data, HireType hireType, float units)
    {
        var yearFrom = data.Date.Year;
        var yearTo = data.Date.Year + 1;

        var dateFrom = new DateOnly(year: yearFrom, month: 9, day: 1);
        var dateTo = new DateOnly(
            year: yearTo,
            month: hireType == HireType.CumulExtern ? 7 : 8,
            day: hireType == HireType.CumulExtern ? 5 : 31);

        return new HireData
        {
            Date = data.Date.ToString("dd.MM.yyyy"),
            Department = data.Department,
            Faculty = data.Faculty,
            Function = data.Function,
            Name = $"{data.LastName} {data.FirstName}",
            HireType = hireType switch
            {
                HireType.Contract => "contract",
                HireType.CumulExtern => "cumul extern",
                HireType.CumulIntern => "cumul intern",
                HireType.Titular => "titular",
                _ => throw Unreachable(),
            },
            Units = units.ToString("0.00"),
            DateFrom = dateFrom.ToString("dd.MM.yyyy"),
            DateTo = dateTo.ToString("dd.MM.yyyy"),
            YearFrom = yearFrom.ToString(),
            YearTo = yearTo.ToString(),
        };
    }

    internal static CIMData ToCimData(
        PersonInfo data,
        float units)
    {
        string contractEndDate = data.ContractEndDate == DateOnly.MinValue
            ? ""
            : data.ContractEndDate.ToString("dd.MM.yyyy");
        string probationEndDate = data.ProbationPeriodEndDate == DateOnly.MinValue
            ? ""
            : data.ProbationPeriodEndDate.ToString("dd.MM.yyyy");

        return new CIMData
        {
            Name = $"{data.LastName} {data.FirstName}",
            Function = data.Function,
            WorkingPlace = data.WorkingPlace,
            WorkMode = data.WorkingMode switch
            {
                WorkingMode.Birou => "birou",
                WorkingMode.Domiciliu => "domiciliu",
                WorkingMode.Serviciu => "serviciu",
                _ => throw Unreachable(),
            },
            Units = units.ToString("0.00"),
            ContractEndDate = contractEndDate,
            ProbationEndDate = probationEndDate,
            Risks = "",
            AdditionalRights = "",
            AdditionalObligations = "",
            WorkRegime = "", // "de 8 ore pe zi, de luni până vineri, între orele 8:00 - 16:00",
            RestRegime = "", // "zilele de sâmbătă și duminică, precum și sărbătorile legale",
            AnnualVacationDuration = "", // "20 de zile lucrătoare",
            AdditionalAnnualVacationDuration = "",
            SpecialClauses = "",
            HomeAddress = data.HomeAddress,
            PhoneNumber = data.PhoneNumber,
            Email = data.Email,
            ID = new ID
            {
                BSeriesCode = data.ID.BSeriesCode,
                IssueDate = data.ID.IssueDate.ToString("dd.MM.yyyy"),
                PersonalIdentifier = data.ID.PersonalIdentifier,
            },
        };
    }

    internal static (string BOrI, string Code) ParseBICode(string code)
    {
        var parser = new SequenceReader(code);
        var bparser = parser.BufferedView();
        bparser.SkipLetters();
        var bi = parser.PeekSpanUntilPosition(bparser.Position);
        parser.MoveTo(bparser.Position);
        var code1 = parser.PeekSpanUntilEnd();
        return (bi.ToString(), code1.ToString());
    }

    internal static DeclarationConsimtandData ToDeclarationConsimtandData(PersonInfo p)
    {
        var c = ParseBICode(p.ID.BSeriesCode);
        return new DeclarationConsimtandData
        {
            Name = $"{p.LastName} {p.FirstName}",
            ID = p.ID.PersonalIdentifier,
            BOrI = c.BOrI,
            BISeries = c.Code,
            Date = p.Date.ToString("dd.MM"),
            // 2 last digits of year
            Year2 = p.Date.ToString("yy"),
        };
    }

    internal static XmlDocument ObjectToXmlDocument(object obj)
    {
        var serializer = new XmlSerializer(obj.GetType());
        using var stream = new MemoryStream();
        serializer.Serialize(stream, obj);
        stream.Position = 0; // Reset stream position for reading
        var xmlDoc = new XmlDocument();
        xmlDoc.Load(stream);
        return xmlDoc;
    }
}
