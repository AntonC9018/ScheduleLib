using System.Diagnostics;

namespace ScheduleLib.Helper;

public static class PathHelper
{
    public static string WithExtension(ReadOnlySpan<char> file, ReadOnlySpan<char> extension)
    {
        Debug.Assert(extension.Length == 0 || extension[0] == '.');
        int indexOfDot = file.LastIndexOf('.');
        if (indexOfDot < 0)
        {
            return string.Concat(file, extension);
        }
        return string.Concat(file[.. indexOfDot], extension);
    }
}
