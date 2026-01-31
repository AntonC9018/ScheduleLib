using EmploymentDocs;
using Microsoft.Extensions.Configuration;

var fileNames = new FilePaths
{
    Declaration = "declaratie_proprie_raspundere.docx",
    Hire = "angajare_didactica.docx",
    DeclarationConsimtand = "declaratie_consimtand.docx",
    CIM = "cim.docx",
};

IConfiguration config;
{
    var builder = new ConfigurationBuilder();
    builder.AddUserSecrets<Program>();
    config = builder.Build();
}

const string inputExcelPath = "data/docs_data.xlsx";
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
        Date = new DateOnly(year: DateTime.Now.Year, month: 9, day: 1),
        Department = "Informatică",
        Faculty = "Matematică și Informatică",
        Function = "asistent universitar",
        FirstName = "Anton",
        LastName = "Curmanschii",
        Units = 1.1f,
        HireType = HireType.Titular,
        Email = personal.Email,
        HomeAddress = personal.HomeAddress,
        ID = new()
        {
            IssueDate = personal.IDIssueDate,
            PersonalIdentifier = personal.PersonalIdentifier,
            BSeriesCode = personal.BISeries,
        },
        PhoneNumber = personal.PhoneNumber,
        WorkingMode = WorkingMode.Birou,
        WorkingPlace = "Universitatea de Stat din Moldova",
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
    OutputDir = "output",
    TemplateFilePaths = fileNames.Map(x => $"data/templates/{x}"),
    OutputFilePaths = fileNames,
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
