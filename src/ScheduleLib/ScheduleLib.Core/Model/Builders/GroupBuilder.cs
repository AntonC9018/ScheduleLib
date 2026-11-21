using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Builders;

partial class ScheduleBuilder
{
    public ListBuilder<GroupBuilderModel> Groups = new();
}

public sealed class GroupBuilderModel
{
    /// <summary>
    /// Does not contain the language
    /// </summary>
    public required string Name;

    public Grade Grade;
    public int GroupNumber;
    public QualificationType QualificationType;
    public Faculty Faculty;
    public AttendanceMode AttendanceMode;
    public Specialty Specialty;
    public Language Language;

    public GroupCategory Category => new(Grade, Faculty, AttendanceMode, QualificationType);

    /// <summary>Checks the name as well as the grade, because a model may be populated without the parser.</summary>
    public void Validate()
    {
        if (Name is null)
        {
            throw UninitializedScheduleModelException.ForGroupNameNotInitialized();
        }

        if (Grade.Value == 0)
        {
            throw UninitializedScheduleModelException.ForGroupGradeNotInitialized();
        }
    }

    public Group Build()
    {
        Validate();
        return new(
            name: Name,
            grade: Grade,
            groupNumber: GroupNumber,
            qualificationType: QualificationType,
            faculty: Faculty,
            attendanceMode: AttendanceMode,
            language: Language,
            specialty: Specialty);
    }
}

public readonly struct GroupBuilder
{
    public required ScheduleBuilder Schedule { get; init; }
    public required GroupId Id { get; init; }

    public GroupBuilderModel Model => Schedule.Groups.List[Id.Value];
    public static implicit operator GroupId(GroupBuilder g) => g.Id;
}

public static class GroupBuilderHelper
{
    private static StudyYear DetermineStudyYear()
    {
        var now = DateTime.Now;
        if (now.Month >= 8 && now.Month <= 12)
        {
            return new(now.Year);
        }
        return new(now.Year - 1);
    }

    public static GroupBuilderModel ParseGroup(this ScheduleBuilder s, string fullName)
    {
        var context = s.GroupParseContext;
        if (context is null)
        {
            context = GroupParseContext.Create(new()
            {
                CurrentStudyYear = DetermineStudyYear(),
            });
            s.SetDefaultGroupParseContext(context);
        }

        return context.Parse(fullName, s.Remappings);
    }

    public static GroupBuilderModel Parse(this GroupParseContext context, string fullName, Remappings remappings)
    {
        var ret = context.Parse(fullName.AsMemory());
        if (!remappings.GroupNameRemappings.TryGetValue(key: ret.Name, value: out var name))
        {
            return ret;
        }

        var language = ret.Language;
        ret = context.Parse(name.AsMemory());
        ret.Language = language;
        return ret;
    }

    public static GroupBuilder Group(this ScheduleBuilder s, string fullName)
    {
        var group = s.ParseGroup(fullName);
        GroupId ret;
        if (s.LookupModule is { } lookupModule)
        {
            ret = lookupModule.Groups.GetOrAdd(
                group.Name,
                (s, group),
                static state => Default(state.s, state.group));
        }
        else
        {
            ret = Default(s, group);
        }
        return new()
        {
            Id = ret,
            Schedule = s,
        };

        static GroupId Default(ScheduleBuilder s, GroupBuilderModel group)
        {
            var result = s.Groups.New();
            result.Value = group;
            return new(result.Id);
        }
    }

    internal static void ValidateGroups(ScheduleBuilder s)
    {
        foreach (var group in s.Groups.List)
        {
            group.Validate();
        }
    }
}
