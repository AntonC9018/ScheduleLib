using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using AngleSharp.Text;
using ScheduleLib.Parsing;
using ScheduleLib.Parsing.GroupParser;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class HtmlTests
{
    public const string CreateLessonHtmlPath = "data/create_lesson.html";
    public const string GroupsHtmlPath = "data/groups.html";
    public const string LessonsHtmlPath = "data/lessons.html";
    public const string LessonsListHtmlPath = "data/lessons_list.html";
    public const string EmptyLessonsListHtmlPath = "data/lessons_list_empty.html";

    private static IDocument Load(string doc)
    {
        var htmlParser = new HtmlParser();
        var stream = File.OpenRead(doc);
        using var source = new TextSource(stream, Encoding.UTF8);
        var result = htmlParser.ParseDocument(source);
        return result;
    }

    [Fact]
    public async Task CourseLinksHtmlTest()
    {
        var doc = Load(LessonsHtmlPath);
        var courseNames = new List<string>();
        var result = HtmlSearch.ScanCoursesDocumentForLinks(new()
        {
            Document = doc,
            Semester = Semester.Sem1,
            FindCourse = courseName =>
            {
                courseNames.Add(courseName);
                return new(courseNames.Count);
            },
        }).ToArray();
        var resultWithNames = result.Zip(courseNames, (a, b) => new
        {
            Url = a.Url.ToString(),
            Id = a.CourseId.Id,
            CourseName = b,
        });
        await Verify(resultWithNames);
    }

    [Fact]
    public async Task CourseAddButtonHtmlTest()
    {
        var doc = Load(LessonsListHtmlPath);
        var uri = HtmlSearch.ScanForLessonAddLink(doc);
        await Verify(uri.ToString());
    }

    [Fact]
    public async Task GroupLinksHtmlTest()
    {
        var doc = Load(GroupsHtmlPath);
        var groupParseContext = GroupParseContext.Create(new()
        {
            CurrentStudyYear = 2025,
        });

        List<GroupForSearch> groups = new();
        var result = HtmlSearch.ScanGroupsDocumentForLinks(new()
        {
            Document = doc,
            GroupParseContext = groupParseContext,
            SearchGroupId = (in GroupForSearch g) =>
            {
                groups.Add(g);
                return new(groups.Count);
            },
        }).ToArray();

        var mapped = result.Zip(groups, (a, b) => new
        {
            Uri = a.Uri.ToString(),
            b.GroupNumber,
            SubGroup = b.SubGroupName.ToString(),
            FacultyName = b.FacultyName.ToString(),
        });
        await Verify(mapped);
    }

    private sealed class ScanLessonVerifyModel
    {
        public required RemoteLessonInstance[] Lessons { get; init; }
        public required HtmlStudent[] Students { get; init; }
    }

    private static async ValueTask<ScanLessonVerifyModel> Scan(string docPath)
    {
        var doc = Load(docPath);
        var addDoc = Load(CreateLessonHtmlPath);
        var result = await HtmlSearch.ScanLessonsDocumentForLessonInstances(new()
        {
            Document = doc,
            ErrorHandler = new LessonFailErrorHandler(),
            GetAddLessonDocument = () => Task.FromResult(addDoc),
        });
        return new()
        {
            Lessons = result.Lessons.ToArray(),
            Students = result.Students,
        };
    }

    [Fact]
    public async Task LessonDataHtmlTest()
    {
        var result = await Scan(LessonsListHtmlPath);
        await Verify(result);
    }

    [Fact]
    public async Task EmptyLessonDataHtmlTest()
    {
        var result = await Scan(EmptyLessonsListHtmlPath);
        await Verify(result);
    }

    [Fact]
    public void Test()
    {
        NameParts<string?> N(string s)
        {
            var ret = new NameParts<string?>();
            ret[0] = s;
            return ret;
        }

        var a = new Name()
        {
            FirstName = N("Anton"),
            LastName = N("Curmanschii"),
        };
        var b = new Name()
        {
            FirstName = N("ANTON"),
            LastName = N("CURMANSCHII"),
        };
        Assert.Equal(a, b, Name_IgnoreDiacritics_EqualityComparer.Instance);
        Assert.Equal(Name_IgnoreDiacritics_EqualityComparer.Instance.GetHashCode(a), Name_IgnoreDiacritics_EqualityComparer.Instance.GetHashCode(b));
    }
}

file sealed class LessonFailErrorHandler : IRegistryLessonParserErrorHandler
{
    public void CustomLessonType(ReadOnlySpan<char> ch)
    {
        throw new NotImplementedException();
    }
}
