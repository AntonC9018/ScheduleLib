using System.IO.Compression;
using System.Xml.Linq;
using EmploymentDocs;

namespace DocsTemplates.Tests;

public sealed class OverlayTests
{
    [Theory]
    [InlineData(HireType.Titular, "31.08.2027")]
    [InlineData(HireType.Contract, "31.08.2027")]
    [InlineData(HireType.CumulIntern, "04.07.2027")]
    [InlineData(HireType.CumulExtern, "04.07.2027")]
    public void GeneratedOverlaysPreserveSourceAndEmploymentPeriod(HireType hireType, string expectedEnd)
    {
        var directory = Directory.CreateTempSubdirectory("docstemplates-test-");
        try
        {
            var templatePath = Path.Combine(directory.FullName, "template.docx");
            const string sourceXml = """
                <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"
                            xmlns:v="urn:schemas-microsoft-com:vml">
                  <w:body>
                    <w:p><w:r><w:t>Source wording ___</w:t><w:br/><w:t>Second line ___</w:t></w:r>
                      <w:r><w:pict><v:shape style="position:absolute" fillcolor="white" stroked="f">
                        <v:textbox><w:txbxContent><w:p><w:r><w:t>{{ExternalEmploymentClause}}</w:t></w:r></w:p></w:txbxContent></v:textbox>
                      </v:shape></w:pict></w:r>
                    </w:p>
                    <w:p><w:r><w:pict><v:shape style="position:absolute" fillcolor="white" stroked="f">
                      <v:textbox><w:txbxContent><w:p><w:r><w:t>{{Name}} | {{DocumentDate}} | {{DateFrom}} | {{DateTo}} | {{Units}} | {{WorkplaceAddressCompact}}</w:t></w:r></w:p></w:txbxContent></v:textbox>
                    </v:shape></w:pict></w:r></w:p>
                    <w:sectPr><w:pgSz w:w="11906" w:h="16838"/></w:sectPr>
                  </w:body>
                </w:document>
                """;
            using (var archive = ZipFile.Open(templatePath, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open()))
            {
                writer.Write(sourceXml);
            }

            // Non-repeating documents do not receive employment-period fields.
            var commonTemplatePath = Path.Combine(directory.FullName, "common.docx");
            using (var archive = ZipFile.Open(commonTemplatePath, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open()))
            {
                writer.Write(sourceXml.Replace("{{ExternalEmploymentClause}}", "{{Name}}")
                    .Replace("{{DateFrom}} | {{DateTo}} | {{Units}} | {{WorkplaceAddressCompact}}", "{{DocumentDate}}"));
            }

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
            var person = new PersonInfo
            {
                FirstName = "Test",
                LastName = "Example & Example",
                Function = "asistent universitar",
                Faculty = "Faculty",
                Department = "Department",
                DocumentDate = new DateOnly(2026, 10, 15),
                Units = 1.25m,
                HireType = hireType,
                WorkplaceAddress = "Str. Example, nr. 60, Chișinău",
                PrimaryFunction = "lector universitar",
                PrimaryEmployer = "Example employer",
                HomeAddress = "Address",
                PhoneNumber = "000000000",
                Email = "test@example.invalid",
                ID = new IDInfo { BISeriesCode = "B000000", IssueDate = new DateOnly(2020, 1, 1), PersonalIdentifier = "0000000000000" },
                PreparedByName = "Preparer",
                PreparedByDepartment = "Department",
            };
            var outputPath = Path.Combine(directory.FullName, "output");
            DocsGenerator.Generate(new GenerateReportParams
            {
                Data = [person],
                OutputDir = outputPath,
                OutputFilePaths = fileNames,
                TemplateFilePaths = fileNames.Map(name => name is "hire.docx" or "cim.docx" ? templatePath : commonTemplatePath),
                OpenOutputDirectory = false,
            });

            XNamespace word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
            var original = XDocument.Parse(sourceXml);
            var docsPath = Path.Combine(outputPath, "Example & Example_Test", "docs");
            foreach (var fileName in new[] { "hire1.docx", "cim1.docx", "hire2.docx", "cim2.docx" })
            {
                using var archive = ZipFile.OpenRead(Path.Combine(docsPath, fileName));
                using var stream = archive.GetEntry("word/document.xml")!.Open();
                var generated = XDocument.Load(stream);
                Assert.Equal(2, generated.Descendants(word + "txbxContent").Count());
                var text = string.Concat(generated.Descendants(word + "t").Select(element => element.Value));
                Assert.DoesNotContain("{{", text);
                Assert.Contains("Example & Example Test | 15.10.2026 | 01.09.2026 |", text);
                Assert.Contains(fileName.Contains('2') ? "04.07.2027 | 0.25" : $"{expectedEnd} | 1", text);
                Assert.EndsWith(" | Example, nr. 60.", text);

                foreach (var box in generated.Descendants(word + "txbxContent"))
                {
                    box.RemoveNodes();
                }
                var expected = new XDocument(original);
                foreach (var box in expected.Descendants(word + "txbxContent"))
                {
                    box.RemoveNodes();
                }
                Assert.True(XNode.DeepEquals(expected, generated), "Only text-box contents may change.");
            }
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
