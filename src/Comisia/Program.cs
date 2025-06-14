
using Comisia;

var commissions = CommissionParser.ParseCommissions("data/commissions.xlsx");
var theses = ThesisListParser.Parse("data/thesis_distribution.xlsx", ThesisType.Licenta);
var matchedData = DataMatcher.Match(commissions, theses);
ReportGenerator.GenerateReport(new()
{
    Data = matchedData,
    OutputFileName = "report-generated.docx",
    TemplateFileName = "data/report-template.docx",
});

