namespace ScheduleLib.OnlineRegistry;

/// <summary>Assigns legacy rows once, before reconciling independent audience sequences.</summary>
internal static class RegistryScopeReconciliation
{
    internal static Dictionary<Uri, string> Assign(
        Schedule schedule,
        LessonInstance[] local,
        RemoteLessonInstance[] remote,
        IReadOnlyCollection<string> knownScopes,
        Func<LessonInstance, string> getScope,
        IEquationCommandsDerivation derivation,
        Action<RemoteLessonInstance>? unresolvedAssessment = null,
        Func<RemoteLessonInstance, string, string?>? resolveScope = null,
        IDictionary<Uri, LessonType>? inferredTypes = null)
    {
        var owners = new Dictionary<Uri, string>();
        var legacy = new List<RemoteLessonInstance>();
        foreach (var row in remote)
        {
            if (RegistryTopicScope.TrySplit(
                    topic: row.Topic,
                    knownScopes,
                    baseTopic: out _,
                    scope: out var scope))
            {
                var owner = resolveScope is null ? scope : resolveScope(row, scope!);
                if (owner is not null)
                {
                    owners[row.ViewUri] = owner;
                }

                continue;
            }
            if (!RegistryAssessmentTopics.IsAssessment(row.Topic))
            {
                legacy.Add(row);
                continue;
            }
            var candidates = local.Where(lesson => lesson.DateTime == row.DateTime
                    && MatchesType(lesson, row))
                .Select(getScope)
                .Distinct()
                .ToArray();
            if (candidates.Length == 0)
            {
                candidates = local.Where(lesson => lesson.DateTime.Date == row.DateTime.Date
                        && MatchesType(lesson, row))
                    .Select(getScope)
                    .Distinct()
                    .ToArray();
            }

            if (candidates.Length == 1)
            {
                owners[row.ViewUri] = candidates[0];
            }
            else if (candidates.Length > 1)
            {
                unresolvedAssessment?.Invoke(row);
            }
        }
        // Preserve normal duplicate and legacy pairing, while preventing scoped rows from crossing audiences.
        var attributionTopic = "registry-scope-attribution";
        while (remote.Any(row => row.Topic == attributionTopic))
        {
            attributionTopic += "_";
        }

        var legacyLocal = local.Where(lesson => !remote.Any(row =>
            {
                if (!owners.TryGetValue(row.ViewUri, out var scope))
                {
                    return false;
                }
                if (!(scope == getScope(lesson)))
                {
                    return false;
                }
                if (!(row.DateTime == lesson.DateTime))
                {
                    return false;
                }
                if (!MatchesType(lesson, row))
                {
                    return false;
                }
                return true;
            }))
            .Select(lesson => lesson with
            {
                Topic = attributionTopic,
                Attendance = null,
            })
            .ToArray();
        foreach (var type in legacy.GroupBy(row => row.LessonType))
        {
            foreach (var command in derivation.DeriveCommands(new(
                         schedule,
                         type,
                         legacyLocal.Where(lesson =>
                             type.Key == LessonType.Unspecified || lesson.RegistryLessonType == type.Key))))
            {
                if (command.Type == LessonEquationCommandType.Update)
                {
                    owners[command.Remote.ViewUri] = getScope(command.Local);
                    if (inferredTypes is not null)
                    {
                        inferredTypes[command.Remote.ViewUri] = command.Local.RegistryLessonType;
                    }
                }
            }
        }

        return owners;
    }

    internal static IEnumerable<LessonEquationCommand> Derive(
        Schedule schedule,
        LessonInstance[] local,
        RemoteLessonInstance[] remote,
        IReadOnlyDictionary<Uri, string> owners,
        Func<LessonInstance, string> getScope,
        IEquationCommandsDerivation derivation,
        IReadOnlyDictionary<Uri, LessonType>? inferredTypes = null)
    {
        var consumed = new HashSet<Uri>();
        foreach (var audience in local.GroupBy(lesson => (Scope: getScope(lesson), Type: lesson.RegistryLessonType)))
        {
            var matching = remote.Where(row =>
                {
                    if (consumed.Contains(row.ViewUri))
                    {
                        return false;
                    }

                    if (owners.GetValueOrDefault(row.ViewUri) != audience.Key.Scope)
                    {
                        return false;
                    }

                    if (row.LessonType == audience.Key.Type)
                    {
                        return true;
                    }

                    if (row.LessonType != LessonType.Unspecified)
                    {
                        return false;
                    }

                    if (inferredTypes is not null)
                    {
                        if (inferredTypes.TryGetValue(row.ViewUri, out var inferred))
                        {
                            if (inferred == audience.Key.Type)
                            {
                                return true;
                            }

                            return false;
                        }
                    }

                    if (audience.Any(lesson => lesson.DateTime == row.DateTime))
                    {
                        return true;
                    }

                    if (local.Where(lesson => getScope(lesson) == audience.Key.Scope)
                            .Select(lesson => lesson.RegistryLessonType)
                            .Distinct()
                            .Count()
                        == 1)
                    {
                        return true;
                    }

                    return false;
                })
                .ToArray();
            foreach (var row in matching)
            {
                consumed.Add(row.ViewUri);
            }

            foreach (var command in derivation.DeriveCommands(new(
                         schedule,
                         matching,
                         audience)))
            {
                yield return command;
            }
        }
        foreach (var row in remote.Where(row =>
                     !consumed.Contains(row.ViewUri) && !RegistryAssessmentTopics.IsAssessment(row.Topic)))
        {
            yield return LessonEquationCommand.Delete(row);
        }
    }

    private static bool MatchesType(LessonInstance local, RemoteLessonInstance remote)
        => remote.LessonType == LessonType.Unspecified || local.RegistryLessonType == remote.LessonType;
}
