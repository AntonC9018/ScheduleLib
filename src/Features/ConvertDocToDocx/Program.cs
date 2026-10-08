using Microsoft.Office.Interop.Word;
using System.Runtime.InteropServices;
using ConvertDocToDocx;

var inputPath = args[0];
var outputPath = args[1];
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
app.Visible = false;
app.DisplayAlerts = WdAlertLevel.wdAlertsNone;

Document? inputDoc = null;
try
{
    object inputObj = inputPath;
    inputDoc = app.Documents.Open(ref inputObj);
    inputDoc.SaveAs2(
        FileName: outputPath,
        FileFormat: WdSaveFormat.wdFormatXMLDocument);
}
finally
{
    inputDoc?.Close(WdSaveOptions.wdDoNotSaveChanges);
    app.Quit(WdSaveOptions.wdDoNotSaveChanges);
}

return 0;
