using AutoConstructor.Attributes;
using Microsoft.Extensions.Logging;
using ScheduleLib.Application.Core.Helper;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;
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

[AutoConstructor]
public sealed partial class ListsForPredzashitaTaskHandler
{
    private static partial (string Student, string Group)[] AvrStudents { get; }
    private static partial Commission<CommissionMember>[] Commissions { get; }

    private readonly ThesesListProvider _thesesListProvider;
    private readonly ILogger _logger;
    private readonly ScheduleBuilder _builder;

    public async Task Handle(
        OutputDirectory outputDirectory,
        CancellationToken cancellationToken)
    {
        var theses = await _thesesListProvider.DownloadAndParse(cancellationToken);

        var avrLookup = new HashSet<NameAndGroup>(Hasher.Instance);
        foreach (var x in AvrStudents)
        {
            var name = NameHelper.Parse(x.Student);
            avrLookup.Add(new(name, x.Group));
        }
        var nameComparer = Name_IgnoreDiacritics_AllowNoPatronymic_EqualityComparer.Instance;

        Name FixName(Name name, bool isTeacher)
        {
            var x = name.Copy();
            if (isTeacher)
            {
                x.LastName = _builder.RemapTeacherName(new(x.LastName));
            }
            NameParts<string?> FixRussian(NameParts<string?> s)
            {
                return s.Map(p =>
                {
                    return p?.Replace("а", "a").Replace("А", "A").Replace("Е", "E").Replace("е", "e");
                });
            }

            x.LastName = FixRussian(x.LastName);
            x.FirstName = FixRussian(x.FirstName);
            return x;
        }

        var studentByTeacher = new Dictionary<Name, List<ThesisRecord>>(nameComparer);
        foreach (var t in theses[ThesisType.Licenta].Items)
        {
            var group = t.GroupName;
            var studentName = FixName(t.StudentName, isTeacher: false);
            var teacherName = FixName(t.TeacherName, isTeacher: true);

            var thesisName = string.IsNullOrEmpty(t.ThesisNameRomanian) ? t.ThesisNameRussian : t.ThesisNameRomanian;
            thesisName ??= "";
            var list = studentByTeacher.GetOrAdd(teacherName, _ => new());
            list.Add(new(studentName, group, thesisName));
        }

        var commissionsWithParsedNames = Commissions.Select(x =>
        {
            return new Commission<Name>
            {
                Members = x.Members.Select(y =>
                {
                    var name = NameHelper.Parse(y.Name);
                    return FixName(name, isTeacher: true);
                }).ToArray(),
                Number = x.Number,
                Room = x.Room,
            };
        }).ToArray();

        Dictionary<Name, Commission<Name>> commisionByTeacher = new(nameComparer);
        foreach (var c in commissionsWithParsedNames)
        {
            foreach (var m in c.Members)
            {
                commisionByTeacher.Add(m, c);
            }
        }

        var teachersNotInAnyCommision = studentByTeacher.Keys.Except(commisionByTeacher.Keys);
        foreach (var x in teachersNotInAnyCommision)
        {
            _logger.LogWarning("Teacher {Teacher} has students but is not part of any commission", x.ToString());
        }

        Dictionary<int, List<StudentThesisRecord>> thesesOfCommision = new();
        foreach (var c in commissionsWithParsedNames)
        {
            thesesOfCommision.Add(c.Number, new());
        }
        foreach (var (teacher, commision) in commisionByTeacher)
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

        await WriteAllAsync(thesesOfCommision, outputDirectory, cancellationToken);
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
