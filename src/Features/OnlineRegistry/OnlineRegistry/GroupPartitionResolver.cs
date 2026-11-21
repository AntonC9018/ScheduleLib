namespace ScheduleLib.OnlineRegistry;

// Registry suffix semantics: empty is the whole group, a known specialization names a
// specialization partition, and anything else is a numeric subgroup ("I" is subgroup 1).
internal sealed class GroupPartitionResolver
{
    private readonly SpecializationRegistry? _specializationRegistry;

    public GroupPartitionResolver(SpecializationRegistry? specializationRegistry)
    {
        _specializationRegistry = specializationRegistry;
    }

    public GroupPartitionKey Resolve(in GroupForSearch groupForSearch)
    {
        if (groupForSearch.SubGroupName.IsEmpty)
        {
            return GroupPartitionKey.All;
        }
        var subGroup = new SubGroup(groupForSearch.SubGroupName.ToString());
        if (Specializations.TryResolveSpecialization(
                subGroup.Value,
                _specializationRegistry,
                out var specialization))
        {
            return new(SubGroup.All, specialization);
        }
        return new(subGroup, Specialization.All);
    }
}
