using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;
using ScheduleLib.Helper;

namespace EmploymentDocs;

public sealed class PersonInfo
{
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required string Function { get; init; }
    public required string Faculty { get; init; }
    public required string Department { get; init; }
    public string FacultyShort { get; init; } = string.Empty;
    public string DepartmentShort { get; init; } = string.Empty;
    public required DateOnly DocumentDate { get; init; }
    public required decimal Units { get; init; }
    public required HireType HireType { get; init; }
    public required string WorkplaceAddress { get; init; }
    public string PrimaryFunction { get; init; } = string.Empty;
    public string PrimaryEmployer { get; init; } = string.Empty;
    public required string HomeAddress { get; init; }
    public required string PhoneNumber { get; init; }
    public required string Email { get; init; }
    public required IDInfo ID { get; init; }
    public required string PreparedByName { get; init; }
    public required string PreparedByDepartment { get; init; }

    public string Name => $"{LastName} {FirstName}";
}

public sealed class IDInfo
{
    public required string BISeriesCode { get; init; }
    public required DateOnly IssueDate { get; init; }
    public required string PersonalIdentifier { get; init; }
}

public enum HireType
{
    CumulIntern,
    CumulExtern,
    Contract,
    Titular,
}

public static class AcademicFunctions
{
    public static readonly string[] Allowed =
    [
        "asistent universitar",
        "lector universitar",
        "conferențiar universitar",
        "profesor universitar",
    ];

    public static string Normalize(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        if (!Allowed.Contains(normalized, StringComparer.Ordinal))
        {
            throw new FormatException(
                $"Funcția '{value}' nu este acceptată. Valori permise: {string.Join(", ", Allowed)}.");
        }

        return normalized;
    }
}

public sealed class FilePaths
{
    public required string AdditionalAgreement { get; init; }
    public required string HireRequest { get; init; }
    public required string IndividualEmploymentContract { get; init; }
    public required string ConsentDeclaration { get; init; }
    public required string InformationDeclaration { get; init; }
    public required string OwnResponsibilityDeclaration { get; init; }
    public required string AssistantJobDescription { get; init; }

    public FilePaths Map(Func<string, string> map) => new()
    {
        AdditionalAgreement = map(AdditionalAgreement),
        HireRequest = map(HireRequest),
        IndividualEmploymentContract = map(IndividualEmploymentContract),
        ConsentDeclaration = map(ConsentDeclaration),
        InformationDeclaration = map(InformationDeclaration),
        OwnResponsibilityDeclaration = map(OwnResponsibilityDeclaration),
        AssistantJobDescription = map(AssistantJobDescription),
    };
}

public sealed class GenerateReportParams
{
    public required PersonInfo[] Data { get; init; }
    public required string OutputDir { get; init; }
    public required FilePaths TemplateFilePaths { get; init; }
    public required FilePaths OutputFilePaths { get; init; }
    public bool OpenOutputDirectory { get; init; } = true;
}

public static class DocsGenerator
{
    private const decimal MaxUnitsPerDocument = 1.00m;
    private const decimal MaxTotalUnits = 2.00m;
    private const int PrintCopiesPerContract = 2;
    private const int PrintCopiesPerJobDescription = 2;
    private const string PrintBundleFileName = "print.pdf";

    public static void Generate(GenerateReportParams parameters)
    {
        foreach (var person in parameters.Data)
        {
            Validate(person);
            GeneratePersonPacket(person, parameters);
        }

        if (parameters.Data.Length > 0 && parameters.OpenOutputDirectory)
        {
            ExplorerHelper.TryOpenExplorerAndSelectFile(Path.GetFullPath(parameters.OutputDir));
        }
    }

    private static void GeneratePersonPacket(PersonInfo person, GenerateReportParams parameters)
    {
        var personDirectory = Path.Combine(parameters.OutputDir, SafePathPart($"{person.LastName}_{person.FirstName}"));
        if (Directory.Exists(personDirectory))
        {
            Directory.Delete(personDirectory, recursive: true);
        }

        var printDirectory = Path.Combine(personDirectory, "print");
        var notPrintDirectory = Path.Combine(personDirectory, "not-print");
        Directory.CreateDirectory(printDirectory);
        Directory.CreateDirectory(notPrintDirectory);

        var common = CommonFields(person);
        common["Units"] = FormatUnits(person.Units);
        GenerateDoc(common, parameters.TemplateFilePaths.AdditionalAgreement,
            Path.Combine(notPrintDirectory, parameters.OutputFilePaths.AdditionalAgreement));

        // Documents merged into print.pdf, with their print copy counts.
        // The employment contract prints twice per split part and the
        // assistant job description twice; everything else prints once.
        var printDocuments = new List<(string Path, int Copies)>();

        string PrintPath(string fileName) => Path.Combine(printDirectory, fileName);

        var repeatedDocuments = SplitUnits(person).ToArray();
        for (var index = 0; index < repeatedDocuments.Length; index++)
        {
            var part = repeatedDocuments[index];
            var fields = RepeatingFields(person, part.HireType, part.Units);
            var hirePath = RepeatedPath(printDirectory, parameters.OutputFilePaths.HireRequest, index, repeatedDocuments.Length);
            GenerateDoc(fields, parameters.TemplateFilePaths.HireRequest, hirePath);
            var contractPath = RepeatedPath(printDirectory, parameters.OutputFilePaths.IndividualEmploymentContract, index, repeatedDocuments.Length);
            GenerateDoc(fields, parameters.TemplateFilePaths.IndividualEmploymentContract, contractPath);
            printDocuments.Add((hirePath, 1));
            printDocuments.Add((contractPath, PrintCopiesPerContract));
        }

        var consentPath = PrintPath(parameters.OutputFilePaths.ConsentDeclaration);
        GenerateDoc(common, parameters.TemplateFilePaths.ConsentDeclaration, consentPath);
        printDocuments.Add((consentPath, 1));

        var informationPath = PrintPath(parameters.OutputFilePaths.InformationDeclaration);
        GenerateDoc(common, parameters.TemplateFilePaths.InformationDeclaration, informationPath);
        printDocuments.Add((informationPath, 1));

        var responsibilityPath = PrintPath(parameters.OutputFilePaths.OwnResponsibilityDeclaration);
        GenerateDoc(common, parameters.TemplateFilePaths.OwnResponsibilityDeclaration, responsibilityPath);
        printDocuments.Add((responsibilityPath, 1));

        if (AcademicFunctions.Normalize(person.Function) == "asistent universitar")
        {
            var jobPath = PrintPath(parameters.OutputFilePaths.AssistantJobDescription);
            GenerateDoc(common, parameters.TemplateFilePaths.AssistantJobDescription, jobPath);
            printDocuments.Add((jobPath, PrintCopiesPerJobDescription));
        }

        MergePrintDocuments(personDirectory, printDocuments);
    }

    /// <summary>
    /// Merges every generated PDF print document into a single
    /// <c>print.pdf</c> bundle at the person-folder level, repeating each
    /// document by its print copy count. Non-PDF outputs (the legacy DOCX
    /// path) cannot be merged and are skipped.
    /// </summary>
    private static void MergePrintDocuments(
        string personDirectory,
        List<(string Path, int Copies)> printDocuments)
    {
        var pdfDocuments = printDocuments
            .Where(document => document.Path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (pdfDocuments.Count == 0)
        {
            return;
        }

        using var bundle = new PdfDocument();
        foreach (var (path, copies) in pdfDocuments)
        {
            using var source = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            for (var copy = 0; copy < copies; copy++)
            {
                foreach (var page in source.Pages)
                {
                    bundle.AddPage(page);
                }
            }
        }

        bundle.Save(Path.Combine(personDirectory, PrintBundleFileName));
    }

    private static Dictionary<string, string> CommonFields(PersonInfo person) => new(StringComparer.Ordinal)
    {
        ["Name"] = person.Name,
        ["Function"] = person.Function,
        ["Faculty"] = person.Faculty,
        ["Department"] = person.Department,
        ["DocumentDate"] = FormatDate(person.DocumentDate),
        ["DocumentDayMonth"] = person.DocumentDate.ToString("dd.MM", CultureInfo.InvariantCulture),
        ["DocumentYear2"] = person.DocumentDate.ToString("yy", CultureInfo.InvariantCulture),
        ["HomeAddress"] = person.HomeAddress,
        // Continuation line for long addresses in templates whose address area
        // spans two rules (acord). The renderer flows the overflow here via
        // the manifest's flowTo link; empty by default so short addresses
        // leave the second rule blank.
        ["HomeAddressCont"] = string.Empty,
        ["PhoneNumber"] = person.PhoneNumber,
        ["Email"] = person.Email,
        ["BISeries"] = person.ID.BISeriesCode,
        ["IDIssueDate"] = FormatDate(person.ID.IssueDate),
        ["PersonalIdentifier"] = person.ID.PersonalIdentifier,
        ["PreparedByName"] = person.PreparedByName,
        ["PreparedByDepartment"] = person.PreparedByDepartment,
    };

    private static Dictionary<string, string> RepeatingFields(PersonInfo person, HireType hireType, decimal units)
    {
        var fields = CommonFields(person);
        var period = EmploymentPeriodPolicy.For(person.DocumentDate, hireType);
        fields["AcademicYear"] = $"{person.DocumentDate.Year}–{person.DocumentDate.Year + 1}";
        fields["EmploymentBasis"] = $"pe perioada anului de studii {person.DocumentDate.Year}–{person.DocumentDate.Year + 1}";
        fields["Units"] = FormatUnits(units);
        fields["UnitsText"] = units == 1m ? "1.00 unitate" : $"{FormatUnits(units)} unități";
        fields["HireType"] = HireTypeForRequest(hireType);
        fields["CimHireType"] = HireTypeForCim(hireType);
        fields["DateFrom"] = FormatDate(period.Start);
        fields["DateTo"] = FormatDate(period.End);
        fields["ContractPeriod"] = $"{FormatDate(period.Start)} până la {FormatDate(period.End)}";
        fields["Workplace"] = Workplace(person);
        fields["WorkplaceAddress"] = person.WorkplaceAddress;
        fields["PrimaryFunction"] = hireType == HireType.CumulExtern ? person.PrimaryFunction : string.Empty;
        fields["PrimaryEmployer"] = hireType == HireType.CumulExtern ? person.PrimaryEmployer : string.Empty;
        // The overlay extends into the empty space after the short street blank
        // and covers its original full stop, so retain that punctuation here.
        fields["WorkplaceAddressCompact"] = CompactWorkplaceAddress(person.WorkplaceAddress).TrimEnd('.') + ".";
        fields["ExternalEmploymentClause"] = hireType switch
        {
            HireType.CumulExtern =>
                $"În cazul cumulului extern, Salariatul declară că deține funcția de bază de {person.PrimaryFunction} la {person.PrimaryEmployer} și se obligă să informeze Angajatorul, în scris, despre orice schimbare a acestei situații.",
            HireType.CumulIntern =>
                $"În cazul cumulului intern, Salariatul deține funcția de bază de {person.Function} la Universitatea de Stat din Moldova.",
            _ => string.Empty,
        };
        return fields;
    }

    private static IEnumerable<(decimal Units, HireType HireType)> SplitUnits(PersonInfo person)
    {
        if (person.Units <= MaxUnitsPerDocument)
        {
            yield return (person.Units, person.HireType);
            yield break;
        }

        yield return (MaxUnitsPerDocument, person.HireType);
        yield return (person.Units - MaxUnitsPerDocument, HireType.CumulIntern);
    }

    private static void Validate(PersonInfo person)
    {
        _ = AcademicFunctions.Normalize(person.Function);
        if (person.Units <= 0 || person.Units > MaxTotalUnits)
        {
            throw new InvalidOperationException(
                $"Norma didactică pentru {person.Name} trebuie să fie mai mare decât 0 și cel mult {MaxTotalUnits:0.00}.");
        }
    }

    private static string Workplace(PersonInfo person)
    {
        var faculty = string.IsNullOrWhiteSpace(person.FacultyShort) ? person.Faculty : person.FacultyShort;
        var department = string.IsNullOrWhiteSpace(person.DepartmentShort) ? person.Department : person.DepartmentShort;
        return $"{faculty}, {department}";
    }

    private static string CompactWorkplaceAddress(string address)
    {
        var compact = address.Trim();
        if (compact.StartsWith("Str. ", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[5..];
        }
        if (compact.EndsWith(", Chișinău", StringComparison.OrdinalIgnoreCase))
        {
            compact = compact[..^10];
        }
        return compact;
    }

    private static string HireTypeForRequest(HireType hireType) => hireType switch
    {
        HireType.CumulIntern => "cumul intern",
        HireType.CumulExtern => "cumul extern",
        HireType.Contract or HireType.Titular => "angajare de bază",
        _ => throw new ArgumentOutOfRangeException(nameof(hireType)),
    };

    private static string HireTypeForCim(HireType hireType) => hireType switch
    {
        HireType.CumulIntern => "prin cumul intern",
        HireType.CumulExtern => "prin cumul extern",
        HireType.Contract or HireType.Titular => "de bază",
        _ => throw new ArgumentOutOfRangeException(nameof(hireType)),
    };

    private static string RepeatedPath(string directory, string fileName, int index, int count)
    {
        if (count == 1)
        {
            return Path.Combine(directory, fileName);
        }

        return Path.Combine(directory,
            $"{Path.GetFileNameWithoutExtension(fileName)}{index + 1}{Path.GetExtension(fileName)}");
    }

    private static string SafePathPart(string value)
    {
        foreach (var invalidCharacter in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(invalidCharacter, '_');
        }
        return value;
    }

    private static string FormatDate(DateOnly date) => date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
    private static string FormatUnits(decimal units) => units.ToString("0.00", CultureInfo.InvariantCulture);

    private static void GenerateDoc(
        IReadOnlyDictionary<string, string> fields,
        string templatePath,
        string outputPath)
    {
        if (templatePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            PdfTemplateRenderer.Render(templatePath, outputPath, fields);
            return;
        }

        DocxTemplateRenderer.Render(templatePath, outputPath, fields);
    }
}

internal readonly record struct EmploymentPeriod(DateOnly Start, DateOnly End);

internal static class EmploymentPeriodPolicy
{
    public static EmploymentPeriod For(DateOnly documentDate, HireType hireType)
    {
        var start = new DateOnly(documentDate.Year, 9, 1);
        var end = hireType is HireType.CumulIntern or HireType.CumulExtern
            ? new DateOnly(documentDate.Year + 1, 7, 4)
            : new DateOnly(documentDate.Year + 1, 8, 31);
        return new(start, end);
    }
}

internal static class DocxTemplateRenderer
{
    public static void Render(
        string templatePath,
        string outputPath,
        IReadOnlyDictionary<string, string> fields)
    {
        var replacements = fields.ToDictionary(
            pair => "{{" + pair.Key + "}}",
            pair => SecurityElement.Escape(pair.Value) ?? string.Empty,
            StringComparer.Ordinal);

        var outputDirectory = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }

        using var sourceFile = File.OpenRead(templatePath);
        using var source = new ZipArchive(sourceFile, ZipArchiveMode.Read);
        using var outputFile = File.Create(outputPath);
        using var target = new ZipArchive(outputFile, ZipArchiveMode.Create);

        foreach (var sourceEntry in source.Entries)
        {
            var targetEntry = target.CreateEntry(sourceEntry.FullName, CompressionLevel.Optimal);
            targetEntry.LastWriteTime = sourceEntry.LastWriteTime;
            using var input = sourceEntry.Open();
            using var output = targetEntry.Open();

            if (!sourceEntry.FullName.StartsWith("word/", StringComparison.Ordinal) ||
                !sourceEntry.FullName.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                input.CopyTo(output);
                continue;
            }

            string xml;
            using (var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            {
                xml = reader.ReadToEnd();
            }
            foreach (var replacement in replacements)
            {
                xml = xml.Replace(replacement.Key, replacement.Value, StringComparison.Ordinal);
            }

            var unresolvedStart = xml.IndexOf("{{", StringComparison.Ordinal);
            if (unresolvedStart >= 0)
            {
                var unresolvedEnd = xml.IndexOf("}}", unresolvedStart, StringComparison.Ordinal);
                var token = unresolvedEnd >= 0
                    ? xml[unresolvedStart..(unresolvedEnd + 2)]
                    : xml[unresolvedStart..Math.Min(xml.Length, unresolvedStart + 80)];
                throw new InvalidOperationException($"Câmp de șablon fără valoare în {templatePath}: {token}");
            }

            using var writer = new StreamWriter(output, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(xml);
        }
    }

}
