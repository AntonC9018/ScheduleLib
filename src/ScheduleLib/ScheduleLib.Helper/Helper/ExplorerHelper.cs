using System.Runtime.InteropServices;

namespace ScheduleLib.Helper;

public static class ExplorerHelper
{
    public static bool TryOpenExplorerAndSelectFile(string file)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ExplorerHelper.OpenFolderAndSelectFile(file);
            return true;
        }
        return false;
    }

    // https://stackoverflow.com/a/14601675
    public static void OpenFolderAndSelectFile(string filePath)
    {
        filePath = Path.GetFullPath(filePath);
        ArgumentNullException.ThrowIfNull(filePath);
        IntPtr pidl = ILCreateFromPathW(filePath);
        SHOpenFolderAndSelectItems(pidlFolder: pidl, cild: 0, apidl: IntPtr.Zero, dwFlags: 0);
        ILFree(pidl);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, int cild, IntPtr apidl, int dwFlags);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);
}
