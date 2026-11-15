namespace ScheduleLib.OnlineRegistry;

/// <summary>A registry group list that lessons are routed to, with the lessons selected for it.</summary>
internal readonly record struct RegistryLessonDestination(GroupLink Group, RegistryLessonSelection Selection);
