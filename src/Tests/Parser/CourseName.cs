using ScheduleLib.Parsing.CourseName;

namespace App.Tests;

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
}
