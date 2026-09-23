using System.IO.Compression;
using EmploymentDocs;

namespace DocsTemplates.Tests;

public sealed class ConcursRoutingTests
{
    private static string WriteTemplate(string directory, string fileName, string body)
    {
        var templatePath = Path.Combine(directory, fileName);
        const string prefix = """
            <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
              <w:body>
                <w:p><w:r><w:t>
            """;
        const string suffix = """
                </w:t></w:r></w:p>
                <w:sectPr><w:pgSz w:w="11906" w:h="16838"/></w:sectPr>
              </w:body>
            </w:document>
            """;
        using (var archive = ZipFile.Open(templatePath, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open()))
        {
            writer.Write(prefix + System.Security.SecurityElement.Escape(body) + suffix);
        }
        return templatePath;
    }

    private static PersonInfo Person(string first, string last, string function, decimal units, HireType hireType, bool concurs) => new()
    {
        FirstName = first,
        LastName = last,
        Function = function,
        Faculty = "Faculty",
        Department = "Department",
        DocumentDate = new DateOnly(2026, 9, 1),
        Units = units,
        HireType = hireType,
        Concurs = concurs,
        WorkplaceAddress = "Address",
        HomeAddress = "Address",
        PhoneNumber = "000000000",
        Email = "test@example.invalid",
        ID = new IDInfo { BISeriesCode = "B000000", IssueDate = new DateOnly(2020, 1, 1), PersonalIdentifier = "0000000000000" },
        PreparedByName = "Preparer",
        PreparedByDepartment = "Department",
    };

    [Fact]
    public void ConcursSuppressesBaseContractAndRoutesExtraDocuments()
    {
        var directory = Directory.CreateTempSubdirectory("docstemplates-concurs-");
        try
        {
            var hireTpl = WriteTemplate(directory.FullName, "hire-tpl.docx", "{{Name}} | {{Units}}");
            var cimTpl = WriteTemplate(directory.FullName, "cim-tpl.docx", "{{Name}} | {{Units}}");
            var concursTpl = WriteTemplate(directory.FullName, "concurs-tpl.docx", "{{Name}} | {{Solicit}} | {{Units}}");
            var acordTpl = WriteTemplate(directory.FullName, "acord-tpl.docx", "{{Name}} | {{Units}}");
            var commonTpl = WriteTemplate(directory.FullName, "common-tpl.docx", "{{Name}}");

            var fileNames = new FilePaths
            {
                AdditionalAgreement = "agreement.docx",
                HireRequest = "hire.docx",
                ConcursRequest = "concurs.docx",
                IndividualEmploymentContract = "cim.docx",
                ConsentDeclaration = "consent.docx",
                InformationDeclaration = "information.docx",
                OwnResponsibilityDeclaration = "responsibility.docx",
                AssistantJobDescription = "job.docx",
            };
            var templates = new FilePaths
            {
                AdditionalAgreement = acordTpl,
                HireRequest = hireTpl,
                ConcursRequest = concursTpl,
                IndividualEmploymentContract = cimTpl,
                ConsentDeclaration = commonTpl,
                InformationDeclaration = commonTpl,
                OwnResponsibilityDeclaration = commonTpl,
                AssistantJobDescription = commonTpl,
            };

            var outputPath = Path.Combine(directory.FullName, "output");
            DocsGenerator.Generate(new GenerateReportParams
            {
                Data =
                [
                    Person("Ion", "ConcursUnu", "lector universitar", 1.0m, HireType.Titular, concurs: true),
                    Person("Ana", "ConcursSplit", "conferențiar universitar", 1.5m, HireType.CumulIntern, concurs: true),
                    Person("Vasile", "Obisnuit", "asistent universitar", 1.0m, HireType.Titular, concurs: false),
                ],
                OutputDir = outputPath,
                OutputFilePaths = fileNames,
                TemplateFilePaths = templates,
                OpenOutputDirectory = false,
            });

            // Concurs with exactly 1.0: no CIM, plus concurs request and agreement.
            var unu = Path.Combine(outputPath, "ConcursUnu_Ion", "docs");
            Assert.True(Directory.Exists(unu));
            Assert.False(Directory.Exists(Path.Combine(outputPath, "ConcursUnu_Ion", "print")));
            Assert.False(Directory.Exists(Path.Combine(outputPath, "ConcursUnu_Ion", "not-print")));
            Assert.True(File.Exists(Path.Combine(unu, "hire.docx")));
            Assert.False(File.Exists(Path.Combine(unu, "cim.docx")));
            Assert.True(File.Exists(Path.Combine(unu, "concurs.docx")));
            Assert.True(File.Exists(Path.Combine(unu, "agreement.docx")));

            // Concurs above 1.0: no CIM for the 1.0 part, CIM only for the remainder.
            var split = Path.Combine(outputPath, "ConcursSplit_Ana", "docs");
            Assert.True(File.Exists(Path.Combine(split, "hire1.docx")));
            Assert.True(File.Exists(Path.Combine(split, "hire2.docx")));
            Assert.False(File.Exists(Path.Combine(split, "cim1.docx")));
            Assert.True(File.Exists(Path.Combine(split, "cim2.docx")));
            Assert.True(File.Exists(Path.Combine(split, "concurs.docx")));
            Assert.True(File.Exists(Path.Combine(split, "agreement.docx")));

            // Non-concurs: unchanged set, no concurs request and no agreement.
            var plain = Path.Combine(outputPath, "Obisnuit_Vasile", "docs");
            Assert.True(File.Exists(Path.Combine(plain, "hire.docx")));
            Assert.True(File.Exists(Path.Combine(plain, "cim.docx")));
            Assert.True(File.Exists(Path.Combine(plain, "job.docx")));
            Assert.False(File.Exists(Path.Combine(plain, "concurs.docx")));
            Assert.False(File.Exists(Path.Combine(plain, "agreement.docx")));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
