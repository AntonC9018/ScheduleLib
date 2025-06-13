using ScheduleLib;
using ScheduleLib.Builders;

namespace ScheduleLib.Tests;

public sealed class TeacherNameTests
{
    [Fact]
    public void LastName()
    {
        var name = TeacherNameHelper.ParseName("Lastname");
        Assert.Equal("Lastname", name.LastName);
        Assert.True(name.Name.All(x => x.IsNull));
    }

    [Fact]
    public void RegularFirstLast()
    {
        var name = TeacherNameHelper.ParseName("First Last");
        Assert.Equal("First", name.Name.A.Full);
        Assert.True(name.Name.B.IsNull);
        Assert.Equal("Last", name.LastName);
    }

    [Fact]
    public void ShortFirstName()
    {
        var name = TeacherNameHelper.ParseName("F. Last");
        Assert.Equal("F.", name.Name.A.Short);
        Assert.True(name.Name.B.IsNull);
        Assert.Equal("Last", name.LastName);
    }

    [Fact]
    public void DoubleFirstName()
    {
        var name = TeacherNameHelper.ParseName("Firsta-Firstb Last");
        Assert.Equal("Firsta", name.Name.A.Full);
        Assert.Equal("Firstb", name.Name.B.Full);
        Assert.Equal("Last", name.LastName);
    }

    [Fact]
    public void DoubleLastName_NotAllowed()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var name = TeacherNameHelper.ParseName("First Lasta-Lastb");
            _ = name;
        });
    }

    [Fact]
    public void ShortLast_NotAllowed()
    {
        Assert.Throws<ArgumentException>(() =>
        {
            var name = TeacherNameHelper.ParseName("First L.");
            _ = name;
        });
    }

    [Fact]
    public void CanMixShortAndFullInDoubleNames()
    {
        var name = TeacherNameHelper.ParseName("F.-Firstb Last");
        Assert.Equal("F.", name.Name.A.Short);
        Assert.Equal("Firstb", name.Name.B.Full);
        Assert.Equal("Last", name.LastName);
    }
}
