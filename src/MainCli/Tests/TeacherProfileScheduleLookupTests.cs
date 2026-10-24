using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;

public sealed class TeacherProfileScheduleLookupTests
{
    [Fact]
    public void HistoricalProfileSpellingSelectsVladaAmongSharedLastNames()
    {
        var schedule = new ScheduleBuilder();
        schedule.EnableLookupModule();
        var other = schedule.Teacher("Victor Vișnevschii");
        var vlada = schedule.Teacher("Vlada Vișnevschii");
        schedule.RefreshLookup();
        var lookup = schedule.Lookup(null!);

        global::ScheduleLib.TeacherId? expected2 = vlada.Id;
        global::ScheduleLib.Builders.LookupFacade lookup2 = lookup;
        global::ScheduleLib.Parsing.Name profileName = NameHelper.Parse("Visnevschi Vlada");
        global::ScheduleLib.TeacherId? actual2 = TeacherProfileScheduleLookup.Find(lookup2, profileName);
        Assert.Equal(expected: expected2, actual: actual2);
        global::ScheduleLib.TeacherId? expected3 = other.Id;
        global::ScheduleLib.Builders.LookupFacade lookup3 = lookup;
        global::ScheduleLib.Parsing.Name profileName2 = NameHelper.Parse("Visnevschi Vlada");
        global::ScheduleLib.TeacherId? actual3 = TeacherProfileScheduleLookup.Find(lookup3, profileName2);
        Assert.NotEqual(expected: expected3, actual: actual3);
    }

    [Fact]
    public void ExistingScheduleSpellingAndUnrelatedProfilesKeepDirectLookup()
    {
        var schedule = new ScheduleBuilder();
        schedule.EnableLookupModule();
        var oldSpelling = schedule.Teacher("Vlada Visnevschi");
        var natalia = schedule.Teacher("Natalia Plesca");
        schedule.RefreshLookup();
        var lookup = schedule.Lookup(null!);

        global::ScheduleLib.TeacherId? expected2 = oldSpelling.Id;
        global::ScheduleLib.Builders.LookupFacade lookup2 = lookup;
        global::ScheduleLib.Parsing.Name profileName = NameHelper.Parse("Visnevschi Vlada");
        global::ScheduleLib.TeacherId? actual2 = TeacherProfileScheduleLookup.Find(lookup2, profileName);
        Assert.Equal(expected: expected2, actual: actual2);
        global::ScheduleLib.TeacherId? expected3 = natalia.Id;
        global::ScheduleLib.Builders.LookupFacade lookup3 = lookup;
        global::ScheduleLib.Parsing.Name profileName2 = NameHelper.Parse("Plesca Natalia");
        global::ScheduleLib.TeacherId? actual3 = TeacherProfileScheduleLookup.Find(lookup3, profileName2);
        Assert.Equal(expected: expected3, actual: actual3);
        global::ScheduleLib.Builders.LookupFacade lookup4 = lookup;
        global::ScheduleLib.Parsing.Name profileName3 = NameHelper.Parse("Unknown Teacher");
        global::ScheduleLib.TeacherId? value = TeacherProfileScheduleLookup.Find(lookup4, profileName3);
        Assert.Null(value);
    }
}
