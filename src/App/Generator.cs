namespace App;

public readonly struct GroupAccessor
{
    private readonly Schedule _schedule;

    public GroupAccessor(Schedule schedule, GroupId id)
    {
        _schedule = schedule;
        Id = id;
    }

    public GroupId Id { get; }
    public Group Value => _schedule.Groups[Id.Value];
}

public static class AllGroupsGenerator
{
    public struct Filter
    {
        public required QualificationType QualificationType;
        public required int Grade;
    }

    public static void GeneratePdfZi(Schedule schedule, Filter filter)
    {
        var maxGroupsInRow = 5;
        var groups = GroupsWithRegularLessons();


        // Find out which groups have regular lessons.
        IEnumerable<GroupId> GroupsWithRegularLessons()
        {
            HashSet<GroupId> groups = new();
            foreach (var regularLesson in schedule.RegularLessons)
            {
                var groupId = regularLesson.Lesson.Group;
                var g = new GroupAccessor(schedule, groupId);
                if (g.Value.QualificationType != filter.QualificationType)
                {
                    continue;
                }
                if (g.Value.Grade == filter.Grade)
                {
                    continue;
                }
                groups.Add(groupId);
            }
            var ret = groups.ToArray();
            // Sorting by name is fine here.
            Array.Sort(ret);
        }
    }
}