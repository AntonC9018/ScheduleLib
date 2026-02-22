using System.Diagnostics;

namespace ScheduleLib.OnlineRegistry;

public readonly struct GetLessonEquationCommandsParams
{
    public readonly Schedule Schedule;
    // TODO: Must never be enumerated more than once, that breaks counters.
    public readonly IEnumerable<RemoteLessonInstance> RemoteLessons;
    public readonly IEnumerable<LessonInstance> LocalLessons;

    public GetLessonEquationCommandsParams(
        Schedule schedule,
        IEnumerable<RemoteLessonInstance> remoteLessons,
        IEnumerable<LessonInstance> localLessons)
    {
        Schedule = schedule;

        // ReSharper disable once PossibleMultipleEnumeration
        MissingLessonDetection.AssertOrdered(ref localLessons, x => x.DateTime);
        // ReSharper disable once PossibleMultipleEnumeration
        MissingLessonDetection.AssertOrdered(ref remoteLessons, x => x.DateTime);

        // ReSharper disable once PossibleMultipleEnumeration
        RemoteLessons = remoteLessons;
        // ReSharper disable once PossibleMultipleEnumeration
        LocalLessons = localLessons;
    }
}


public readonly record struct LessonInstance : IDateTime
{
    public required AnyLessonId LessonId { get; init; }
    public required DateTime DateTime { get; init; }
    public required string? Topic { get; init; }
    public required Attendance[]? Attendance { get; init; }
    public required LessonType RegistryLessonType { get; init; }
}

public interface IEquationCommandsDerivation
{
    IEnumerable<LessonEquationCommand> DeriveCommands(GetLessonEquationCommandsParams p);
}

public enum LessonEquationCommandType
{
    Create,
    Update,
    Delete,
    Count,
}

[Flags]
public enum LessonEquationCommandTypes
{
    None = 0,
    Create = 1 << (int) LessonEquationCommandType.Create,
    Update = 1 << (int) LessonEquationCommandType.Update,
    Delete = 1 << (int) LessonEquationCommandType.Delete,
    All = Create | Update | Delete,
}

public static class LessonEquationCommandTypeHelper
{
    public static bool HasAll(this LessonEquationCommandType type)
    {
        return type is LessonEquationCommandType.Create or LessonEquationCommandType.Update;
    }

    public static bool HasExisting(this LessonEquationCommandType type)
    {
        return type is LessonEquationCommandType.Update or LessonEquationCommandType.Delete;
    }
}

public readonly struct LessonEquationCommand
{
    public readonly LessonEquationCommandType Type;
    private readonly RemoteLessonInstance _remote;
    private readonly LessonInstance _local;

    private LessonEquationCommand(
        LessonEquationCommandType type,
        RemoteLessonInstance remote = default,
        LessonInstance local = default)
    {
        Type = type;
        _remote = remote;
        _local = local;
    }

    public bool HasAll => Type.HasAll();
    public LessonInstance Local
    {
        get
        {
            Debug.Assert(HasAll);
            return _local;
        }
    }

    public bool HasExisting => Type.HasExisting();
    public RemoteLessonInstance Remote
    {
        get
        {
            Debug.Assert(HasExisting);
            return _remote;
        }
    }


    public static LessonEquationCommand Create(LessonInstance local)
    {
        return new(LessonEquationCommandType.Create, local: local);
    }

    public static LessonEquationCommand Update(RemoteLessonInstance remote, LessonInstance local)
    {
        return new(LessonEquationCommandType.Update, remote: remote, local: local);
    }

    public static LessonEquationCommand Delete(RemoteLessonInstance remote)
    {
        return new(LessonEquationCommandType.Delete, remote: remote);
    }
}
