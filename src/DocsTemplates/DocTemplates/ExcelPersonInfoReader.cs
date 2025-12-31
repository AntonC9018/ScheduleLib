using System.Globalization;
using ClosedXML.Excel;

namespace EmploymentDocs;

public enum PersonColumns
{
    FirstName,
    LastName,
    Function,
    Faculty,
    Department,
    Date,
    Units,
    HireType,
    WorkingPlace,
    WorkingMode,
    ContractEndDate,
    ProbationPeriodEndDate,
    HomeAddress,
    PhoneNumber,
    Email,
    BISeries,
    IDIssueDate,
    PersonalIdentifier,

    Count,
}

public static class ColumnLabels
{
    public static readonly string[][] Labels;

    static ColumnLabels()
    {
        Labels = new string[(int) PersonColumns.Count][];

        Set(PersonColumns.FirstName, "FirstName", "Prenume");
        Set(PersonColumns.LastName, "LastName", "Nume");
        Set(PersonColumns.Function, "Function", "Funcție");
        Set(PersonColumns.Faculty, "Faculty", "Facultate");
        Set(PersonColumns.Department, "Department", "Departament");
        Set(PersonColumns.Date, "Date", "Data");
        Set(PersonColumns.Units, "Units", "Unități");
        Set(PersonColumns.HireType, "HireType", "TipAngajare");
        Set(PersonColumns.WorkingPlace, "WorkingPlace", "LocMuncă");
        Set(PersonColumns.WorkingMode, "WorkingMode", "ModLucru");
        Set(PersonColumns.ContractEndDate, "ContractEndDate", "DataSfârșitContract");
        Set(PersonColumns.ProbationPeriodEndDate, "ProbationPeriodEndDate", "DataSfârșitProbă");
        Set(PersonColumns.HomeAddress, "HomeAddress", "Adresă");
        Set(PersonColumns.PhoneNumber, "PhoneNumber", "Telefon");
        Set(PersonColumns.Email, "Email", "E-mail");
        Set(PersonColumns.BISeries, "BISeries", "SerieBI");
        Set(PersonColumns.IDIssueDate, "IDIssueDate", "DataEliberareBI");
        Set(PersonColumns.PersonalIdentifier, "PersonalIdentifier", "CodPersonal");

        ValidateNoDuplicates();
    }

    private static void Set(PersonColumns col, params string[] labels)
    {
        Labels[(int) col] = labels;
    }

    private static void ValidateNoDuplicates()
    {
        var seen = new Dictionary<string, PersonColumns>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < (int) PersonColumns.Count; i++)
        {
            if (Labels is null)
            {
                throw new InvalidOperationException($"No labels defined for {(PersonColumns) i}");
            }

            foreach (var label in Labels[i])
            {
                if (seen.TryGetValue(label, out var existing))
                {
                    throw new InvalidOperationException(
                        $"Duplicate label '{label}' found in {existing} and {(PersonColumns) i}");
                }

                seen[label] = (PersonColumns) i;
            }
        }
    }
}

public static partial class DocsExcel
{
    // ChatGPT code
    public static List<PersonInfo> Parse(string path)
    {
        using var wb = new XLWorkbook(path);
        var ws = wb.Worksheets.Worksheet(1);

        // Map header labels → column indices
        var headerRow = ws.FirstRowUsed();
        if (headerRow is null)
        {
            throw new InvalidOperationException("Excel must contain the header row");
        }
        var colCount = headerRow.CellCount();

        int[] fieldColumnIndexes = new int[(int) PersonColumns.Count];
        for (int i = 0; i < fieldColumnIndexes.Length; i++)
        {
            fieldColumnIndexes[i] = -1;
        }

        for (int c = 1; c <= colCount; c++)
        {
            string header = headerRow.Cell(c).GetString().Trim();
            if (string.IsNullOrEmpty(header))
            {
                continue;
            }

            for (int f = 0; f < (int) PersonColumns.Count; f++)
            {
                foreach (var label in ColumnLabels.Labels[f])
                {
                    if (string.Equals(header, label, StringComparison.OrdinalIgnoreCase))
                    {
                        fieldColumnIndexes[f] = c;
                    }
                }
            }
        }

        // Validate required columns
        foreach (PersonColumns col in Enum.GetValues(typeof(PersonColumns)))
        {
            if (col == PersonColumns.Count)
            {
                continue;
            }

            if (fieldColumnIndexes[(int) col] == -1)
            {
                throw new InvalidOperationException($"Missing column for {col}");
            }
        }

        var people = new List<PersonInfo>();

        foreach (var row in ws.RowsUsed().Skip(1))
        {
            var person = new PersonInfo
            {
                FirstName = GetString(row, fieldColumnIndexes[(int) PersonColumns.FirstName]),
                LastName = GetString(row, fieldColumnIndexes[(int) PersonColumns.LastName]),
                Function = GetString(row, fieldColumnIndexes[(int) PersonColumns.Function]),
                Faculty = GetString(row, fieldColumnIndexes[(int) PersonColumns.Faculty]),
                Department = GetString(row, fieldColumnIndexes[(int) PersonColumns.Department]),
                Date = GetDate(row, fieldColumnIndexes[(int) PersonColumns.Date]),
                Units = GetFloat(row, fieldColumnIndexes[(int) PersonColumns.Units]),
                HireType = GetEnum<HireType>(row, fieldColumnIndexes[(int) PersonColumns.HireType]),
                WorkingPlace = GetString(row, fieldColumnIndexes[(int) PersonColumns.WorkingPlace]),
                WorkingMode = GetEnum<WorkingMode>(row, fieldColumnIndexes[(int) PersonColumns.WorkingMode]),
                ContractEndDate = GetDateOrMin(row, fieldColumnIndexes[(int) PersonColumns.ContractEndDate]),
                ProbationPeriodEndDate =
                    GetDateOrMin(row, fieldColumnIndexes[(int) PersonColumns.ProbationPeriodEndDate]),
                HomeAddress = GetString(row, fieldColumnIndexes[(int) PersonColumns.HomeAddress]),
                PhoneNumber = GetString(row, fieldColumnIndexes[(int) PersonColumns.PhoneNumber]),
                Email = GetString(row, fieldColumnIndexes[(int) PersonColumns.Email]),
                ID = new IDInfo
                {
                    BSeriesCode = GetString(row, fieldColumnIndexes[(int) PersonColumns.BISeries]),
                    IssueDate = GetDate(row, fieldColumnIndexes[(int) PersonColumns.IDIssueDate]),
                    PersonalIdentifier =
                        GetString(row, fieldColumnIndexes[(int) PersonColumns.PersonalIdentifier]),
                },
            };

            people.Add(person);
        }

        return people;
    }

    private static string GetString(IXLRow row, int col)
    {
        return row.Cell(col).GetString().Trim();
    }

    private static float GetFloat(IXLRow row, int col)
    {
        return float.TryParse(row.Cell(col).GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            ? f
            : 0;
    }

    private static DateOnly GetDate(IXLRow row, int col)
    {
        if (row.Cell(col).DataType == XLDataType.DateTime)
        {
            return DateOnly.FromDateTime(row.Cell(col).GetDateTime());
        }

        if (DateTime.TryParse(row.Cell(col).GetString(), out var dt))
        {
            return DateOnly.FromDateTime(dt);
        }

        throw new FormatException($"Invalid date in cell {row.Cell(col).Address}");
    }

    private static DateOnly GetDateOrMin(IXLRow row, int col)
    {
        var txt = row.Cell(col).GetString();
        if (string.IsNullOrWhiteSpace(txt))
        {
            return DateOnly.MinValue;
        }

        return GetDate(row, col);
    }

    private static TEnum GetEnum<TEnum>(IXLRow row, int col) where TEnum : struct
    {
        var txt = row.Cell(col).GetString().Trim();
        if (Enum.TryParse<TEnum>(txt, ignoreCase: true, out var value))
        {
            return value;
        }

        throw new FormatException($"Invalid enum value '{txt}' for {typeof(TEnum).Name} at {row.Cell(col).Address}");
    }
}
