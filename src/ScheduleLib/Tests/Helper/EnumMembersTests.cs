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

        Assert.Equal(expected: [InvalidAndCount.First, InvalidAndCount.Second], actual: list);
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

        Assert.Equal(expected: [Invalid.First, Invalid.Second], actual: list);
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

        Assert.Equal(expected: [ZeroInvalid.First, ZeroInvalid.Second], actual: list);
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

        Assert.Equal(expected: [NoSpecialMembers.First, NoSpecialMembers.Second, NoSpecialMembers.Third], actual: list);
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
        Assert.Equal(expected: TestEnum.First, actual: EnumMembers<TestEnum>.Start);
        Assert.Equal(expected: TestEnum.Second, actual: EnumMembers<TestEnum>.End);
        Assert.Equal(expected: 2, actual: EnumMembers<TestEnum>.Count);
    }

    [Fact]
    public void GetOffsetTest()
    {
        Assert.Equal(expected: 0, actual: EnumMembers<TestEnum>.GetOffset(TestEnum.First));
        Assert.Equal(expected: 1, actual: EnumMembers<TestEnum>.GetOffset(TestEnum.Second));
    }
}
