using Anton.LayeredData;
using Microsoft.Extensions.DependencyInjection;
using ScheduleLib.Builders;
using ScheduleLib.Parsing.CourseName;
using ScheduleLib.ScheduleDefaults;

namespace ScheduleLib.OnlineRegistry.Tests;

public sealed class RegistryCourseNameTests
{
    [Theory]
    [InlineData("Course(BaDe)", "Course", "BaDe")]
    [InlineData("Course (BaDe&FrDe)", "Course", "BaDe,FrDe")]
    [InlineData("Course(Unknown)", "Course(Unknown)", "")]
    public void ElectiveClassificationsAreParsedSeparately(string input, string name, string classifications)
    {
        var parsed = RegistryCourseNameLookup.Parse(input);
        Assert.Equal(name, parsed.Name);
        Assert.Equal(classifications, string.Join(',', parsed.ElectiveClassifications));
    }

    [Fact]
    public void NormalizationMatchesObservedPunctuationAndSpacingError()
    {
        var lookup = Lookup("Designul UI/U X");
        Assert.Equal(new CourseId(0), RegistryCourseNameLookup.Find(lookup, "Design-ul UI/UX", new Dictionary<string, string>()));
    }

    [Fact]
    public void LongNameTyposMatchOnlyOneCourseWithinSmallRelativeDistance()
    {
        var lookup = Lookup("Introduction to networking");
        Assert.Equal(new CourseId(0), RegistryCourseNameLookup.Find(lookup, "Introduction to netwroking", new Dictionary<string, string>()));
        Assert.Null(RegistryCourseNameLookup.Find(lookup, "Introduction to netwroking", new Dictionary<string, string>(), maxDistance: 0));
        Assert.Null(RegistryCourseNameLookup.Find(lookup, "Introduction to netwroking(Unknown)", new Dictionary<string, string>()));
        var ambiguous = Lookup("Introduction to networking", "Introduction to networkinb");
        Assert.Null(RegistryCourseNameLookup.Find(ambiguous, "Introduction to networkinx", new Dictionary<string, string>()));
        Assert.Null(RegistryCourseNameLookup.Find(Lookup("HTML"), "HTLM", new Dictionary<string, string>()));
    }

    [Fact]
    public void AliasOnBaseNameHandlesElectiveSuffixWithoutFuzzySemanticGuess()
    {
        var lookup = Lookup("Server-side");
        var aliases = new Dictionary<string, string> { ["Application development with Node.js"] = "Server-side" };
        Assert.Equal(new CourseId(0), RegistryCourseNameLookup.Find(lookup, "Application development with Node.js(BaDe)", aliases));
    }

    private static LookupFacade Lookup(params string[] names)
    {
        var builder = new ScheduleBuilder();
        builder.EnableLookupModule();
        foreach (var name in names) builder.Course(name);
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

        Assert.Equal(automatic, RegistryCourseNameLookup.Find(lookup, "Advanced web", new Dictionary<string, string>()));
        Assert.Equal(intended, RegistryCourseNameLookup.Find(lookup, "Advanced web",
            new Dictionary<string, string> { ["Advanced web"] = "Web fundamentals" }));
        Assert.Null(RegistryCourseNameLookup.Find(lookup, "Advanced web",
            new Dictionary<string, string> { ["Advanced web"] = "Missing course" }));
    }

    [Fact]
    public void SuppliedAliasMapReplacesEarlierMapAndOmittedMapInherits()
    {
        var services = new ServiceCollection();
        RegistryConfig.Register(services);
        using var provider = services.BuildServiceProvider();
        var merger = provider.GetRequiredService<IMerger<RegistryConfig>>();
        var earlier = new RegistryConfig { CourseNames = new Dictionary<string, string> { ["Old alias"] = "Old course" } };
        var later = new RegistryConfig { CourseNames = new Dictionary<string, string> { ["New alias"] = "New course" } };

        var replacement = merger.Merge(later, earlier);
        Assert.Single(replacement.CourseNames!);
        Assert.Equal("New course", replacement.CourseNames!["New alias"]);
        var inherited = merger.Merge(new RegistryConfig(),
            new RegistryConfig { CourseNames = new Dictionary<string, string> { ["Old alias"] = "Old course" } });
        Assert.Equal("Old course", inherited.CourseNames!["Old alias"]);
    }
}
