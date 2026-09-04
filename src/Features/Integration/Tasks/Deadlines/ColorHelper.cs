using System.Drawing;

namespace ScheduleLib.Application.Core;

internal static class ColorHelper
{
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0, 1);
        int r = (int) (a.R + (b.R - a.R) * t);
        int g = (int) (a.G + (b.G - a.G) * t);
        int b2 = (int) (a.B + (b.B - a.B) * t);
        return Color.FromArgb(r, g, b2);
    }
}
