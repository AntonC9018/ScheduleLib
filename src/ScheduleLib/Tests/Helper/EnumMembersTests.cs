using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public sealed class EnumMembersTests
{
    enum InvalidAndCount
    {
        Invalid = -1,
        First = 0,
        Second = 1,
        Count = 2,
    }

    [Fact]
    public void InvalidAndCountTest()
    {
        var members = new EnumMembers<InvalidAndCount>();
        var list = members.ToList();

        Assert.Equal([InvalidAndCount.First, InvalidAndCount.Second], list);
    }

    enum Invalid
    {
        Invalid = -1,
        First = 1,
        Second = 2,
    }

    [Fact]
    public void InvalidTest()
    {
        var members = new EnumMembers<Invalid>();
        var list = members.ToList();

        Assert.Equal([Invalid.First, Invalid.Second], list);
    }

    enum ZeroInvalid
    {
        Invalid = 0,
        First = 1,
        Second = 2,
    }

    [Fact]
    public void ZeroInvalidTest()
    {
        var members = new EnumMembers<ZeroInvalid>();
        var list = members.ToList();

        Assert.Equal([ZeroInvalid.First, ZeroInvalid.Second], list);
    }

    enum NoSpecialMembers
    {
        First = 0,
        Second = 1,
        Third = 2,
    }

    [Fact]
    public void NoSpecialMembersTest()
    {
        var members = new EnumMembers<NoSpecialMembers>();
        var list = members.ToList();

        Assert.Equal([NoSpecialMembers.First, NoSpecialMembers.Second, NoSpecialMembers.Third], list);
    }

    enum TestEnum
    {
        Invalid = -1,
        First = 0,
        Second = 1,
        Count = 2,
    }

    [Fact]
    public void StaticPropertiesTest()
    {
        Assert.Equal(TestEnum.First, EnumMembers<TestEnum>.Start);
        Assert.Equal(TestEnum.Second, EnumMembers<TestEnum>.End);
        Assert.Equal(2, EnumMembers<TestEnum>.Count);
    }

    [Fact]
    public void GetOffsetTest()
    {
        Assert.Equal(0, EnumMembers<TestEnum>.GetOffset(TestEnum.First));
        Assert.Equal(1, EnumMembers<TestEnum>.GetOffset(TestEnum.Second));
    }
}
