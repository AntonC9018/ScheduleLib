using Microsoft.Office.Interop.Word;

var inputPath = args[0];
var outputPath = args[1];
var app = new Application();
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
    inputDoc?.Close();
}
