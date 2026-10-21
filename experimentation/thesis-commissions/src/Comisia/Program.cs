
using Comisia;
using ScheduleLib;

// var commissions = CommissionParser.ParseCommissions("data/commissions.xlsx");
var theses = ThesisListParser.Parse("data/thesis_distribution.xlsx", ThesisType.Licenta);
// var matchedData = DataMatcher.Match(commissions, theses);
// ReportGenerator.GenerateReport(new()
// {
//     Data = matchedData,
//     OutputFileName = "report-generated.docx",
//     TemplateFileName = "data/report-template.docx",
// });
var excellentStudents = ExcellentStudentTableParser.Parse("data/lucrarile_studentesti.xlsx");

foreach (var student in excellentStudents.Students)
{
    if (FindThesisName() is not { } title)
    {
        Console.WriteLine($"Missing thesis title for student {student.LastName}");
        continue;
    }

    Console.WriteLine($"- {student.LastName} {student.FirstName} — {title}");
    continue;

    string? FindThesisName()
    {
        foreach (var thesis in theses.Items)
        {
            if (!CheckEqual(student.FirstName, thesis.StudentName.FirstName))
            {
                continue;
            }
            if (!CheckEqual(student.LastName, thesis.StudentName.LastName))
            {
                continue;
            }

            var title = thesis.ThesisNameRomanian;
            return title;

            bool CheckEqual(string a, NameParts<string?> b)
            {
                var firstA = new NameParts<string?>
                {
                    A = a,
                    B = null,
                };
                if (!DataMatcher.AreEqual(b, firstA))
                {
                    return false;
                }
                return true;

            }
        }
        return null;
    }
}

