using System.Drawing;

namespace ScheduleLib.Application.Core;

internal static class ColorHelper
{
    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(value: t, min: 0, max: 1);
        float byteValue = a.R;
        int int32 = b.R - a.R;
        float single = (int32) * t;
        float single2 = byteValue + single;
        int r = (int) (single2);
        float byteValue2 = a.G;
        int int322 = b.G - a.G;
        float single3 = (int322) * t;
        float single4 = byteValue2 + single3;
        int g = (int) (single4);
        float byteValue3 = a.B;
        int int323 = b.B - a.B;
        float single5 = (int323) * t;
        float single6 = byteValue3 + single5;
        int b2 = (int) (single6);
        return Color.FromArgb(red: r, green: g, blue: b2);
    }
}
