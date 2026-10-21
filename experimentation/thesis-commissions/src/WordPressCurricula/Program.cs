using System.Diagnostics;
using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using ScheduleLib;
using ScheduleLib.Parsing;
using WordPressCurricula;
using static ScheduleLib.UnreachableHelper;

#pragma warning disable CS8321 // Local function is declared but never used

List<CurriculumInfo> curricula;
{
    var curriculaDir = Path.GetFullPath("data/Curricula");
    curricula = Helper.ParseCurriculumFiles(curriculaDir);
    foreach (var c in curricula)
    {
        Console.WriteLine($"File: {c.FilePath}, Faculty: {c.Faculty}, Program: {c.Program}, Code: {c.Curriculum.Code}, Name: {c.Curriculum.Name}");
    }
}

foreach (var curriculum in curricula)
{
    var newFileName = CurriculumName(curriculum);
    var outputDirectory = "data/CurriculaFlat";
    Directory.CreateDirectory(outputDirectory);
    var outputPath = Path.Join(outputDirectory, newFileName);
    if (!File.Exists(outputPath))
    {
        File.Copy(curriculum.FilePath, outputPath, overwrite: false);
    }
}

foreach (var sourceFilePath in Directory.EnumerateFiles("data/SourceFiles"))
{
    IHtmlDocument dom;
    {
        var sourceHtml = await File.ReadAllTextAsync(sourceFilePath);
        var parser = new HtmlParser();
        dom = parser.ParseDocument("<html><body></body></html>");
        var nodes = parser.ParseFragment(sourceHtml, dom.Body ?? throw Unreachable());

        foreach (var node in nodes.ToArray())
        {
            foreach (var li in node.DescendantsAndSelf<IHtmlListItemElement>())
            {
                INode parent = li;
                if (parent.NextSibling is null
                    || parent.NextSibling is IText)
                {
                    parent = parent.Parent!;
                }

                var next = parent.NextSibling;
                while (next != null)
                {
                    var current = next.NextSibling;
                    next = next.NextSibling;

                    if (current is IText text
                        && text.TextContent == "\n")
                    {
                        continue;
                    }
                    if (current is IHtmlSpanElement or IText)
                    {
                        li.AppendChild(current);
                    }
                }
            }
        }

        foreach (var node in nodes.ToArray())
        {
            dom.Body!.AppendChild(node);
        }
    }

    var curriculaInHtml = ParseLinks(dom.Body!.Children);
    foreach (var htmlCurriculum in curriculaInHtml)
    {
        CurriculumInfo[] foundCurriculum = [];

        if (htmlCurriculum.Code.AsCode() is { } code)
        {
            var matches = FindByCode(code).ToArray();

            foundCurriculum = matches;
        }
        else if (htmlCurriculum.Name is { } name && name != "")
        {
            var matches = FindByName(name).ToArray();
            foundCurriculum = matches;
        }
        else
        {
            Debug.Fail("No code or name");
        }

        Console.WriteLine($"FACULTATE: {htmlCurriculum.FacultyShitName.FullName}");

        if (foundCurriculum.Length > 1)
        {
            Console.WriteLine($"MULTIPLE MATCHES: '{htmlCurriculum.Code} {htmlCurriculum.Name}'");
        }
        else if (foundCurriculum.Length == 0)
        {
            Console.WriteLine($"NOT FOUND: '{htmlCurriculum.Code} {htmlCurriculum.Name}'");
        }
        else
        {
            Console.WriteLine($"FOUND: '{htmlCurriculum.Code} {htmlCurriculum.Name}'");

            var newFileName = CurriculumName(foundCurriculum[0]);
            // wrap htmlCurriculum.DomLink into an <a> tag
            var a = dom.CreateElement("a");
            a.SetAttribute("href", WordPressLink(newFileName));
            foreach (var child in htmlCurriculum.DomLink.ChildNodes.ToArray())
            {
                a.AppendChild(child);
            }
            htmlCurriculum.DomLink.AppendChild(a);
        }

        if (foundCurriculum.Length > 1)
        {
            foreach (var match in foundCurriculum)
            {
                var newFileName = CurriculumName(match);
                var link = WordPressLink(newFileName);
                Console.WriteLine($"{link}");
            }
            Console.WriteLine();
        }
    }

    // output html
    {
        var outputDirectory = "data/output";
        Directory.CreateDirectory(outputDirectory);
        var outputFilePath = Path.Join("data/output", Path.GetFileName(sourceFilePath));
        using var writer = new StreamWriter(outputFilePath, append: false, Encoding.UTF8);
        dom.ToHtml(writer, new MinifyMarkupFormatter());
    }
    continue;

    IEnumerable<CurriculumInfo> FindByCode(Code code)
    {
        foreach (var c in curricula)
        {
            if (c.Curriculum.Code.AsCode() is not { } otherCode)
            {
                continue;
            }
            if (otherCode.Equals(code))
            {
                yield return c;
            }
        }
    }

    IEnumerable<CurriculumInfo> FindByName(string name)
    {
        const int maxPartsToConsider = 2;

        Debug.Assert(name.Length != 0);
        var nameParts = name.Split(' ');

        int size = int.Min(nameParts.Length, maxPartsToConsider);

        foreach (var c in curricula)
        {
            var otherNameParts = c.Curriculum.Name.Split(' ');
            if (otherNameParts.Length < size)
            {
                continue;
            }
            if (!Compare())
            {
                continue;
            }
            yield return c;
            continue;

            bool Compare()
            {
                for (int i = 0; i < size; i++)
                {
                    var a = nameParts[i];
                    var b = otherNameParts[i];
                    var longer = a;
                    var shorter = b;
                    if (longer.Length < shorter.Length)
                    {
                        (longer, shorter) = (shorter, longer);
                    }
                    if (!IgnoreDiacriticsAndCaseComparer.Instance.StartsWith(longer, shorter))
                    {
                        return false;
                    }
                }
                return true;
            }
        }
    }

}

return;

static List<CurriculumListItem> ParseLinks(IEnumerable<INode> nodes)
{
    var state = new FacultyShitParsingState();
    var sb = new StringBuilder();
    using var nodeE = nodes.GetEnumerator();

    while (nodeE.MoveNext())
    {
        var node = nodeE.Current;
        bool isSectionHeader = node.NodeName.Equals(TagNames.B, StringComparison.OrdinalIgnoreCase);
        if (!isSectionHeader && state.Section is null)
        {
            throw new InvalidOperationException("Expected first element to be bold");
        }

        if (isSectionHeader)
        {
            if (!node.HasChildNodes)
            {
                continue;
            }

            var facultyNameThing = node.TextContent;
            var shitName = GetShitName(facultyNameThing);
            Console.WriteLine(shitName.ShortenedName);

            state.SectionIndex++;
            state.Section = shitName;
            continue;
        }

        ProcessMaybeListItem(node);
    }

    return state.Items;

    void ProcessMaybeListItem(INode maybeList)
    {
        switch (maybeList)
        {
            case IHtmlListItemElement listItem:
            {
                IEnumerable<HtmlTextWithLink> TextWithLinks()
                {
                    foreach (var child in listItem.Children)
                    {
                        var text = child.TextContent;
                        yield return new(text, child);
                    }
                }

                var c = Helper.ParseCurriculumInHtml(new()
                {
                    Source = TextWithLinks(),
                });
                state.Items.Add(new()
                {
                    // Code = c.Code,
                    Code = default,
                    Name = c.Name,
                    DomLink = listItem,
                    FacultyShitName = state.Section!,
                });
                break;
            }
            default:
            {
                foreach (var child in maybeList.ChildNodes)
                {
                    ProcessMaybeListItem(child);
                }
                break;
            }
        }

    }

    FacultyShitName GetShitName(string facultyNameThing1)
    {
        NormalizeLabel(sb, facultyNameThing1);
        facultyNameThing1 = sb.ToStringAndClear();

        var parser = new Parser(facultyNameThing1);
        if (parser.SkipWhitespace().EndOfInput)
        {
            throw new InvalidOperationException("Empty section");
        }

        bool isFaculty = false;
        if (parser.ConsumeExactString("FACULTATEA"))
        {
            isFaculty = true;
        }
        else if (parser.ConsumeExactString("INSTITUTUL"))
        {
        }
        else
        {
            throw new InvalidOperationException($"Something is wrong -- no 'facultatea de' in front. Index: {state.SectionIndex}");
        }

        parser.ConsumeExactString(" DE");

        if (!parser.SkipWhitespace().SkippedAny)
        {
            throw new InvalidOperationException("Expected spaces after 'facultatea de'");
        }

        return new()
        {
            FullName = facultyNameThing1,
            IsFaculty = isFaculty,
            ShortenedName = parser.SourceUntilEnd().ToString(),
        };
    }

}

static void NormalizeLabel(StringBuilder sb, ReadOnlySpan<char> s)
{
    bool justAddedSpace = false;
    Debug.Assert(sb.Length == 0);
    foreach (var ch in s)
    {
        if (char.IsWhiteSpace(ch))
        {
            if (justAddedSpace)
            {
                continue;
            }
            if (sb.Length == 0)
            {
                continue;
            }
            sb.Append(' ');
            justAddedSpace = true;
        }
        else
        {
            sb.Append(ch);
            justAddedSpace = false;
        }
    }
}

static string WordPressLink(string newFileName)
{
    return $"https://usm.md/wp-content/uploads/{newFileName}";
}
static string CurriculumName(CurriculumInfo c)
{
    var curriculaPath = Path.GetFullPath("data/Curricula");
    var pathRelativeToCurricula = c.FilePath[(curriculaPath.Length + 1) ..];
    var newFileName = pathRelativeToCurricula.Replace('\\', '-');
    var t = newFileName.Replace(" ", "-");
    return t;
}

public sealed class CurriculumInfo
{
    public required string FilePath;
    public required string Faculty;
    public required string Program;
    public required Curriculum Curriculum;
}

public sealed class FacultyShitName
{
    public required bool IsFaculty;
    public required string FullName;
    public required string ShortenedName;
}

public struct FacultyShitParsingState()
{
    public int SectionIndex;
    public FacultyShitName? Section;
    public List<CurriculumListItem> Items = new();
}

public sealed class CurriculumListItem
{
    public required FacultyShitName FacultyShitName;
    public required MaybeCode Code;
    public required string? Name;
    public required IHtmlListItemElement DomLink;
}
