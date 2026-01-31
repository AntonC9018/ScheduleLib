using ScheduleLib;
using ScheduleLib.OnlineRegistry;

namespace OnlineRegistry.OnlineRegistry.Impl;

public sealed class AnyDayDerivation : IEquationCommandsDerivation
{
    public IEnumerable<LessonEquationCommand> DeriveCommands(GetLessonEquationCommandsParams p)
    {
        // ReSharper disable once GenericEnumeratorNotDisposed
        using var remoteE = p.RemoteLessons.GetEnumerator().RememberIsDone();
        // ReSharper disable once GenericEnumeratorNotDisposed
        using var localE = p.LocalLessons.GetEnumerator().RememberIsDone();

        remoteE.MoveNext();
        localE.MoveNext();

        while (true)
        {
            if (remoteE.IsDone)
            {
                break;
            }
            if (localE.IsDone)
            {
                break;
            }

            var match = new MatchedLessonData
            {
                Local = localE.Current,
                Remote = remoteE.Current,
            };
            remoteE.MoveNext();
            localE.MoveNext();

            if (match.Equals(p.Schedule))
            {
                continue;
            }

            yield return LessonEquationCommand.Update(match.Remote, match.Local);
        }

        while (!remoteE.IsDone)
        {
            yield return LessonEquationCommand.Delete(remoteE.Current);
            remoteE.MoveNext();
        }

        while (!localE.IsDone)
        {
            yield return LessonEquationCommand.Create(localE.Current);
            localE.MoveNext();
        }
    }
}


