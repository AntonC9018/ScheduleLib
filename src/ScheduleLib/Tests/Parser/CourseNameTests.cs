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

        var course1 = Parse(parserConfig, "Programarea jocurilor in Python");
        var course2 = Parse(parserConfig, "PJ Python");
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
        var course1 = Parse(parserConfig, "Modele de design software");
        var course2 = Parse(parserConfig, "Design Soft");
        Assert.Equal(course1, course2);
    }

    [Fact]
    public void NodeJs()
    {
        var parserConfig = Config.CourseNameParser;

        var course1 = Parse(parserConfig, "Dezvoltarea de aplicatii server-side cu Node.js");
        var course2 = Parse(parserConfig, "Dezv. apl. server-side cu Node.js");
        Assert.Equal(course1, course2);
    }

    private ParsedCourseName Parse(
        CourseNameParserConfig config,
        string str,
        CourseNameParseOptions options = default)
    {
        return config.Parse(str.AsMemory(), options);
    }

    [Fact]
    public void MTA32()
    {
        var parserConfig = Config.CourseNameParser;

        var course1 = Parse(parserConfig, "Modelare, texturare si animatie 3D p/u jocuri", new()
        {
            IgnorePunctuation = true,
        });
        var course2 = Parse(parserConfig, "MTA3D");
        Assert.Equal(course1, course2);
    }

    [Fact]
    public void Python()
    {
        var parserConfig = Config.CourseNameUnifier;

        var course1 = Parse(parserConfig.ParserConfig, "PYTHON pentru aplicatii", new()
        {
            IgnorePunctuation = true,
        });
        var course2 = Parse(parserConfig.ParserConfig, "Python");
        var remapped1 = parserConfig.TryRemap(course1);
        Assert.Equal(remapped1, course2);
    }

    [Fact]
    public void DezvoltareaWebPHP()
    {
        var parserConfig = Config.CourseNameUnifier;

        var course1 = Parse(parserConfig.ParserConfig, "Dezv. web PHP", new()
        {
            IgnorePunctuation = true,
        });
        var course2 = Parse(parserConfig.ParserConfig, "Dezvoltare WEB avansată cu PHP");
        var remapped1 = parserConfig.TryRemap(course1);
        var remapped2 = parserConfig.TryRemap(course2);
        Assert.Equal(remapped1, remapped2);
    }
}
