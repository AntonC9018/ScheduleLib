using System.Runtime.CompilerServices;

namespace ScheduleLib.OnlineRegistry;

[InlineArray((int) LessonType.Count)]
public struct ValueForEachLessonType<T>
{
    private T _items;
}
