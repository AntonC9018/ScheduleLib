using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Xml;
using AngleSharp.Xml.Dom;
using ScheduleLib.Helper;

namespace ScheduleLib.Scraping.Common;

public static class DebugHelper
{
    public static async Task SaveForDebug(this IDocument doc)
    {
        string FileName(string ext)
        {
            return $"temp.{ext}";
        }
        FileStream Open(string fileName)
        {
            return new FileStream(fileName, FileMode.Create, FileAccess.Write);
        }
        string Ext()
        {
            return doc switch
            {
                IXmlDocument => "xml",
                IHtmlDocument => "html",
                _ => throw new InvalidOperationException(),
            };
        }
        string ext = Ext();
        string fileName = FileName(ext);
        await using var outputStream = Open(fileName);

        switch (doc)
        {
            case IXmlDocument xml:
            {
                await using var textWriter = new StreamWriter(outputStream);
                xml.ToXml(textWriter);
                break;
            }
            case IHtmlDocument html:
            {
                await html.ToHtmlAsync(outputStream);
                break;
            }
        }
        ExplorerHelper.TryOpenExplorerAndSelectFile(fileName);
    }
}
