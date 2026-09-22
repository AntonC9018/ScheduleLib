using EmploymentDocs;

namespace DocsTemplates.Tests;

public sealed class ExcelLockTests
{
    [Fact]
    public void ParseReadsWorkbookOpenInExcel()
    {
        var directory = Directory.CreateTempSubdirectory("docstemplates-excel-test-");
        try
        {
            var path = Path.Combine(directory.FullName, "docs_data.xlsx");
            DocsExcel.GenerateTemplateExcel(path, new PersonInfo
            {
                FirstName = "Prenume",
                LastName = "Nume",
                Function = "asistent universitar",
                Faculty = "Matematică și Informatică",
                Department = "Informatică",
                DocumentDate = new DateOnly(2026, 9, 1),
                Units = 1.00m,
                HireType = HireType.Titular,
                WorkplaceAddress = "Str. Alexei Mateevici, nr. 60, MD-2009, Chișinău",
                HomeAddress = "or. Chișinău, str. X, ap. 1",
                PhoneNumber = "079111111",
                Email = "user@gmail.com",
                ID = new IDInfo
                {
                    BISeriesCode = "B123",
                    IssueDate = new DateOnly(2020, 1, 1),
                    PersonalIdentifier = "1234567890123",
                },
                PreparedByName = "Capcelea Titu",
                PreparedByDepartment = "Informatica",
            });

            // Excel keeps the workbook open with a read share; parsing must
            // not demand exclusive access.
            using var excelLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
            var people = DocsExcel.Parse(path);

            var person = Assert.Single(people);
            Assert.Equal("Nume", person.LastName);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
