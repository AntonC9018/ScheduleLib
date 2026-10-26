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

        ScheduleLib.TeacherId? expected2 = vlada.Id;
        var lookup2 = lookup;
        var profileName = NameHelper.Parse("Visnevschi Vlada");
        var actual2 = TeacherProfileScheduleLookup.Find(lookup2, profileName);
        Assert.Equal(expected: expected2, actual: actual2);
        ScheduleLib.TeacherId? expected3 = other.Id;
        var lookup3 = lookup;
        var profileName2 = NameHelper.Parse("Visnevschi Vlada");
        var actual3 = TeacherProfileScheduleLookup.Find(lookup3, profileName2);
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

        ScheduleLib.TeacherId? expected2 = oldSpelling.Id;
        var lookup2 = lookup;
        var profileName = NameHelper.Parse("Visnevschi Vlada");
        var actual2 = TeacherProfileScheduleLookup.Find(lookup2, profileName);
        Assert.Equal(expected: expected2, actual: actual2);
        ScheduleLib.TeacherId? expected3 = natalia.Id;
        var lookup3 = lookup;
        var profileName2 = NameHelper.Parse("Plesca Natalia");
        var actual3 = TeacherProfileScheduleLookup.Find(lookup3, profileName2);
        Assert.Equal(expected: expected3, actual: actual3);
        var lookup4 = lookup;
        var profileName3 = NameHelper.Parse("Unknown Teacher");
        var value = TeacherProfileScheduleLookup.Find(lookup4, profileName3);
        Assert.Null(value);
    }
}
