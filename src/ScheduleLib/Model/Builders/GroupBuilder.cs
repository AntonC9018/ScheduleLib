using System.Runtime.InteropServices;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.Builders;

partial class ScheduleBuilder
{
    public ListBuilder<Group> Groups = new();
}

public struct GroupBuilder
{
    public required ScheduleBuilder Schedule { get; init; }
    public required GroupId Id { get; init; }

    public ref Group Ref => ref Schedule.Groups.Ref(Id.Value);
    public static implicit operator GroupId(GroupBuilder g) => g.Id;
}

public static class GroupBuilderHelper
{
    public static void SetStudyYear(this ScheduleBuilder s, int year)
    {
        if (s.Groups.Count != 0)
        {
            throw new InvalidOperationException("The year must be initialized prior to creating groups.");
        }
        s.GroupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = year,
        });
    }

    private static int DetermineStudyYear()
    {
        var now = DateTime.Now;
        if (now.Month >= 8 && now.Month <= 12)
        {
            return now.Year;
        }
        return now.Year - 1;
    }

    public static GroupBuilder Group(this ScheduleBuilder s, string fullName)
    {
        s.GroupParseContext ??= GroupParseContext.Create(new()
        {
            CurrentStudyYear = DetermineStudyYear(),
        });

        var group = s.GroupParseContext.Parse(fullName);

        int ret;
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
            Id = new(ret),
            Schedule = s,
        };

        static int Default(ScheduleBuilder s, Group group)
        {
            var result = s.Groups.New();
            result.Value = group;
            return result.Id;
        }
    }

    public static void ValidateGroups(ScheduleBuilder s)
    {
        foreach (ref var group in CollectionsMarshal.AsSpan(s.Groups.List))
        {
            if (group.Name == null)
            {
                throw new InvalidOperationException("The group name must be initialized.");
            }

            if (group.Grade == 0)
            {
                throw new InvalidOperationException("The group grade must be initialized.");
            }
        }
    }
}
