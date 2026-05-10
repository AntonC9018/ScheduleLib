namespace ScheduleLib.Helper.Helper;

public static class CollectionExtensions
{
    extension<T>(IReadOnlyCollection<T> self)
    {
        public bool IsEmpty => self.Count == 0;
    }
}
