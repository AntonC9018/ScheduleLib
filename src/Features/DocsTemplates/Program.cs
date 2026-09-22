using EmploymentDocs;
using Microsoft.Extensions.Configuration;

var fileNames = new FilePaths
{
    AdditionalAgreement = "acord_suplimentar.pdf",
    HireRequest = "cerere_angajare_didactica.pdf",
    IndividualEmploymentContract = "contract_individual_de_munca.pdf",
    ConsentDeclaration = "declaratie_consimtamant.pdf",
    InformationDeclaration = "declaratie_informare.pdf",
    OwnResponsibilityDeclaration = "declaratie_proprie_raspundere.pdf",
    AssistantJobDescription = "fisa_postului_asistent_universitar.pdf",
};

IConfiguration config;
{
    var builder = new ConfigurationBuilder();
    builder.AddUserSecrets<Program>();
    config = builder.Build();
}

var inputExcelPath = args.FirstOrDefault(argument => argument.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
    ?? "data/docs_data.xlsx";
if (!File.Exists(inputExcelPath))
{
    var personal = config.GetSection("Personal")?.Get<PersonalConfig>();
    if (personal is null)
    {
        // default values.
        personal = new()
        {
            IDIssueDate = new DateOnly(year: 2000, month: 1, day: 1),
            PersonalIdentifier = "1234567890123",
            Email = "user@gmail.com",
            HomeAddress = "Moldova, str. X, bl. Y, ap. Z",
            PhoneNumber = "079111111",
            BISeries = "B123123123",
        };
    }
    var defaultPerson = new PersonInfo
    {
        DocumentDate = new DateOnly(year: DateTime.Now.Year, month: 9, day: 1),
        Department = "Informatică",
        Faculty = "Matematică și Informatică",
        Function = "asistent universitar",
        FirstName = "Prenume",
        LastName = "Nume",
        Units = 1.00m,
        HireType = HireType.Titular,
        Email = personal.Email,
        HomeAddress = personal.HomeAddress,
        ID = new()
        {
            IssueDate = personal.IDIssueDate,
            PersonalIdentifier = personal.PersonalIdentifier,
            BISeriesCode = personal.BISeries,
        },
        PhoneNumber = personal.PhoneNumber,
        WorkplaceAddress = "Str. Alexei Mateevici, nr. 60, biroul 225, blocul IV, MD-2009, Chișinău",
        FacultyShort = "Fac. de Mat. și Inf.",
        DepartmentShort = "Dep. Inf.",
        PreparedByName = "Capcelea Titu",
        PreparedByDepartment = "Informatica",
    };

    DocsExcel.GenerateTemplateExcel(inputExcelPath, defaultPerson);
}

var data = DocsExcel.Parse(inputExcelPath);
if (HasDuplicates(data))
{
    throw new InvalidOperationException("Duplicates in table");
}
static bool HasDuplicates(List<PersonInfo> people)
{
    HashSet<(string First, string Last)> hmap = new();
    foreach (var p in people)
    {
        if (!hmap.Add((p.FirstName, p.LastName)))
        {
            return true;
        }
    }
    return false;
}

DocsGenerator.Generate(new()
{
    Data = data.ToArray(),
    OutputDir = args.FirstOrDefault(argument => argument.StartsWith("--output=", StringComparison.Ordinal))?[9..]
        ?? "output",
    TemplateFilePaths = fileNames.Map(x => $"data/pdf-backgrounds/{x}"),
    OutputFilePaths = fileNames,
    OpenOutputDirectory = !args.Contains("--no-open", StringComparer.Ordinal),
});

public sealed class PersonalConfig
{
    public string Email { get; set; } = string.Empty;
    public string HomeAddress { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string PersonalIdentifier { get; set; } = string.Empty;
    public string BISeries { get; set; } = string.Empty;
    public DateOnly IDIssueDate { get; set; }
}
