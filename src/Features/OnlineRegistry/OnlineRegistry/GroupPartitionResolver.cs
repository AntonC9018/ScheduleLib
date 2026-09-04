namespace ScheduleLib.OnlineRegistry;

/// <summary>
/// Classifies the subgroup suffix of a registry group link (e.g. "Spring", "I", or empty)
/// into a <see cref="GroupPartitionKey"/>: empty means whole group; a known
/// specialization (built-in <see cref="Specializations"/> or custom registry) means
/// a specialization partition; anything else means a numeric subgroup (e.g. "I" is subgroup 1).
/// The custom specialization registry is injected via the constructor; pass
/// <c>null</c> when only built-in <see cref="Specializations"/> apply.
/// </summary>
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
            subGroup.Value, _specializationRegistry, out var specialization))
        {
            return new(SubGroup.All, specialization);
        }
        return new(subGroup, Specialization.All);
    }
}
