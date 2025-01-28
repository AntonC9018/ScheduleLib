namespace ReaderApp;

using System.Runtime.InteropServices;

public static class ExplorerHelper
{
    // https://stackoverflow.com/a/14601675
    public static void OpenFolderAndSelectFile(string filePath)
    {
        ArgumentNullException.ThrowIfNull(filePath);
        IntPtr pidl = ILCreateFromPathW(filePath);
        SHOpenFolderAndSelectItems(pidl, 0, IntPtr.Zero, 0);
        ILFree(pidl);
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr ILCreateFromPathW(string pszPath);

    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidlFolder, int cild, IntPtr apidl, int dwFlags);

    [DllImport("shell32.dll")]
    private static extern void ILFree(IntPtr pidl);
}
