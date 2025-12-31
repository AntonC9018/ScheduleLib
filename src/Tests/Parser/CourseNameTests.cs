using ScheduleLib.Parsing.CourseName;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.ParserTests;

public sealed class CourseNameTests
{
    [Fact]
    public void ProgrammingWordsAreOptionalToIgnore()
    {
        var parserConfig = new CourseNameParserConfig(new()
        {
            ProgrammingLanguages = ["Python"],
            IgnoredProgrammingRelatedWords = ["Programare"],
            MinUsefulWordLength = 3,
        });

        var course1 = parserConfig.Parse("Programarea jocurilor in Python");
        var course2 = parserConfig.Parse("PJ Python");
        Assert.Equal(course1, course2);
    }

    [Fact]
    public void ExploresAllOptions()
    {
        var parserConfig = new CourseNameParserConfig(new()
        {
            IgnoredFullWords = ["modele"],
            MinUsefulWordLength = 3,
        });

        // The problem was that "de" got consumed by "design".
        var course1 = parserConfig.Parse("Modele de design software");
        var course2 = parserConfig.Parse("Design Soft");
        Assert.Equal(course1, course2);
    }

    [Fact]
    public void NodeJs()
    {
        var parserConfig = Config.CourseNameParser;

        var course1 = parserConfig.Parse("Dezvoltarea de aplicatii server-side cu Node.js");
        var course2 = parserConfig.Parse("Dezv. apl. server-side cu Node.js");
        Assert.Equal(course1, course2);
    }
}
