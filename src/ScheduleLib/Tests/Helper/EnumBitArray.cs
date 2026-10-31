using ScheduleLib.Helper;

namespace ScheduleLib.Tests;

public enum TestDay
{
    Monday,
    Tuesday,
    Wednesday,
    Thursday,
    Friday,
    Saturday,
    Sunday,
}

public sealed class EnumBitArrayTests
{
    [Fact]
    public void SetAndIsSet()
    {
        var bitArray = new EnumBitArray<TestDay>();
        bool condition = bitArray.IsSet(TestDay.Monday);
        Assert.False(condition);

        bitArray.Set(TestDay.Monday);
        bool condition2 = bitArray.IsSet(TestDay.Monday);
        Assert.True(condition2);
        bool condition3 = bitArray.IsSet(TestDay.Tuesday);
        Assert.False(condition3);
    }

    [Fact]
    public void Clear()
    {
        var bitArray = new EnumBitArray<TestDay>();
        bitArray.Set(TestDay.Monday);
        bitArray.Set(TestDay.Wednesday);

        bool condition = bitArray.IsSet(TestDay.Monday);
        Assert.True(condition);
        bitArray.Clear(TestDay.Monday);
        bool condition2 = bitArray.IsSet(TestDay.Monday);
        Assert.False(condition2);
        bool condition3 = bitArray.IsSet(TestDay.Wednesday);
        Assert.True(condition3);
    }

    [Fact]
    public void AllSet()
    {
        var bitArray = EnumBitArray<TestDay>.AllSet;
        Assert.True(bitArray.AreAllSet);
        Assert.Equal(expected: 7, actual: bitArray.SetCount);

        foreach (var day in new EnumMembers<TestDay>())
        {
            bool condition = bitArray.IsSet(day);
            Assert.True(condition);
        }
    }

    [Fact]
    public void WithSetImmutable()
    {
        var bitArray = new EnumBitArray<TestDay>();
        var modified = bitArray.WithSet(TestDay.Tuesday);

        bool condition = bitArray.IsSet(TestDay.Tuesday);
        Assert.False(condition);
        bool condition2 = modified.IsSet(TestDay.Tuesday);
        Assert.True(condition2);
    }

    [Fact]
    public void WithClearImmutable()
    {
        var bitArray = EnumBitArray<TestDay>.AllSet;
        var modified = bitArray.WithClear(TestDay.Friday);

        bool condition = bitArray.IsSet(TestDay.Friday);
        Assert.True(condition);
        bool condition2 = modified.IsSet(TestDay.Friday);
        Assert.False(condition2);
    }

    [Fact]
    public void Flipped()
    {
        var bitArray = new EnumBitArray<TestDay>();
        bitArray.Set(TestDay.Monday);
        bitArray.Set(TestDay.Wednesday);

        var flipped = bitArray.Flipped;
        bool condition = flipped.IsSet(TestDay.Monday);
        Assert.False(condition);
        bool condition2 = flipped.IsSet(TestDay.Tuesday);
        Assert.True(condition2);
        bool condition3 = flipped.IsSet(TestDay.Wednesday);
        Assert.False(condition3);
        bool condition4 = flipped.IsSet(TestDay.Thursday);
        Assert.True(condition4);
    }

    [Fact]
    public void Intersect()
    {
        var bitArray1 = new EnumBitArray<TestDay>();
        bitArray1.Set(TestDay.Monday);
        bitArray1.Set(TestDay.Wednesday);
        bitArray1.Set(TestDay.Friday);

        var bitArray2 = new EnumBitArray<TestDay>();
        bitArray2.Set(TestDay.Wednesday);
        bitArray2.Set(TestDay.Friday);
        bitArray2.Set(TestDay.Sunday);

        var result = bitArray1.Intersect(bitArray2);
        bool condition = result.IsSet(TestDay.Monday);
        Assert.False(condition);
        bool condition2 = result.IsSet(TestDay.Wednesday);
        Assert.True(condition2);
        bool condition3 = result.IsSet(TestDay.Friday);
        Assert.True(condition3);
        bool condition4 = result.IsSet(TestDay.Sunday);
        Assert.False(condition4);
    }

    [Fact]
    public void GetFirstSet()
    {
        var bitArray = new EnumBitArray<TestDay>();
        Assert.Null(bitArray.GetFirstSet());

        bitArray.Set(TestDay.Wednesday);
        bitArray.Set(TestDay.Friday);
        Assert.Equal(expected: TestDay.Wednesday, actual: bitArray.GetFirstSet());
    }

    [Fact]
    public void SetValuesEnumeration()
    {
        var bitArray = new EnumBitArray<TestDay>();
        bitArray.Set(TestDay.Tuesday);
        bitArray.Set(TestDay.Thursday);
        bitArray.Set(TestDay.Saturday);

        var values = bitArray.SetValues()
            .ToList();
        Assert.Equal(expected: 3, actual: values.Count);
        Assert.Equal(expected: TestDay.Tuesday, actual: values[0]);
        Assert.Equal(expected: TestDay.Thursday, actual: values[1]);
        Assert.Equal(expected: TestDay.Saturday, actual: values[2]);
    }

    [Fact]
    public void SetValuesEmpty()
    {
        var bitArray = new EnumBitArray<TestDay>();
        var values = bitArray.SetValues()
            .ToList();
        Assert.Empty(values);
    }

    [Fact]
    public void ClearAll()
    {
        var bitArray = EnumBitArray<TestDay>.AllSet;
        Assert.True(bitArray.AreAllSet);

        bitArray.ClearAll();
        Assert.True(bitArray.AreNoneSet);
        Assert.Equal(expected: 0, actual: bitArray.SetCount);
    }

    [Fact]
    public void AreNoneSet()
    {
        var bitArray = new EnumBitArray<TestDay>();
        Assert.True(bitArray.AreNoneSet);

        bitArray.Set(TestDay.Monday);
        Assert.False(bitArray.AreNoneSet);
    }

    [Fact]
    public void SetCount()
    {
        var bitArray = new EnumBitArray<TestDay>();
        Assert.Equal(expected: 0, actual: bitArray.SetCount);

        bitArray.Set(TestDay.Monday);
        Assert.Equal(expected: 1, actual: bitArray.SetCount);

        bitArray.Set(TestDay.Wednesday);
        bitArray.Set(TestDay.Friday);
        Assert.Equal(expected: 3, actual: bitArray.SetCount);
    }

    [Fact]
    public void RemoveTest()
    {
        var arr = EnumBitArray<TestDay>.Empty;
        arr.Set(TestDay.Friday);
        arr.Set(TestDay.Monday);
        arr.Set(TestDay.Sunday);
        arr.Set(TestDay.Wednesday);

        var mask = EnumBitArray<TestDay>.Empty;
        mask.Set(TestDay.Monday);
        mask.Set(TestDay.Sunday);
        mask.Set(TestDay.Saturday);

        arr = arr.Remove(mask);

        Assert.Equal(expected: arr.SetValues(), actual: [TestDay.Wednesday, TestDay.Friday]);
    }
}
