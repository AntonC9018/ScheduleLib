
using Comisia;

var commissions = CommissionParser.ParseCommissions(@"C:\Users\Anton\Downloads\commissions.xlsx");
var theses = ThesisListParser.Parse(@"C:\Users\Anton\Downloads\thesis_distribution.xlsx", ThesisType.Licenta);
var matchedData = DataMatcher.Match(commissions, theses);
ReportGenerator.GenerateReport(new()
{
    Data = matchedData,
    OutputFileName = "report-generated.docx",
    TemplateFileName = "data/report-template.docx",
});

