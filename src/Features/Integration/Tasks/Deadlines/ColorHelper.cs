using System.Drawing;

namespace ScheduleLib.Application.Core;

internal static class ColorHelper
{
    public static Color Lerp(
        Color a,
        Color b,
        float t)
    {
        t = Math.Clamp(
            value: t,
            min: 0,
            max: 1);
        return Color.FromArgb(
            red: Interpolate(a.R, b.R, t),
            green: Interpolate(a.G, b.G, t),
            blue: Interpolate(a.B, b.B, t));
    }

    private static int Interpolate(byte from, byte to, float t)
    {
        return (int) (from + (to - from) * t);
    }
}
