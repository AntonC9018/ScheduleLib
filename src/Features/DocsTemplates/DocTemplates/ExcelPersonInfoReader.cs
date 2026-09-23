using System.Globalization;
using ClosedXML.Excel;

namespace EmploymentDocs;

public enum PersonColumns
{
    FirstName, LastName, Function, Faculty, Department, DocumentDate, Units, HireType,
    HomeAddress, PhoneNumber, Email, BISeries, IDIssueDate, PersonalIdentifier,
    PrimaryFunction, PrimaryEmployer, WorkplaceAddress, FacultyShort, DepartmentShort,
    PreparedByName, PreparedByDepartment, Concurs,
    Count,
}

public static class ColumnLabels
{
    public static readonly string[][] Labels = BuildLabels();

    private static string[][] BuildLabels()
    {
        var labels = new string[(int) PersonColumns.Count][];
        Set(PersonColumns.FirstName, "FirstName", "Prenume");
        Set(PersonColumns.LastName, "LastName", "Nume");
        Set(PersonColumns.Function, "Function", "Funcție");
        Set(PersonColumns.Faculty, "Faculty", "Facultate");
        Set(PersonColumns.Department, "Department", "Departament");
        Set(PersonColumns.DocumentDate, "DocumentDate", "DataDocumentului");
        Set(PersonColumns.Units, "Units", "Unități");
        Set(PersonColumns.HireType, "HireType", "TipAngajare");
        Set(PersonColumns.HomeAddress, "HomeAddress", "AdresăDomiciliu");
        Set(PersonColumns.PhoneNumber, "PhoneNumber", "Telefon");
        Set(PersonColumns.Email, "Email", "E-mail");
        Set(PersonColumns.BISeries, "BISeries", "SerieBI");
        Set(PersonColumns.IDIssueDate, "IDIssueDate", "DataEliberareBI");
        Set(PersonColumns.PersonalIdentifier, "PersonalIdentifier", "IDNP");
        Set(PersonColumns.PrimaryFunction, "PrimaryFunction", "FuncțieDeBază");
        Set(PersonColumns.PrimaryEmployer, "PrimaryEmployer", "AngajatorDeBază");
        Set(PersonColumns.WorkplaceAddress, "WorkplaceAddress", "AdresaLoculuiDeMuncă");
        Set(PersonColumns.FacultyShort, "FacultyShort", "FacultateScurt");
        Set(PersonColumns.DepartmentShort, "DepartmentShort", "DepartamentScurt");
        Set(PersonColumns.PreparedByName, "PreparedByName", "ÎntocmităDe");
        Set(PersonColumns.PreparedByDepartment, "PreparedByDepartment", "DepartamentÎntocmitor");
        Set(PersonColumns.Concurs, "Concurs");

        var duplicate = labels.SelectMany(values => values)
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Etichetă de coloană duplicată: {duplicate.Key}");
        }
        return labels;

        void Set(PersonColumns column, params string[] values) => labels[(int) column] = values;
    }
}

public static partial class DocsExcel
{
    public static List<PersonInfo> Parse(string path)
    {
        // Excel locks the workbook while it is open, so read through a
        // shared stream instead of letting ClosedXML open the file exclusively.
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var workbook = new XLWorkbook(stream);
        var worksheet = workbook.Worksheets.Worksheet("Persons");
        var headerRow = worksheet.FirstRowUsed()
            ?? throw new InvalidOperationException("Fișierul Excel trebuie să conțină antetul.");
        var indexes = FindColumnIndexes(headerRow);
        var people = new List<PersonInfo>();

        foreach (var row in worksheet.RowsUsed().Skip(1))
        {
            if (row.CellsUsed().All(cell => string.IsNullOrWhiteSpace(cell.GetString())))
            {
                continue;
            }

            people.Add(new PersonInfo
            {
                FirstName = GetRequiredString(row, indexes, PersonColumns.FirstName),
                LastName = GetRequiredString(row, indexes, PersonColumns.LastName),
                Function = AcademicFunctions.Normalize(GetRequiredString(row, indexes, PersonColumns.Function)),
                Faculty = GetRequiredString(row, indexes, PersonColumns.Faculty),
                Department = GetRequiredString(row, indexes, PersonColumns.Department),
                DocumentDate = GetDate(row, indexes, PersonColumns.DocumentDate),
                Units = GetDecimal(row, indexes, PersonColumns.Units),
                HireType = GetEnum<HireType>(row, indexes, PersonColumns.HireType),
                Concurs = GetBool(row, indexes, PersonColumns.Concurs),
                HomeAddress = GetRequiredString(row, indexes, PersonColumns.HomeAddress),
                PhoneNumber = GetRequiredString(row, indexes, PersonColumns.PhoneNumber),
                Email = GetRequiredString(row, indexes, PersonColumns.Email),
                PrimaryFunction = GetString(row, indexes, PersonColumns.PrimaryFunction),
                PrimaryEmployer = GetString(row, indexes, PersonColumns.PrimaryEmployer),
                WorkplaceAddress = GetRequiredString(row, indexes, PersonColumns.WorkplaceAddress),
                FacultyShort = GetString(row, indexes, PersonColumns.FacultyShort),
                DepartmentShort = GetString(row, indexes, PersonColumns.DepartmentShort),
                PreparedByName = GetRequiredString(row, indexes, PersonColumns.PreparedByName),
                PreparedByDepartment = GetRequiredString(row, indexes, PersonColumns.PreparedByDepartment),
                ID = new IDInfo
                {
                    BISeriesCode = GetRequiredString(row, indexes, PersonColumns.BISeries),
                    IssueDate = GetDate(row, indexes, PersonColumns.IDIssueDate),
                    PersonalIdentifier = GetRequiredString(row, indexes, PersonColumns.PersonalIdentifier),
                },
            });
        }

        return people;
    }

    private static int[] FindColumnIndexes(IXLRow headerRow)
    {
        var indexes = Enumerable.Repeat(-1, (int) PersonColumns.Count).ToArray();
        foreach (var cell in headerRow.CellsUsed())
        {
            var header = cell.GetString().Trim();
            for (var field = 0; field < indexes.Length; field++)
            {
                if (ColumnLabels.Labels[field].Contains(header, StringComparer.OrdinalIgnoreCase))
                {
                    indexes[field] = cell.Address.ColumnNumber;
                }
            }
        }

        for (var field = 0; field < indexes.Length; field++)
        {
            if (indexes[field] < 0)
            {
                throw new InvalidOperationException($"Lipsește coloana {ColumnLabels.Labels[field][0]}.");
            }
        }
        return indexes;
    }

    private static string GetString(IXLRow row, int[] indexes, PersonColumns column) =>
        row.Cell(indexes[(int) column]).GetString().Trim();

    private static string GetRequiredString(IXLRow row, int[] indexes, PersonColumns column)
    {
        var value = GetString(row, indexes, column);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException($"Câmpul {ColumnLabels.Labels[(int) column][0]} este gol în rândul {row.RowNumber()}.");
        }
        return value;
    }

    private static decimal GetDecimal(IXLRow row, int[] indexes, PersonColumns column)
    {
        var cell = row.Cell(indexes[(int) column]);
        if (cell.TryGetValue<decimal>(out var value) ||
            decimal.TryParse(cell.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out value) ||
            decimal.TryParse(cell.GetString(), NumberStyles.Number, CultureInfo.GetCultureInfo("ro-MD"), out value))
        {
            return value;
        }
        throw new FormatException($"Valoare numerică invalidă în {cell.Address}.");
    }

    private static DateOnly GetDate(IXLRow row, int[] indexes, PersonColumns column)
    {
        var cell = row.Cell(indexes[(int) column]);
        if (cell.TryGetValue<DateTime>(out var dateTime))
        {
            return DateOnly.FromDateTime(dateTime);
        }
        if (DateOnly.TryParse(cell.GetString(), CultureInfo.GetCultureInfo("ro-MD"), DateTimeStyles.None, out var date) ||
            DateOnly.TryParse(cell.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
        {
            return date;
        }
        throw new FormatException($"Dată invalidă în {cell.Address}.");
    }

    private static TEnum GetEnum<TEnum>(IXLRow row, int[] indexes, PersonColumns column) where TEnum : struct
    {
        var cell = row.Cell(indexes[(int) column]);
        if (Enum.TryParse<TEnum>(cell.GetString().Trim(), ignoreCase: true, out var value))
        {
            return value;
        }
        throw new FormatException($"Valoare invalidă '{cell.GetString()}' pentru {column} în {cell.Address}.");
    }

    private static bool GetBool(IXLRow row, int[] indexes, PersonColumns column)
    {
        var cell = row.Cell(indexes[(int) column]);
        if (cell.TryGetValue<bool>(out var value))
        {
            return value;
        }
        return cell.GetString().Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "da" or "yes" or "x" => true,
            "false" or "0" or "nu" or "no" or "" => false,
            _ => throw new FormatException($"Valoare invalidă '{cell.GetString()}' pentru {column} în {cell.Address}. Așteptat TRUE/FALSE."),
        };
    }
}
