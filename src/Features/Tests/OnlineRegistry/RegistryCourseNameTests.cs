using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryCourseNameTests
{
    [Theory]
    [InlineData("Course(BaDe)",
        "Course",
        "BaDe")]
    [InlineData("Course (BaDe&FrDe)",
        "Course",
        "BaDe,FrDe")]
    [InlineData("Course(Unknown)",
        "Course(Unknown)",
        "")]
    public void ElectiveClassificationsAreParsedSeparately(
        string input,
        string name,
        string classifications)
    {
        var parsed = RegistryCourseNameLookup.Parse(input);
        Assert.Equal(expected: name, actual: parsed.Name);
        string expected2 = classifications;
        string actual2 = string.Join(',', parsed.ElectiveClassifications);
        Assert.Equal(expected: expected2, actual: actual2);
    }

    [Fact]
    public void NormalizationMatchesObservedPunctuationAndSpacingError()
    {
        var lookup = Lookup("Designul UI/U X");
        var expected2 = new CourseId(0);
        CourseId? expected3 = expected2;
        var actual2 = RegistryCourseNameLookup.Find(lookup,
            "Design-ul UI/UX",
            new Dictionary<string, string>());
        Assert.Equal(expected: expected3, actual: actual2);
    }

    [Fact]
    public void LongNameTyposMatchOnlyOneCourseWithinSmallRelativeDistance()
    {
        var lookup = Lookup("Introduction to networking");
        var expected2 = new CourseId(0);
        CourseId? expected3 = expected2;
        var actual2 = RegistryCourseNameLookup.Find(lookup,
            "Introduction to netwroking",
            new Dictionary<string, string>());
        Assert.Equal(expected: expected3, actual: actual2);
        var value = RegistryCourseNameLookup.Find(lookup,
            "Introduction to netwroking",
            new Dictionary<string, string>(),
            maxDistance: 0);
        Assert.Null(value);
        var value2 = RegistryCourseNameLookup.Find(lookup,
            "Introduction to netwroking(Unknown)",
            new Dictionary<string, string>());
        Assert.Null(value2);
        var ambiguous = Lookup("Introduction to networking", "Introduction to networkinb");
        var value3 = RegistryCourseNameLookup.Find(ambiguous,
            "Introduction to networkinx",
            new Dictionary<string, string>());
        Assert.Null(value3);
        var lookup2 = Lookup("HTML");
        string registryName = "HTLM";
        IReadOnlyDictionary<string, string> courseNames = new Dictionary<string, string>();
        var value4 = RegistryCourseNameLookup.Find(lookup2,
            registryName,
            courseNames);
        Assert.Null(value4);
    }

    [Fact]
    public void AliasOnBaseNameHandlesElectiveSuffixWithoutFuzzySemanticGuess()
    {
        var lookup = Lookup("Server-side");
        var aliases = new Dictionary<string, string>
        {
            ["Application development with Node.js"] = "Server-side",
        };
        var expected2 = new CourseId(0);
        CourseId? expected3 = expected2;
        var actual2 = RegistryCourseNameLookup.Find(lookup,
            "Application development with Node.js(BaDe)",
            aliases);
        Assert.Equal(expected: expected3, actual: actual2);
    }

    private static LookupFacade Lookup(params string[] names)
    {
        var builder = new ScheduleBuilder();
        builder.EnableLookupModule();
        foreach (var name in names)
        {
            builder.Course(name);
        }

        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        unifier.Refresh(builder);
        return builder.Lookup(unifier);
    }

    [Fact]
    public void ExplicitAliasOverridesConflictingAutomaticCourseMatch()
    {
        var builder = new ScheduleBuilder();
        builder.EnableLookupModule();
        var automatic = builder.Course("Advanced web");
        var intended = builder.Course("Web fundamentals");
        var unifier = new CourseNameUnifierModule(Config.CourseNameUnifier);
        unifier.Refresh(builder);
        var lookup = builder.Lookup(unifier);

        CourseId? expected2 = automatic;
        var actual2 = RegistryCourseNameLookup.Find(lookup,
            "Advanced web",
            new Dictionary<string, string>());
        Assert.Equal(expected: expected2, actual: actual2);
        Assert.Equal(expected: intended,
            actual: RegistryCourseNameLookup.Find(lookup,
                "Advanced web",
                new Dictionary<string, string>
                {
                    ["Advanced web"] = "Web fundamentals",
                }));
        Assert.Null(RegistryCourseNameLookup.Find(lookup,
            "Advanced web",
            new Dictionary<string, string>
            {
                ["Advanced web"] = "Missing course",
            }));
    }

    [Fact]
    public void SuppliedAliasMapReplacesEarlierMapAndOmittedMapInherits()
    {
        var services = new ServiceCollection();
        RegistryConfig.Register(services);
        using var provider = services.BuildServiceProvider();
        var merger = provider.GetRequiredService<IMerger<RegistryConfig>>();
        var earlier =
            new RegistryConfig
            {
                CourseNames = new Dictionary<string, string>
                {
                    ["Old alias"] = "Old course",
                },
            };
        var later = new RegistryConfig
        {
            CourseNames = new Dictionary<string, string>
            {
                ["New alias"] = "New course",
            },
        };

        var replacement = merger.Merge(@from: later, @into: earlier);
        Assert.Single(replacement.CourseNames!);
        Assert.Equal(expected: "New course", actual: replacement.CourseNames!["New alias"]);
        var inherited = merger.Merge(@from: new RegistryConfig(),
            @into: new RegistryConfig
            {
                CourseNames = new Dictionary<string, string>
                {
                    ["Old alias"] = "Old course",
                },
            });
        Assert.Equal(expected: "Old course", actual: inherited.CourseNames!["Old alias"]);
    }
}
