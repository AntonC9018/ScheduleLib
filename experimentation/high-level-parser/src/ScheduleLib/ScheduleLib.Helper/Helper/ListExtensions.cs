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
    extension<T>(List<T> list)
    {
        public void ReplaceRange(int from, int len, T item)
        {
            if (len == 0)
            {
                list.Insert(from, item);
            }
            else
            {
                list[from] = item;
                list.RemoveRange(from + 1, len);
            }
        }
    }
}
