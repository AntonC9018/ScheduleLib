using ClosedXML.Excel;

namespace EmploymentDocs;

// ChatGPT code
public static partial class DocsExcel
{
    private const int HeaderRow = 1;
    private const int DefaultRow = 2;
    private const int MaxRows = 1048576;

    public static void GenerateTemplateExcel(string path, PersonInfo defaultPerson)
    {
        if (defaultPerson == null)
        {
            throw new ArgumentNullException(nameof(defaultPerson));
        }

        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Persons");

        // --- 1. Write headers ---
        for (int i = 0; i < (int) PersonColumns.Count; i++)
        {
            ws.Cell(HeaderRow, i + 1).Value = ColumnLabels.Labels[i][0];
        }

        // --- 2. Local helper to set default values ---
        void SetDefaultValue<T>(PersonColumns col, T value)
        {
            int colIndex = (int) col + 1;
            var cell = ws.Cell(DefaultRow, colIndex);
            cell.Value = value switch
            {
                DateOnly d when d == DateOnly.MinValue => "",
                DateOnly d => d.ToDateTime(TimeOnly.MinValue),
                float f => (double) f,
                double db => db,
                int i => i,
                string s => s,
                bool b => b,
                _ => value?.ToString() ?? "",
            };

            // Don't know how to do this properly.
            var columnRange = ws.Column(colIndex);
            if (value is DateOnly || value is DateTime)
            {
                // Date format
                columnRange.Style.NumberFormat.Format = "yyyy-MM-dd";  // or whatever date format you want
            }
            else if (value is float || value is double || value is int)
            {
                // Numeric format, possibly decimals
                columnRange.Style.NumberFormat.Format = "0.00";  // two decimal places
            }
            else
            {
                // Force Text format
                columnRange.Style.NumberFormat.Format = "@";  // "@" = text in Excel/ClosedXML
            }
        }

        // --- 3. Populate default row from parameter object ---
        SetDefaultValue(PersonColumns.FirstName, defaultPerson.FirstName);
        SetDefaultValue(PersonColumns.LastName, defaultPerson.LastName);
        SetDefaultValue(PersonColumns.Function, defaultPerson.Function);
        SetDefaultValue(PersonColumns.Faculty, defaultPerson.Faculty);
        SetDefaultValue(PersonColumns.Department, defaultPerson.Department);
        SetDefaultValue(PersonColumns.Date, defaultPerson.Date);
        SetDefaultValue(PersonColumns.Units, defaultPerson.Units);
        SetDefaultValue(PersonColumns.HireType, defaultPerson.HireType.ToString());
        SetDefaultValue(PersonColumns.WorkingPlace, defaultPerson.WorkingPlace);
        SetDefaultValue(PersonColumns.WorkingMode, defaultPerson.WorkingMode.ToString());
        SetDefaultValue(PersonColumns.ContractEndDate, defaultPerson.ContractEndDate);
        SetDefaultValue(PersonColumns.ProbationPeriodEndDate, defaultPerson.ProbationPeriodEndDate);
        SetDefaultValue(PersonColumns.HomeAddress, defaultPerson.HomeAddress);
        SetDefaultValue(PersonColumns.PhoneNumber, defaultPerson.PhoneNumber);
        SetDefaultValue(PersonColumns.Email, defaultPerson.Email);
        SetDefaultValue(PersonColumns.BISeries, defaultPerson.ID.BSeriesCode);
        SetDefaultValue(PersonColumns.IDIssueDate, defaultPerson.ID.IssueDate);
        SetDefaultValue(PersonColumns.PersonalIdentifier, defaultPerson.ID.PersonalIdentifier);

#pragma warning disable CS8321 // Local function is declared but never used
        void AddDropdownValidation(PersonColumns col, string[] allowedValues)
#pragma warning restore CS8321 // Local function is declared but never used
        {
            int colIndex = (int) col + 1;
            var comment = ws.Cell(HeaderRow, colIndex).CreateComment();
            var list = string.Join(",", allowedValues);
            comment.AddText(list);
            comment.SetVisible(false);

            // data validation doesn't work
            #if false
            var range = ws.Range(DefaultRow, colIndex, 30, colIndex);

#pragma warning disable CS0618 // Type or member is obsolete
            var validation = range.SetDataValidation();
#pragma warning restore CS0618 // Type or member is obsolete
            validation.IgnoreBlanks = true;
            validation.InCellDropdown = true;


            if (list.Length < 255)
            {
                // Inline list, must be quoted
                validation.List(list, inCellDropdown: true);
            }
            else
            {
                // Too long: put in helper sheet
                var helper = ws.Workbook.Worksheets.FirstOrDefault(s => s.Name == "ValidationHelper")
                    ?? ws.Workbook.Worksheets.Add("ValidationHelper");

                int colIndex1 = helper.LastColumnUsed()?.ColumnNumber() + 1 ?? 1;

                for (int i = 0; i < allowedValues.Length; i++)
                {
                    helper.Cell(i + 1, colIndex1).Value = allowedValues[i];
                }

                var listRange = helper.Range(1, colIndex1, allowedValues.Length, colIndex);
                string namedRange = $"{col}_Values";
                listRange.AddToNamed(namedRange);

                validation.List("=" + namedRange);
                helper.Hide();
            }
            #endif
        }

        AddDropdownValidation(PersonColumns.HireType, Enum.GetNames<HireType>());
        AddDropdownValidation(PersonColumns.WorkingMode, Enum.GetNames<WorkingMode>());

        // --- 5. Adjust column widths ---
        ws.Columns().AdjustToContents();

        wb.SaveAs(path);
    }
}
