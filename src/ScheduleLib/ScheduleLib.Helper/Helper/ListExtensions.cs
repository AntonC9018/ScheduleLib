namespace ScheduleLib.Helper.Helper;

public static class ListExtensions
{
    extension<T>(IList<T> list)
    {
        public bool TryAdd(T value)
        {
            if (list.Contains(value))
            {
                return false;
            }
            list.Add(value);
            return true;
        }
    }
}
