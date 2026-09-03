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
    private static StudyYear DetermineStudyYear()
    {
        var now = DateTime.Now;
        if (now.Month >= 8 && now.Month <= 12)
        {
            return new(now.Year);
        }
        return new(now.Year - 1);
    }

    public static Group ParseGroup(this ScheduleBuilder s, string fullName)
    {
        s.GroupParseContext ??= GroupParseContext.Create(new()
        {
            CurrentStudyYear = DetermineStudyYear(),
        });

        var ret = s.GroupParseContext.Parse(fullName.AsMemory());
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

        static GroupId Default(ScheduleBuilder s, Group group)
        {
            var result = s.Groups.New();
            result.Value = group;
            return new(result.Id);
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

            if (group.Grade.Value == 0)
            {
                throw new InvalidOperationException("The group grade must be initialized.");
            }
        }
    }
}
