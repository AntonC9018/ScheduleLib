using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Core.Services;
using ScheduleLib.Parsing;
using ScheduleLib.Theses.Parsing;
using SpreadCheetah;

namespace ScheduleLib.Application.Core;

public sealed class Commission<TMember>
{
    public required int Number { get; init; }
    public required string Room { get; init; }
    public required TMember[] Members { get; init; }
}

public sealed class CommissionMember
{
    public required string Name { get; init; }
    public bool IsPresident { get; init; }
}

/// <summary>Optional C# configuration; null values retain the existing coded data.</summary>
public sealed class PreDefenseOptions
{
    public Commission<CommissionMember>[]? Commissions { get; set; }
    public (string Student, string Group)[]? AvrStudents { get; set; }
}

public sealed partial class ListsForPredzashitaTaskHandler
{
    // Initialize these in a static constructor.
    private static (string Student, string Group)[] AvrStudents { get; set; } = [];
    private static Commission<CommissionMember>[] Commissions { get; set; } = [];

    private readonly ThesesListProvider _thesesListProvider;
    private readonly ILogger _logger;
    private readonly INameRemapper _nameRemapper;
    private readonly PreDefenseOptions _options;

    public static bool HasConfiguredCommissions(PreDefenseOptions options) =>
        (options.Commissions ?? Commissions) is { Length: > 0 } configured
        && configured.All(x => x.Members.Length > 0);

    public ListsForPredzashitaTaskHandler(
        ThesesListProvider thesesListProvider,
        ILogger<ListsForPredzashitaTaskHandler> logger,
        [FromKeyedServices(NameMappingKeys.Teacher)] INameRemapper nameRemapper,
        IOptions<PreDefenseOptions> options)
    {
        _thesesListProvider = thesesListProvider;
        _logger = logger;
        _nameRemapper = nameRemapper;
        _options = options.Value;
    }

    public string? GetConfigurationError()
    {
        if (!HasConfiguredCommissions(_options))
            return "Pre-defense requires nonempty commissions with members in the existing C# configuration. Configure commissions before running export pre-defense.";
        return GetConfigurationError(ParseCommissionNames());
    }

    private static string? GetConfigurationError(Commission<Name>[] commissions)
    {
        var numbers = new HashSet<int>();
        var members = new HashSet<Name>(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        foreach (var commission in commissions)
        {
            if (!numbers.Add(commission.Number))
                return $"Pre-defense commission number {commission.Number} is configured more than once.";
            foreach (var member in commission.Members)
            {
                if (!members.Add(member))
                    return $"Pre-defense teacher '{member}' is configured more than once among commission members after name remapping.";
            }
        }
        return null;
    }

    private Commission<Name>[] ParseCommissionNames() => (_options.Commissions ?? Commissions).Select(x =>
        new Commission<Name>
        {
            Members = x.Members.Select(y => _nameRemapper.RemapName(NameHelper.Parse(y.Name))).ToArray(),
            Number = x.Number,
            Room = x.Room,
        }).ToArray();

    public async Task Handle(
        ThesisType thesisType,
        OutputDirectory outputDirectory,
        CancellationToken cancellationToken)
    {
        var data = await BuildWorkbooks(thesisType, cancellationToken);
        await WriteAllAsync(data, outputDirectory, cancellationToken);
    }

    public async Task<Dictionary<int, List<StudentThesisRecord>>> BuildWorkbooks(
        ThesisType thesisType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasConfiguredCommissions(_options))
            throw new InvalidDataException("Pre-defense requires commission data in the existing C# configuration.");
        var commissionsWithParsedNames = ParseCommissionNames();
        if (GetConfigurationError(commissionsWithParsedNames) is { } error)
            throw new InvalidDataException(error);
        var theses = await _thesesListProvider.DownloadAndParse(cancellationToken);

        var avrLookup = new HashSet<NameAndGroup>(Hasher.Instance);
        foreach (var x in _options.AvrStudents ?? AvrStudents)
        {
            var name = NameHelper.Parse(x.Student);
            avrLookup.Add(new(name, x.Group));
        }
        var nameComparer = Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance;

        var studentByTeacher = new Dictionary<Name, List<ThesisRecord>>(nameComparer);
        foreach (var t in theses[thesisType].Items)
        {
            var group = t.GroupName;
            var studentName = t.StudentName;
            var teacherName = t.TeacherName;

            var thesisName = t.ThesisNames.Storage.First(x => x != null) ?? "";
            var list = studentByTeacher.GetOrAdd(teacherName, _ => new());
            list.Add(new(studentName, group, thesisName));
        }

        Dictionary<Name, Commission<Name>> commissionByTeacher = new(nameComparer);
        foreach (var c in commissionsWithParsedNames)
        {
            foreach (var m in c.Members)
            {
                commissionByTeacher.Add(m, c);
            }
        }

        var teachersNotInAnyCommision = studentByTeacher.Keys.Except(commissionByTeacher.Keys);
        foreach (var x in teachersNotInAnyCommision)
        {
            _logger.LogWarning("Teacher {Teacher} has students but is not part of any commission", x.ToString());
        }

        Dictionary<int, List<StudentThesisRecord>> thesesOfCommision = new();
        foreach (var c in commissionsWithParsedNames)
        {
            thesesOfCommision.Add(c.Number, new());
        }
        foreach (var (teacher, commision) in commissionByTeacher)
        {
            var list = thesesOfCommision[commision.Number];
            if (!studentByTeacher.TryGetValue(teacher, out var theses1))
            {
                _logger.LogWarning("Teacher {Teacher} is in a commission but has no students!", teacher.ToString());
                continue;
            }
            foreach (var t in theses1)
            {
                string? practicaPlace = null;
                if (avrLookup.Contains(new(t.StudentName, t.Group)))
                {
                    practicaPlace = "Laborator AVR";
                }

                list.Add(new(
                    t.StudentName,
                    teacher,
                    t.Group,
                    t.ThesisName,
                    practicaPlace));
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        return thesesOfCommision;
    }

    public static async Task WriteAsync(
        int commissionNumber,
        List<StudentThesisRecord> data,
        Stream output,
        CancellationToken cancellationToken)
    {
        await using var spreadsheet = await Spreadsheet.CreateNewAsync(output, options: null, cancellationToken);
        const string font = "Times New Roman";

        var headerStyle = spreadsheet.AddStyle(new()
        {
            Fill = new()
            {
                Color = System.Drawing.Color.LightGray,
            },
            Font = new()
            {
                Name = font,
                Bold = true,
                Color = System.Drawing.Color.Black,
            },
        });
        var bodyStyle = spreadsheet.AddStyle(new()
        {
            Font = new()
            {
                Name = font,
                Color = System.Drawing.Color.Black,
            },
        });

        await spreadsheet.StartWorksheetAsync($"Comisia {commissionNumber}", options: null, cancellationToken);

        // Header
        Cell HeaderCell(string s) => new(s, headerStyle);
        await spreadsheet.AddRowAsync(
        [
            HeaderCell("Nr."),
            HeaderCell("Nume, prenume student"),
            HeaderCell("Grupa"),
            HeaderCell("Conducător științific"),
            HeaderCell("Compania / locul de practică"),
            HeaderCell("Documente prezentate (agenda / raport)"),
            HeaderCell("Observații privind prezentarea și activitatea"),
            HeaderCell("Nota finală"),
        ], cancellationToken);

        for (int i = 0; i < data.Count; i++)
        {
            var student = data[i];

            Cell BodyCell<T>(T val)
            {
                if (val is int i1)
                {
                    return new(i1, bodyStyle);
                }
                else if (val is string s1)
                {
                    return new(s1, bodyStyle);
                }
                throw Unreachable();
            }

            await spreadsheet.AddRowAsync(
            [
                BodyCell(i + 1),
                BodyCell(student.StudentName.ToString()),
                BodyCell(student.Group),
                BodyCell(student.TeacherName.ToString()),
                BodyCell(student.PracticaPlace ?? ""),
                BodyCell("da/da"),
                BodyCell(student.ThesisName),
                BodyCell(10),
            ], cancellationToken);
        }
        await spreadsheet.FinishAsync(cancellationToken);
    }

    public static async Task WriteAllAsync(
        Dictionary<int, List<StudentThesisRecord>> data,
        OutputDirectory output,
        CancellationToken cancellationToken)
    {
        foreach (var (commissionNumber, students) in data)
        {
            await using var outputFile = output.OpenFile($"comisia_{commissionNumber}.xlsx", FileMode.Create, FileAccess.Write);
            await WriteAsync(
                commissionNumber,
                students,
                outputFile,
                cancellationToken);
        }
    }
}

public readonly record struct NameAndGroup(Name Name, string Group);
public readonly record struct ThesisRecord(
    Name StudentName,
    string Group,
    string ThesisName);
public sealed record class StudentThesisRecord(
    Name StudentName,
    Name TeacherName,
    string Group,
    string ThesisName,
    string? PracticaPlace);

public sealed class Hasher : IEqualityComparer<NameAndGroup>
{
    public static readonly Hasher Instance = new();

    public bool Equals(NameAndGroup x, NameAndGroup y)
    {
        if (!Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance.Equals(x.Name, y.Name))
        {
            return false;
        }

        if (x.Group != y.Group)
        {
            return false;
        }

        return true;
    }

    public int GetHashCode(NameAndGroup obj)
    {
        var a = Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance.GetHashCode(obj.Name);
        var b = obj.Group.GetHashCode();
        return HashCode.Combine(a, b);
    }
}
