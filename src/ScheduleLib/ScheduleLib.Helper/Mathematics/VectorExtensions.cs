using OpenTK.Mathematics;

namespace ScheduleLib.Mathematics;

public static class VectorExtensions
{
    public static Vector2d Midpoint(this Vector2d start, Vector2d end)
    {
        return (start + end) * 0.5;
    }
}
