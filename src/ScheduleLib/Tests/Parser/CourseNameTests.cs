using System.Collections.Immutable;
using ScheduleLib.Builders;
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
        Assert.Equal(expected: course1, actual: course2);
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
        Assert.Equal(expected: course1, actual: course2);
    }

    [Fact]
    public void NodeJs()
    {
        var parserConfig = Config.CourseNameParser;

        var course1 = Parse(parserConfig, "Dezvoltarea de aplicatii server-side cu Node.js");
        var course2 = Parse(parserConfig, "Dezv. apl. server-side cu Node.js");
        Assert.Equal(expected: course1, actual: course2);
    }

    [Fact]
    public void LookupMatchesEveryNameAddedDuringImport()
    {
        var unifierConfig = CourseNameUnifierConfig.Create(new CourseNameParserConfig(new()), []);
        var builder = new ScheduleBuilder();
        builder.EnableLookupModule();
        var unifier = new CourseNameUnifierModule(unifierConfig);

        // "Networking" shares a prefix with "Network", but not with "Networks".
        var course = Import("Network Systems");
        var longerSpelling = Import("Networking Systems");
        Assert.Equal(expected: course, actual: longerSpelling);
        var schedule = builder.Build();
        var builtCourse = schedule.Get(course);
        Assert.Equal(expected: "Networking Systems", actual: builtCourse.FullName);

        var index = CourseNameIndex.Create(schedule.Courses, unifierConfig);
        var found = index.Find("Networks Systems".AsMemory(), new());
        Assert.Equal(expected: course, actual: found);

        CourseId Import(string name)
        {
            return unifier.FindOrAdd(new()
            {
                Schedule = builder,
                CourseName = name.AsMemory(),
            });
        }
    }

    [Fact]
    public void LookupPrefersTheEarliestCourseWhenNamesOfSeveralMatch()
    {
        var unifierConfig = CourseNameUnifierConfig.Create(new CourseNameParserConfig(new()), []);
        ImmutableArray<Course> courses =
        [
            new(["Networking Systems"]),
            new(["Networks Systems"]),
        ];
        var index = CourseNameIndex.Create(courses, unifierConfig);

        var found = index.Find("Network Systems".AsMemory(), new());

        Assert.Equal(expected: new CourseId(0), actual: found);
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

        var course1 = Parse(
            parserConfig,
            "Modelare, texturare si animatie 3D p/u jocuri",
            new()
            {
                IgnorePunctuation = true,
            });
        var course2 = Parse(parserConfig, "MTA3D");
        Assert.Equal(expected: course1, actual: course2);
    }

    [Fact]
    public void Python()
    {
        var parserConfig = Config.CourseNameUnifier;

        var course1 = Parse(
            parserConfig.ParserConfig,
            "PYTHON pentru aplicatii",
            new()
            {
                IgnorePunctuation = true,
            });
        var course2 = Parse(parserConfig.ParserConfig, "Python");
        var remapped1 = parserConfig.TryRemap(course1);
        Assert.Equal(expected: remapped1, actual: course2);
    }

    [Fact]
    public void DezvoltareaWebPHP()
    {
        var parserConfig = Config.CourseNameUnifier;

        var course1 = Parse(
            parserConfig.ParserConfig,
            "Dezv. web PHP",
            new()
            {
                IgnorePunctuation = true,
            });
        var course2 = Parse(parserConfig.ParserConfig, "Dezvoltare WEB avansată cu PHP");
        var remapped1 = parserConfig.TryRemap(course1);
        var remapped2 = parserConfig.TryRemap(course2);
        Assert.Equal(expected: remapped1, actual: remapped2);
    }

    [Fact]
    public void InitialsMustMatchFullyIfUsed()
    {
        var parserConfig = Config.CourseNameUnifier;

        var course1 = Parse(parserConfig.ParserConfig, "Psihologie");
        var course2 = Parse(parserConfig.ParserConfig, "PSI");
        Assert.NotEqual(expected: course1, actual: course2);
    }
}
