using System.Drawing;
using OpenTK.Mathematics;

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
        var red = Interpolate(
            from: a.R,
            to: b.R,
            t);
        var green = Interpolate(
            from: a.G,
            to: b.G,
            t);
        var blue = Interpolate(
            from: a.B,
            to: b.B,
            t);
        return Color.FromArgb(
            red: red,
            green: green,
            blue: blue);
    }

    private static int Interpolate(
        byte from,
        byte to,
        float t)
    {
        float componentStart = from;
        float componentEnd = to;
        var component = MathHelper.Lerp(start: componentStart, end: componentEnd, t: t);
        return (int) component;
    }
}
