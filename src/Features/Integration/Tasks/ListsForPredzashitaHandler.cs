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
    public AvrStudent[]? AvrStudents { get; set; }
}

/// <summary>A student that is listed for pre-defense by their own group.</summary>
public readonly record struct AvrStudent(string Student, string Group);

public sealed partial class ListsForPredzashitaTaskHandler
{
    // Initialize these in a static constructor.
    private static AvrStudent[] AvrStudents { get; set; } = [];
    private static Commission<CommissionMember>[] Commissions { get; set; } = [];

    private readonly ThesesListProvider _thesesListProvider;
    private readonly ILogger _logger;
    private readonly INameRemapper _nameRemapper;
    private readonly PreDefenseOptions _options;

    public static bool HasConfiguredCommissions(PreDefenseOptions options)
    {
        return (options.Commissions ?? Commissions) is { Length: > 0 } configured
            && configured.All(x => x.Members.Length > 0);
    }

    public ListsForPredzashitaTaskHandler(
        ThesesListProvider thesesListProvider,
        ILogger<ListsForPredzashitaTaskHandler> logger,
        [FromKeyedServices(NameMappingKeys.Teacher)]
        INameRemapper nameRemapper,
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
        {
            return
                "Pre-defense requires nonempty commissions with members in the existing C# configuration. Configure commissions before running export pre-defense.";
        }

        try
        {
            var result = GetConfigurationError(ParseCommissionNames());
            if (result is null)
            {
                return null;
            }

            return result;
        }
        catch (InvalidDataException e) { return e.Message; }
    }

    private static string? GetConfigurationError(Commission<Name>[] commissions)
    {
        var numbers = new HashSet<int>();
        var members = new HashSet<Name>(Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance);
        foreach (var commission in commissions)
        {
            if (!numbers.Add(commission.Number))
            {
                return $"Pre-defense commission number {commission.Number} is configured more than once.";
            }

            foreach (var member in commission.Members)
            {
                if (!members.Add(member))
                {
                    return
                        $"Pre-defense teacher '{member}' is configured more than once among commission members after name remapping.";
                }
            }
        }
        return null;
    }

    private Commission<Name>[] ParseCommissionNames()
    {
        return (_options.Commissions ?? Commissions).Select(x =>
                new Commission<Name>
                {
                    Members = x.Members.Select((y, index) =>
                            ParseCommissionMember(
                                commissionNumber: x.Number,
                                memberNumber: index + 1,
                                y.Name))
                        .ToArray(),
                    Number = x.Number,
                    Room = x.Room,
                })
            .ToArray();
    }

    private Name ParseCommissionMember(
        int commissionNumber,
        int memberNumber,
        string name)
    {
        try
        {
            var iNameRemapper = _nameRemapper;
            var name2 = NameHelper.Parse(name);
            var remapName = iNameRemapper.RemapName(name2);
            return remapName;
        }
        catch (NameParsingException e)
        {
            throw PreDefenseInvalidDataExceptionFactory.ForInvalidMemberName(
                commissionNumber: commissionNumber,
                memberNumber: memberNumber,
                name,
                e);
        }
    }

    public async Task Handle(
        ThesisType thesisType,
        OutputDirectory outputDirectory,
        CancellationToken cancellationToken)
    {
        var data = await BuildWorkbooks(thesisType, cancellationToken);
        await WriteAllAsync(
            data,
            outputDirectory,
            cancellationToken);
    }

    public async Task<Dictionary<int, List<StudentThesisRecord>>> BuildWorkbooks(
        ThesisType thesisType,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!HasConfiguredCommissions(_options))
        {
            throw new InvalidDataException("Pre-defense requires commission data in the existing C# configuration.");
        }

        var commissionsWithParsedNames = ParseCommissionNames();
        if (GetConfigurationError(commissionsWithParsedNames) is { } error)
        {
            throw new InvalidDataException(error);
        }

        var theses = await _thesesListProvider.DownloadAndParse(cancellationToken);

        var avrLookup = new HashSet<NameAndGroup>(Hasher.Instance);
        foreach (var x in _options.AvrStudents ?? AvrStudents)
        {
            var name = NameHelper.Parse(x.Student);
            var hashSet =
                avrLookup;
            NameAndGroup item = new(name, x.Group);
            hashSet.Add(item);
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
            var list2 = list;
            ThesisRecord item2 = new(
                studentName,
                Group: group,
                ThesisName: thesisName);
            list2.Add(item2);
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
                var hashSet2 =
                    avrLookup;
                NameAndGroup item3 = new(t.StudentName, t.Group);
                if (hashSet2.Contains(item3))
                {
                    practicaPlace = "Laborator AVR";
                }

                var
                    list3 = list;
                StudentThesisRecord item4 = new(
                    StudentName: t.StudentName,
                    TeacherName: teacher,
                    Group: t.Group,
                    ThesisName: t.ThesisName,
                    PracticaPlace: practicaPlace);
                list3.Add(item4);
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
        await using var spreadsheet = await Spreadsheet.CreateNewAsync(
            output,
            options: null,
            cancellationToken);
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

        string CalculateName()
        {
            string name = $"Comisia {commissionNumber}";
            return name;
        }

        await spreadsheet.StartWorksheetAsync(
            CalculateName(),
            options: null,
            cancellationToken);

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
            ],
            cancellationToken);

        for (int i = 0; i < data.Count; i++)
        {
            var student = data[i];

            Cell BodyCell<T>(T val)
            {
                if (val is int i1)
                {
                    return new(i1, bodyStyle);
                }
                if (val is string s1)
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
                ],
                cancellationToken);
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
            string CalculatePath()
            {
                string path = $"comisia_{commissionNumber}.xlsx";
                return path;
            }

            await using var outputFile = output.OpenFile(
                CalculatePath(),
                FileMode.Create,
                FileAccess.Write);
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
        if (!Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance.Equals(x: x.Name, y: y.Name))
        {
            return false;
        }

        if (x.Group == y.Group)
        {
            return true;
        }

        return false;
    }

    public int GetHashCode(NameAndGroup obj)
    {
        var a = Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance.GetHashCode(obj.Name);
        var b = obj.Group.GetHashCode();
        return HashCode.Combine(value1: a, value2: b);
    }
}
