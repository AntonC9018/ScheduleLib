using ScheduleLib;
using ScheduleLib.Builders;

namespace App.Tests;

public sealed class TeacherNameTests
{
    [Fact]
    public void LastName()
    {
        var name = TeacherNameHelper.ParseName("Lastname");
        Assert.Equal("Lastname", name.LastName);
        Assert.True(name.FirstName.All(x => x.IsNull));
    }

    [Fact]
    public void RegularFirstLast()
    {
        var name = TeacherNameHelper.ParseName("First Last");
        Assert.Equal("First", name.FirstName.A.Full);
        Assert.True(name.FirstName.B.IsNull);
        Assert.Equal("Last", name.LastName);
    }

    [Fact]
    public void ShortFirstName()
    {
        var name = TeacherNameHelper.ParseName("F. Last");
        Assert.Equal("F.", name.FirstName.A.Short);
        Assert.True(name.FirstName.B.IsNull);
        Assert.Equal("Last", name.LastName);
    }

    [Fact]
    public void DoubleFirstName()
    {
        var name = TeacherNameHelper.ParseName("Firsta-Firstb Last");
        Assert.Equal("Firsta", name.FirstName.A.Full);
        Assert.Equal("Firstb", name.FirstName.B.Full);
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
        Assert.Equal("F.", name.FirstName.A.Short);
        Assert.Equal("Firstb", name.FirstName.B.Full);
        Assert.Equal("Last", name.LastName);
    }
}
