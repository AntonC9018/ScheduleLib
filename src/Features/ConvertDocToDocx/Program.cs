using Microsoft.Office.Interop.Word;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConvertDocToDocx;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 4) return 1;
        var inputPath = args[0];
        var outputPath = args[1];
        var nonce = args[2];
        var cancellationPath = args[3];
        if (File.Exists(cancellationPath)) return 130;
        Application app;
        try
        {
            app = new Application();
        }
        catch (COMException exception) when (WordConverterProtocol.IsMissingWordActivation(exception.HResult))
        {
            Console.Error.WriteLine(WordConverterProtocol.MissingWordMessage);
            return WordConverterProtocol.MissingWordExitCode;
        }

        Document? inputDoc = null;
        Document? identityDoc = null;
        try
        {
            // HWND belongs to this COM application; the caller additionally verifies that
            // the process did not exist before conversion and pins its native handle.
            identityDoc = app.Documents.Add();
            NativeMethods.GetWindowThreadProcessId(new IntPtr(identityDoc.ActiveWindow.Hwnd), out var pid);
            using (var word = Process.GetProcessById((int)pid))
            {
                Console.WriteLine(WordConverterProtocol.OwnershipMessage(nonce, word.Id, word.StartTime.ToUniversalTime().Ticks));
                Console.Out.Flush();
            }
            identityDoc.Close(WdSaveOptions.wdDoNotSaveChanges);
            identityDoc = null;
            app.Visible = false;
            app.DisplayAlerts = WdAlertLevel.wdAlertsNone;
            if (File.Exists(cancellationPath)) return 130;
            object inputObj = inputPath;
            inputDoc = app.Documents.Open(ref inputObj);
            if (File.Exists(cancellationPath)) return 130;
            inputDoc.SaveAs2(FileName: outputPath, FileFormat: WdSaveFormat.wdFormatXMLDocument);
            return File.Exists(cancellationPath) ? 130 : 0;
        }
        finally
        {
            try { inputDoc?.Close(WdSaveOptions.wdDoNotSaveChanges); }
            finally
            {
                try { identityDoc?.Close(WdSaveOptions.wdDoNotSaveChanges); }
                finally { app.Quit(WdSaveOptions.wdDoNotSaveChanges); }
            }
        }
    }
}

internal static class NativeMethods
{
    [DllImport("user32.dll")]
    internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
