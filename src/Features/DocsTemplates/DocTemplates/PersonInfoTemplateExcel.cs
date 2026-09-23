using ClosedXML.Excel;

namespace EmploymentDocs;

public static partial class DocsExcel
{
    private const int HeaderRow = 1;
    private const int DefaultRow = 2;

    public static void GenerateTemplateExcel(string path, PersonInfo defaultPerson)
    {
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Persons");

        for (var index = 0; index < (int) PersonColumns.Count; index++)
        {
            var cell = worksheet.Cell(HeaderRow, index + 1);
            cell.Value = ColumnLabels.Labels[index][0];
            cell.Style.Font.Bold = true;
            cell.Style.Fill.BackgroundColor = XLColor.LightGray;
        }

        Set(PersonColumns.FirstName, defaultPerson.FirstName);
        Set(PersonColumns.LastName, defaultPerson.LastName);
        Set(PersonColumns.Function, defaultPerson.Function);
        Set(PersonColumns.Faculty, defaultPerson.Faculty);
        Set(PersonColumns.Department, defaultPerson.Department);
        Set(PersonColumns.DocumentDate, defaultPerson.DocumentDate);
        Set(PersonColumns.Units, defaultPerson.Units);
        Set(PersonColumns.HireType, defaultPerson.HireType.ToString());
        Set(PersonColumns.HomeAddress, defaultPerson.HomeAddress);
        Set(PersonColumns.PhoneNumber, defaultPerson.PhoneNumber);
        Set(PersonColumns.Email, defaultPerson.Email);
        Set(PersonColumns.BISeries, defaultPerson.ID.BISeriesCode);
        Set(PersonColumns.IDIssueDate, defaultPerson.ID.IssueDate);
        Set(PersonColumns.PersonalIdentifier, defaultPerson.ID.PersonalIdentifier);
        Set(PersonColumns.PrimaryFunction, defaultPerson.PrimaryFunction);
        Set(PersonColumns.PrimaryEmployer, defaultPerson.PrimaryEmployer);
        Set(PersonColumns.WorkplaceAddress, defaultPerson.WorkplaceAddress);
        Set(PersonColumns.FacultyShort, defaultPerson.FacultyShort);
        Set(PersonColumns.DepartmentShort, defaultPerson.DepartmentShort);
        Set(PersonColumns.PreparedByName, defaultPerson.PreparedByName);
        Set(PersonColumns.PreparedByDepartment, defaultPerson.PreparedByDepartment);
        Set(PersonColumns.Concurs, defaultPerson.Concurs);

        AddAllowedValues(PersonColumns.Function, AcademicFunctions.Allowed);
        AddAllowedValues(PersonColumns.HireType, Enum.GetNames<HireType>());
        AddAllowedValues(PersonColumns.Concurs, ["TRUE", "FALSE"]);
        worksheet.SheetView.FreezeRows(1);
        worksheet.Columns().AdjustToContents();

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        workbook.SaveAs(path);

        void Set(PersonColumns column, object value)
        {
            var cell = worksheet.Cell(DefaultRow, (int) column + 1);
            switch (value)
            {
                case DateOnly date:                    cell.Value = date.ToDateTime(TimeOnly.MinValue);
                    worksheet.Column((int) column + 1).Style.NumberFormat.Format = "dd.mm.yyyy";
                    break;
                case decimal number:
                    cell.Value = number;
                    worksheet.Column((int) column + 1).Style.NumberFormat.Format = "0.00";
                    break;
                case bool flag:
                    cell.Value = flag;
                    break;
                default:
                    cell.Value = value?.ToString() ?? string.Empty;
                    worksheet.Column((int) column + 1).Style.NumberFormat.Format = "@";
                    break;
            }
        }

        void AddAllowedValues(PersonColumns column, IReadOnlyCollection<string> values)
        {
            var header = worksheet.Cell(HeaderRow, (int) column + 1);
            header.CreateComment().AddText("Valori permise: " + string.Join(", ", values));
            var validation = worksheet.Range(DefaultRow, (int) column + 1, 1000, (int) column + 1)
                .CreateDataValidation();
            validation.IgnoreBlanks = false;
            validation.InCellDropdown = true;
            validation.List('"' + string.Join(',', values) + '"');
        }
    }
}
