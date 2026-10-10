using ScheduleLib.Application.Core;
using ScheduleLib.Builders;
using ScheduleLib.Parsing;
using Xunit;

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

        Assert.Equal(vlada.Id, TeacherProfileScheduleLookup.Find(lookup, NameHelper.Parse("Visnevschi Vlada")));
        Assert.NotEqual(other.Id, TeacherProfileScheduleLookup.Find(lookup, NameHelper.Parse("Visnevschi Vlada")));
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

        Assert.Equal(oldSpelling.Id, TeacherProfileScheduleLookup.Find(lookup, NameHelper.Parse("Visnevschi Vlada")));
        Assert.Equal(natalia.Id, TeacherProfileScheduleLookup.Find(lookup, NameHelper.Parse("Plesca Natalia")));
        Assert.Null(TeacherProfileScheduleLookup.Find(lookup, NameHelper.Parse("Unknown Teacher")));
    }
}
