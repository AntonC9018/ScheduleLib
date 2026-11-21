using System.Diagnostics;

namespace ScheduleLib.Helper;

public static class PathHelper
{
    public static string WithExtension(ReadOnlySpan<char> file, ReadOnlySpan<char> extension)
    {
        Debug.Assert(extension.Length == 0 || extension[0] == '.');
        int lastSeparator = file.LastIndexOfAny(value0: '/', value1: '\\');
        int fileNameStart = lastSeparator + 1;
        int indexOfDot = file.LastIndexOf('.');
        if (indexOfDot <= fileNameStart)
        {
            return string.Concat(str0: file, str1: extension);
        }
        return string.Concat(str0: file[..indexOfDot], str1: extension);
    }
}
